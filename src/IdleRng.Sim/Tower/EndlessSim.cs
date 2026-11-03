using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Tower
{
    /// <summary>One Endless Tower floor of a season: its squad and level (specs/28 section 3).</summary>
    public sealed class EndlessFloorPlan
    {
        public int Floor { get; set; }
        public string ShapeId { get; set; }

        /// <summary>The band's last floor: slot 0 is a Dungeon marquee boss (<see cref="EliteId"/>).</summary>
        public bool Elite { get; set; }
        public string EliteId { get; set; }

        /// <summary>endless_enemies ids by slot; on an elite floor slot 0 is the elite's tower_bosses id.</summary>
        public List<string> EnemyIds { get; set; } = new List<string>();
        public double Level { get; set; }

        /// <summary>How many of the season's mutators are in force on this floor: floor / ENDLESS_BAND.</summary>
        public int Mutators { get; set; }
    }

    /// <summary>One floor the team fought.</summary>
    public sealed class EndlessFloor
    {
        public int Floor { get; set; }
        public string ShapeId { get; set; }
        public bool Elite { get; set; }
        public bool Won { get; set; }
        public int Ticks { get; set; }

        /// <summary>Boss phases the elite entered, in order (phase kinds).</summary>
        public List<string> Phases { get; set; } = new List<string>();
        public int Vents { get; set; }
        public int PeakCorruption { get; set; }
        public string FightHash { get; set; }
    }

    /// <summary>
    /// The end of one Endless Tower climb (specs/28 section 2). FloorReached is the floor the team fell on, or
    /// ENDLESS_MAX_FLOORS + 1 when it never lost (Capped); FloorsCleared = FloorReached - 1.
    /// </summary>
    public sealed class EndlessResult
    {
        public int Season { get; set; }
        public int FloorReached { get; set; }
        public bool Capped { get; set; }
        public int FloorsCleared => FloorReached - 1;
        public List<EndlessFloor> Floors { get; set; } = new List<EndlessFloor>();
        public string Hash { get; set; }
    }

    /// <summary>
    /// The Endless Tower (specs/28, T11): a team of exactly MON_SIDE_SIZE Vault monsters climbs 4v4 floors with no top, at
    /// full HP each floor, with the tower rules on and the season's mutators stacking one per band. Everything derives from the
    /// content, the team (with its rulebooks) and the season number, so every player meets the same floors in a season and a
    /// climb replays exactly. The sim has no clock: the caller passes the season (the UTC week number).
    /// </summary>
    public static class EndlessSim
    {
        public const string LineupStream = "endless", MutatorStream = "endless_mutators", FightStream = "endless_fight";
        public const int HashVersion = 1;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-ENDLESS-v1");

        /// <summary>The season's seed: Derive(StreamSeed(ENDLESS_SEED, "endless"), season). Same for every player.</summary>
        public static ulong SeasonSeed(ContentDb c, int season)
        {
            if (season < 0) throw new ArgumentOutOfRangeException(nameof(season), "season must be >= 0");
            return SplitMix64.Derive(StreamNames.StreamSeed((ulong)c.Constants.EndlessSeed, LineupStream), (ulong)season);
        }

        /// <summary>
        /// The season of a UTC day number (days since 1970-01-01, the Dungeon's day): the UTC week, starting Monday 00:00 UTC.
        /// Day 0 was a Thursday, so days 0 to 3 are season 0 and day 4 (Monday 1970-01-05) starts season 1.
        /// </summary>
        public static int SeasonOf(int day)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day), "day must be >= 0");
            return (int)(((long)day + 3) / 7);
        }

        public static bool IsElite(ContentDb c, int floor) => floor % c.Constants.EndlessBand == 0;

        /// <summary>The floor's level: ENDLESS_BASE x ENDLESS_GROWTH^(floor-1), by repeated multiplication.</summary>
        public static double Level(ContentDb c, int floor)
        {
            double level = c.Constants.EndlessBase;
            for (int f = 1; f < floor; f++) level *= c.Constants.EndlessGrowth;
            return level;
        }

        /// <summary>
        /// One band of a season (specs/28 section 3.1): floors band x B + 1 to (band + 1) x B, B = ENDLESS_BAND. On the band's
        /// own stream: first which regular floor gets an aoe shape and which a focus shape (two different floors), then floor
        /// by floor its shape (from the aoe shapes, the focus shapes or all of them) and one enemy of each slot's role. The
        /// last floor is elite: a marquee boss in slot 0 and a drawn shape's other slots.
        /// </summary>
        public static List<EndlessFloorPlan> Band(ContentDb c, int season, int band)
        {
            if (band < 0) throw new ArgumentOutOfRangeException(nameof(band), "band must be >= 0");
            int b = c.Constants.EndlessBand, first = band * b + 1;
            var rng = new RngStream(LineupStream, SplitMix64.Derive(SeasonSeed(c, season), (ulong)band));
            var all = c.EndlessShapes;
            var aoe = new List<EndlessShapeDef>();
            var focus = new List<EndlessShapeDef>();
            foreach (var s in all)
            {
                if (s.Check == EndlessShapeDef.AoeCheck) aoe.Add(s);
                else if (s.Check == EndlessShapeDef.FocusCheck) focus.Add(s);
            }
            int regular = b - 1;
            int aoeAt = Pick(rng, regular);
            int focusAt = Pick(rng, regular - 1);
            if (focusAt >= aoeAt) focusAt++;

            var plans = new List<EndlessFloorPlan>(b);
            double level = Level(c, first);
            for (int i = 0; i < b; i++)
            {
                int floor = first + i;
                bool elite = i == regular;
                IReadOnlyList<EndlessShapeDef> pool = i == aoeAt ? aoe : i == focusAt ? focus : all;
                var shape = pool[Pick(rng, pool.Count)];
                var plan = new EndlessFloorPlan
                {
                    Floor = floor, ShapeId = shape.Id, Elite = elite, Level = level, Mutators = floor / b,
                };
                if (elite)
                {
                    plan.EliteId = c.TowerMarquees[Pick(rng, c.TowerMarquees.Count)].Id;
                    plan.EnemyIds.Add(plan.EliteId);
                }
                for (int slot = elite ? 1 : 0; slot < shape.Slots.Count; slot++)
                    plan.EnemyIds.Add(DrawEnemy(c, shape.Slots[slot], rng).Id);
                plans.Add(plan);
                level *= c.Constants.EndlessGrowth;
            }
            return plans;
        }

        /// <summary>The season's first <paramref name="floors"/> floors.</summary>
        public static List<EndlessFloorPlan> Lineup(ContentDb c, int season, int floors)
        {
            var list = new List<EndlessFloorPlan>(Math.Max(0, floors));
            for (int band = 0; list.Count < floors; band++)
                foreach (var p in Band(c, season, band))
                    if (list.Count < floors) list.Add(p);
            return list;
        }

        /// <summary>
        /// The season's first <paramref name="count"/> mutators, in the order they join: drawn on the mutator stream without
        /// repeats until the table is used up, then it refills in CSV order. Floor f has the first f / ENDLESS_BAND in force.
        /// </summary>
        public static List<MutatorDef> SeasonMutators(ContentDb c, int season, int count)
        {
            var rng = new RngStream(MutatorStream, StreamNames.StreamSeed(SeasonSeed(c, season), MutatorStream));
            var left = new List<MutatorDef>();
            var list = new List<MutatorDef>(Math.Max(0, count));
            while (list.Count < count)
            {
                if (left.Count == 0) left.AddRange(c.Mutators);
                int i = Pick(rng, left.Count);
                list.Add(left[i]);
                left.RemoveAt(i);
            }
            return list;
        }

        /// <summary>The fight rules for a floor: the tower rules plus the given mutators (null when there are none).</summary>
        public static FightRules Rules(ContentDb c, IReadOnlyList<MutatorDef> mutators)
        {
            var r = FightRules.FromContent(c, tower: true);
            if (mutators != null && mutators.Count > 0)
            {
                var m = new FightMutators();
                foreach (var d in mutators) m.Add(d.Kind, d.Value, d.Element);
                r.Mutators = m;
            }
            return r;
        }

        /// <summary>The floor's squad as fighters, ids "e&lt;slot&gt;_&lt;id&gt;"; enemies carry their content rulebooks.</summary>
        public static Fighter[] Squad(ContentDb c, EndlessFloorPlan plan)
        {
            var shape = Shape(c, plan.ShapeId);
            var squad = new Fighter[plan.EnemyIds.Count];
            for (int slot = 0; slot < squad.Length; slot++)
            {
                string id = plan.EnemyIds[slot];
                if (plan.Elite && slot == 0)
                {
                    var boss = TowerSim.Boss(c, id, plan.Level);
                    boss.Stats.Hp = c.TowerBosses[id].Weights.Hp * plan.Level * c.Constants.EndlessEliteHpMult;
                    squad[slot] = boss.WithId("e0_" + id);
                    continue;
                }
                var d = Enemy(c, id);
                var w = d.Weights;
                double hp = slot == 0 ? shape.AnchorHp : 1.0;
                var f = new Fighter
                {
                    Id = "e" + slot + "_" + d.Id, Element = d.Element,
                    Stats = new MonsterStats
                    {
                        Hp = w.Hp * plan.Level * hp, Str = w.Str * plan.Level, Int = w.Int * plan.Level, Wis = w.Wis * plan.Level,
                        Dex = w.Dex * plan.Level, Def = w.Def * plan.Level, Spd = w.Spd * plan.Level, Luk = w.Luk * plan.Level,
                    },
                };
                foreach (var sk in d.Skills) f.Skills.Add(c.MonsterSkills[sk]);
                f.Gambits.AddRange(d.Gambits);
                squad[slot] = f;
            }
            return squad;
        }

        /// <summary>The team as fighters: exactly MON_SIDE_SIZE monsters (kj, 2026-11-02), renamed "t&lt;slot&gt;_&lt;id&gt;".</summary>
        public static Fighter[] Team(ContentDb c, IReadOnlyList<Fighter> team)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            int size = c.Constants.MonSideSize;
            if (team.Count != size) throw new ArgumentException($"an Endless Tower team needs exactly {size} monsters, got {team.Count}", nameof(team));
            return TowerSim.Team(team, size);
        }

        public static ulong FloorSeed(ContentDb c, int season, int floor) =>
            SplitMix64.Derive(StreamNames.StreamSeed(SeasonSeed(c, season), FightStream), (ulong)floor);

        /// <summary>
        /// Climbs from floor 1 until the team loses or times out, or ENDLESS_MAX_FLOORS is cleared (Capped). Full HP each floor.
        /// <paramref name="onFloorStart"/> gets each floor's plan, its mutators and the fresh fight state before the first tick;
        /// <paramref name="onAction"/> sees every action. Observers only read.
        /// </summary>
        public static EndlessResult Run(ContentDb c, IReadOnlyList<Fighter> team, int season, Action<FightAction> onAction = null,
                                        Action<EndlessFloorPlan, IReadOnlyList<MutatorDef>, FightState> onFloorStart = null)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var side = Team(c, team);
            int max = c.Constants.EndlessMaxFloors;
            var mutators = SeasonMutators(c, season, max / c.Constants.EndlessBand);
            var result = new EndlessResult { Season = season };
            FightRules rules = null;
            int rulesFor = -1;
            for (int band = 0; result.FloorReached == 0; band++)
            {
                foreach (var plan in Band(c, season, band))
                {
                    if (plan.Floor > max) break;
                    if (plan.Mutators != rulesFor)
                    {
                        rules = Rules(c, mutators.GetRange(0, plan.Mutators));
                        rulesFor = plan.Mutators;
                    }
                    var floor = new EndlessFloor { Floor = plan.Floor, ShapeId = plan.ShapeId, Elite = plan.Elite };
                    void Watch(FightAction a)
                    {
                        if (a.Event != null)
                        {
                            if (a.Event.StartsWith("phase:", StringComparison.Ordinal)) floor.Phases.Add(a.Event.Substring(6));
                            else if (a.Event.StartsWith("vent:", StringComparison.Ordinal)) floor.Vents++;
                        }
                        onAction?.Invoke(a);
                    }
                    var active = mutators.GetRange(0, plan.Mutators);
                    var fight = BattleSim.Resolve(rules, side, Squad(c, plan), FloorSeed(c, season, plan.Floor), Watch,
                        onFloorStart == null ? null : (Action<FightState>)(s => onFloorStart(plan, active, s)));
                    floor.Won = fight.Outcome == FightOutcome.SideAWon;
                    floor.Ticks = fight.Ticks;
                    floor.PeakCorruption = fight.Final.PeakCorruption;
                    floor.FightHash = fight.Hash;
                    result.Floors.Add(floor);
                    if (!floor.Won) { result.FloorReached = plan.Floor; break; }
                }
                if (result.FloorReached == 0 && result.Floors.Count >= max)
                {
                    result.Capped = true;
                    result.FloorReached = max + 1;
                }
            }
            result.Hash = Hash(result);
            return result;
        }

        public static EndlessEnemyDef Enemy(ContentDb c, string id)
        {
            foreach (var e in c.EndlessEnemies) if (e.Id == id) return e;
            throw new KeyNotFoundException($"no endless_enemies row '{id}'");
        }

        public static EndlessShapeDef Shape(ContentDb c, string id)
        {
            foreach (var s in c.EndlessShapes) if (s.Id == id) return s;
            throw new KeyNotFoundException($"no endless_shapes row '{id}'");
        }

        static EndlessEnemyDef DrawEnemy(ContentDb c, string role, IRandomSource rng)
        {
            var pool = new List<EndlessEnemyDef>();
            foreach (var e in c.EndlessEnemies) if (e.Role == role) pool.Add(e);
            return pool[Pick(rng, pool.Count)];
        }

        /// <summary>A uniform index in [0, n) from one draw.</summary>
        static int Pick(IRandomSource rng, int n) => Math.Min(n - 1, (int)(rng.NextDouble() * n));

        static string Hash(EndlessResult r)
        {
            var w = new CanonicalWriter(256);
            w.Bytes(HashMagic);
            w.I32(r.Season); w.I32(r.FloorReached); w.Bool(r.Capped);
            w.I32(r.Floors.Count);
            foreach (var f in r.Floors)
            {
                w.I32(f.Floor); w.Str(f.ShapeId); w.Bool(f.Elite); w.Bool(f.Won); w.I32(f.Ticks);
                w.I32(f.Phases.Count);
                foreach (var p in f.Phases) w.Str(p);
                w.I32(f.Vents); w.I32(f.PeakCorruption); w.Str(f.FightHash);
            }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
