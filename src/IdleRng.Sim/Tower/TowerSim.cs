using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Tower
{
    /// <summary>One floor of a day's lineup: its boss and level.</summary>
    public sealed class TowerFloorPlan
    {
        public int Floor { get; set; }
        public string BossId { get; set; }
        public bool Marquee { get; set; }
        public double Level { get; set; }
    }

    /// <summary>One floor the team fought.</summary>
    public sealed class TowerFloor
    {
        public int Floor { get; set; }
        public string BossId { get; set; }
        public bool Marquee { get; set; }
        public bool Won { get; set; }
        public int Ticks { get; set; }

        /// <summary>Boss phases entered, in order (phase kinds).</summary>
        public List<string> Phases { get; set; } = new List<string>();

        /// <summary>Entropy vents on either side.</summary>
        public int Vents { get; set; }
        public int PeakCorruption { get; set; }
        public string FightHash { get; set; }

        /// <summary>What each team monster did on this floor, in slot order (the damage chart). Not hashed: it is read off the fight.</summary>
        public List<TeamTally> Team { get; set; } = new List<TeamTally>();
    }

    /// <summary>
    /// One team monster's totals on one floor, from its fight tallies (specs/16 section 6.3): damage dealt and taken (HP lost,
    /// after shields) and healing done. The Dungeon and Endless Tower result cards chart them (specs/21 section 8).
    /// </summary>
    public sealed class TeamTally
    {
        public double Dealt { get; set; }
        public double Taken { get; set; }
        public double Healed { get; set; }

        /// <summary>Side A's monsters at the end of a fight, in slot order (adds excluded).</summary>
        public static List<TeamTally> Of(FightState s)
        {
            var list = new List<TeamTally>();
            foreach (var u in s.Units)
                if (u.Side == 0 && !u.IsAdd) list.Add(new TeamTally { Dealt = u.Tally.DamageDealt, Taken = u.Tally.DamageTaken, Healed = u.Tally.HealingDone });
            return list;
        }
    }

    /// <summary>
    /// The end of one Dungeon attempt (specs/21 section 2). FloorReached is the floor the team fell on, or TOWER_FLOORS + 1
    /// when it cleared every floor; FloorsCleared = FloorReached - 1.
    /// </summary>
    public sealed class TowerResult
    {
        public int Day { get; set; }
        public int FloorReached { get; set; }
        public bool Cleared { get; set; }
        public int FloorsCleared => FloorReached - 1;
        public List<TowerFloor> Floors { get; set; } = new List<TowerFloor>();
        public string Hash { get; set; }
    }

    /// <summary>
    /// The Dungeon (specs/21, `specs/13` T10): a team of 1 to 4 Vault monsters climbs TOWER_FLOORS 4v1 boss floors, at full
    /// HP each floor, with the tower rules on (entropy, phases, corruption). Everything derives from the content, the team and the
    /// UTC day number, so every player gets the same lineup on a day and a result replays exactly. The sim has no clock: the
    /// caller passes the day.
    /// </summary>
    public static class TowerSim
    {
        public const string LineupStream = "tower", FightStream = "tower_fight", DropStream = "drop";
        public const int HashVersion = 1;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-TOWER-v1");

        /// <summary>The day's seed: Derive(StreamSeed(TOWER_SEED, "tower"), day). Same for every player.</summary>
        public static ulong DaySeed(ContentDb c, int day)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day), "day must be >= 0");
            return SplitMix64.Derive(StreamNames.StreamSeed((ulong)c.Constants.TowerSeed, LineupStream), (ulong)day);
        }

        public static bool IsMilestone(ContentDb c, int floor) => floor % c.Constants.TowerMilestoneEvery == 0;

        /// <summary>
        /// The day's floors: marquee bosses on milestone floors, regular bosses on the rest, each drawn from its pool on the
        /// lineup stream without repeats until the pool is used up (then it refills in CSV order). Level: TOWER_BASE x
        /// TOWER_GROWTH^(floor-1) by repeated multiplication.
        /// </summary>
        public static List<TowerFloorPlan> Lineup(ContentDb c, int day)
        {
            var rng = new RngStream(LineupStream, DaySeed(c, day));
            var regular = new Deck(c.TowerRegulars);
            var marquee = new Deck(c.TowerMarquees);
            var k = c.Constants;
            var plan = new List<TowerFloorPlan>(k.TowerFloors);
            double level = k.TowerBase;
            for (int f = 1; f <= k.TowerFloors; f++)
            {
                bool m = IsMilestone(c, f);
                var boss = (m ? marquee : regular).Draw(rng);
                plan.Add(new TowerFloorPlan { Floor = f, BossId = boss.Id, Marquee = m, Level = level });
                level *= k.TowerGrowth;
            }
            return plan;
        }

        sealed class Deck
        {
            readonly IReadOnlyList<TowerBossDef> _pool;
            readonly List<TowerBossDef> _left = new List<TowerBossDef>();
            public Deck(IReadOnlyList<TowerBossDef> pool) { _pool = pool; }

            public TowerBossDef Draw(IRandomSource rng)
            {
                if (_left.Count == 0) _left.AddRange(_pool);
                int i = Math.Min(_left.Count - 1, (int)(rng.NextDouble() * _left.Count));
                var b = _left[i];
                _left.RemoveAt(i);
                return b;
            }
        }

        /// <summary>A boss (or add) as a fighter at <paramref name="level"/>: stats = weights x level, hp also x TOWER_BOSS_HP_MULT for non-adds.</summary>
        public static Fighter Boss(ContentDb c, string bossId, double level)
        {
            var d = c.TowerBosses[bossId];
            var w = d.Weights;
            double hpMult = d.Role == TowerRole.Add ? 1.0 : c.Constants.TowerBossHpMult;
            var f = new Fighter
            {
                Id = "boss_" + d.Id, Element = d.Element,
                Stats = new MonsterStats
                {
                    Hp = w.Hp * level * hpMult, Str = w.Str * level, Int = w.Int * level, Wis = w.Wis * level,
                    Dex = w.Dex * level, Def = w.Def * level, Spd = w.Spd * level, Luk = w.Luk * level,
                },
            };
            foreach (var sk in d.Skills) f.Skills.Add(c.MonsterSkills[sk]);
            foreach (var p in d.Phases)
            {
                var bp = new BossPhase
                {
                    EnterHp = p.EnterHp, Kind = p.Kind, Power = p.Power, Duration = p.Duration, Count = p.Count, Guard = p.Guard,
                    HealFrac = p.HealFrac, Strike = p.Strike, Buff = p.Buff,
                    Add = p.Adds == null ? null : Boss(c, p.Adds, level),
                };
                foreach (var sk in p.AddSkills) bp.AddSkills.Add(c.MonsterSkills[sk]);
                f.Phases.Add(bp);
            }
            return f;
        }

        /// <summary>The team with fighter ids made unique by slot ("t0_monster", ...): every vaulted monster has the same id.</summary>
        public static Fighter[] Team(IReadOnlyList<Fighter> team, int sideSize)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            if (team.Count < 1 || team.Count > sideSize) throw new ArgumentException($"a tower team needs 1..{sideSize} monsters, got {team.Count}", nameof(team));
            var t = new Fighter[team.Count];
            for (int i = 0; i < team.Count; i++)
            {
                var f = team[i] ?? throw new ArgumentException($"team[{i}] is null", nameof(team));
                t[i] = f.WithId("t" + i + "_" + f.Id);
            }
            return t;
        }

        public static ulong FloorSeed(ContentDb c, int day, int floor) =>
            SplitMix64.Derive(StreamNames.StreamSeed(DaySeed(c, day), FightStream), (ulong)floor);

        /// <summary>
        /// One attempt: the team (VaultEntry.Fighter for each chosen entry, in slot order) climbs the day's floors until it loses
        /// or times out. <paramref name="onFloorStart"/> gets each floor's plan and fresh fight state before its first tick;
        /// <paramref name="onAction"/> sees every action. Observers only read.
        /// </summary>
        public static TowerResult Run(ContentDb c, IReadOnlyList<Fighter> team, int day,
                                      Action<FightAction> onAction = null, Action<TowerFloorPlan, FightState> onFloorStart = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var rules = FightRules.FromContent(c, tower: true);
            var side = Team(team, rules.SideSize);
            var result = new TowerResult { Day = day };
            foreach (var plan in Lineup(c, day))
            {
                var floor = new TowerFloor { Floor = plan.Floor, BossId = plan.BossId, Marquee = plan.Marquee };
                void Watch(FightAction a)
                {
                    if (a.Event != null)
                    {
                        if (a.Event.StartsWith("phase:", StringComparison.Ordinal)) floor.Phases.Add(a.Event.Substring(6));
                        else if (a.Event.StartsWith("vent:", StringComparison.Ordinal)) floor.Vents++;
                    }
                    onAction?.Invoke(a);
                }
                var fight = BattleSim.Resolve(rules, side, new[] { Boss(c, plan.BossId, plan.Level) }, FloorSeed(c, day, plan.Floor),
                    Watch, onFloorStart == null ? null : (Action<FightState>)(s => onFloorStart(plan, s)));
                floor.Won = fight.Outcome == FightOutcome.SideAWon;
                floor.Ticks = fight.Ticks;
                floor.PeakCorruption = fight.Final.PeakCorruption;
                floor.FightHash = fight.Hash;
                floor.Team = TeamTally.Of(fight.Final);
                result.Floors.Add(floor);
                if (!floor.Won) { result.FloorReached = plan.Floor; break; }
            }
            if (result.FloorReached == 0)
            {
                result.Cleared = true;
                result.FloorReached = c.Constants.TowerFloors + 1;
            }
            result.Hash = Hash(result);
            return result;
        }

        /// <summary>
        /// The Skill Book a cleared floor drops on a day's <paramref name="attempt"/>-th attempt (0 for the day's first), or null
        /// (specs/21 section 7): one roll with chance TOWER_BOOK_CHANCE + TOWER_BOOK_CHANCE_PER_FLOOR x (floor - 1), then a
        /// weighted draw over book_drops rows with min_floor &lt;= floor. Fixed by (day, attempt, floor): every ticket rolls
        /// afresh, and the drops stay reproducible.
        /// </summary>
        public static string BookDrop(ContentDb c, int day, int floor, int attempt = 0)
        {
            if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt), "attempt must be >= 0");
            var k = c.Constants;
            ulong seed = SplitMix64.Derive(StreamNames.StreamSeed(DaySeed(c, day), DropStream), (ulong)floor);
            if (attempt > 0) seed = SplitMix64.Derive(seed, (ulong)attempt);   // attempt 0 keeps the first-version seed
            var rng = new RngStream(DropStream, seed);
            double chance = k.TowerBookChance + k.TowerBookChancePerFloor * (floor - 1);
            if (!(rng.NextDouble() < chance)) return null;
            double total = 0;
            foreach (var b in c.BookDrops) if (b.MinFloor <= floor) total += b.Weight;
            if (!(total > 0)) return null;
            double pick = rng.NextDouble() * total;
            BookDropDef last = null;
            foreach (var b in c.BookDrops)
            {
                if (b.MinFloor > floor) continue;
                last = b;
                if (pick < b.Weight) return b.BookId;
                pick -= b.Weight;
            }
            return last?.BookId;
        }

        public static string BossName(ContentDb c, string bossId) =>
            c.TowerBosses.TryGetValue(bossId, out var d) ? c.Text(d.NameKey) : bossId;

        static string Hash(TowerResult r)
        {
            var w = new CanonicalWriter(256);
            w.Bytes(HashMagic);
            w.I32(r.Day); w.I32(r.FloorReached); w.Bool(r.Cleared);
            w.I32(r.Floors.Count);
            foreach (var f in r.Floors)
            {
                w.I32(f.Floor); w.Str(f.BossId); w.Bool(f.Marquee); w.Bool(f.Won); w.I32(f.Ticks);
                w.I32(f.Phases.Count);
                foreach (var p in f.Phases) w.Str(p);
                w.I32(f.Vents); w.I32(f.PeakCorruption); w.Str(f.FightHash);
            }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
