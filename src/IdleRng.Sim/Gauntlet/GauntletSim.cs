using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Rng;

namespace IdleRng.Sim.Gauntlet
{
    /// <summary>One floor the monster fought.</summary>
    public sealed class GauntletFloor
    {
        public int Floor { get; set; }
        public string EnemyId { get; set; }
        public bool Boss { get; set; }
        public bool Won { get; set; }
        public int Ticks { get; set; }
        public string FightHash { get; set; }
    }

    /// <summary>
    /// The end of a gauntlet (specs/19): where the wheel stops (FloorReached and Killer), the Legacy Title and every floor
    /// fought. A cleared tower has Cleared = true, FloorReached = GAUNTLET_FLOORS + 1 and no killer.
    /// </summary>
    public sealed class GauntletResult
    {
        public int FloorReached { get; set; }
        public bool Cleared { get; set; }

        /// <summary>Gauntlet enemy id that beat the monster, or null when it cleared the tower.</summary>
        public string KillerId { get; set; }

        /// <summary>The Legacy Title's string key (its text may contain {killer}).</summary>
        public string TitleKey { get; set; }

        public List<GauntletFloor> Floors { get; set; } = new List<GauntletFloor>();

        /// <summary>SHA-256 over the floors fought (each fight's own hash included).</summary>
        public string Hash { get; set; }
    }

    /// <summary>
    /// The end-of-run gauntlet (specs/19, `specs/13` T8): the finished monster climbs a tower of GAUNTLET_FLOORS escalating
    /// 1v1 fights, at full HP each floor, until it loses or times out. Pure and deterministic: each floor's fight seed derives
    /// from the caller's seed and the floor number, so the result replays exactly for the wheel animation and the Vault.
    /// </summary>
    public static class GauntletSim
    {
        public const string Stream = "gauntlet";
        public const int HashVersion = 1;
        static readonly byte[] HashMagic = Encoding.ASCII.GetBytes("IDLERNG-GAUNTLET-v1");

        /// <summary>
        /// The enemy level of every floor 1..GAUNTLET_FLOORS ([0] is floor 1): GAUNTLET_BASE x GAUNTLET_GROWTH^(floor-1) by
        /// repeated multiplication (no Math.Pow), x GAUNTLET_BOSS_MULT on boss floors.
        /// </summary>
        public static double[] Levels(ContentDb c)
        {
            var k = c.Constants;
            var levels = new double[k.GauntletFloors];
            double level = k.GauntletBase;
            for (int f = 1; f <= k.GauntletFloors; f++)
            {
                levels[f - 1] = IsBoss(c, f) ? level * k.GauntletBossMult : level;
                level *= k.GauntletGrowth;
            }
            return levels;
        }

        public static bool IsBoss(ContentDb c, int floor) => floor % c.Constants.GauntletBossEvery == 0;

        /// <summary>The enemy on <paramref name="floor"/>: bosses rotate on boss floors, normal enemies on the others.</summary>
        public static GauntletEnemyDef EnemyDef(ContentDb c, int floor)
        {
            if (IsBoss(c, floor))
                return c.GauntletBosses[(floor / c.Constants.GauntletBossEvery - 1) % c.GauntletBosses.Count];
            return c.GauntletEnemies[(floor - 1) % c.GauntletEnemies.Count];
        }

        public static Fighter Enemy(ContentDb c, int floor, double level)
        {
            var d = EnemyDef(c, floor);
            var w = d.Weights;
            return new Fighter
            {
                Id = "floor_" + floor + "_" + d.Id, Element = d.Element,
                Stats = new MonsterStats
                {
                    Hp = w.Hp * level, Str = w.Str * level, Int = w.Int * level, Wis = w.Wis * level,
                    Dex = w.Dex * level, Def = w.Def * level, Spd = w.Spd * level, Luk = w.Luk * level,
                },
            };
        }

        /// <summary>Seed of the fight on <paramref name="floor"/>.</summary>
        public static ulong FloorSeed(ulong seed, int floor) => SplitMix64.Derive(StreamNames.StreamSeed(seed, Stream), (ulong)floor);

        /// <summary>The Legacy Title for a final floor: the last title whose MinFloor is at most <paramref name="floorReached"/>.</summary>
        public static LegacyTitleDef Title(ContentDb c, int floorReached)
        {
            var best = c.LegacyTitles[0];
            foreach (var t in c.LegacyTitles) if (t.MinFloor <= floorReached) best = t;
            return best;
        }

        /// <summary>
        /// Climb the tower with <paramref name="monster"/> (for a vaulted monster, VaultEntry.Fighter; seed, the run seed).
        /// Every floor is a fresh 1v1 BattleSim fight; only a win (SideAWon) goes on to the next floor.
        /// </summary>
        public static GauntletResult Run(ContentDb c, Fighter monster, ulong seed)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (monster == null) throw new ArgumentNullException(nameof(monster));
            var rules = FightRules.FromContent(c);
            var levels = Levels(c);
            var result = new GauntletResult();
            for (int f = 1; f <= levels.Length; f++)
            {
                var enemy = Enemy(c, f, levels[f - 1]);
                var fight = BattleSim.Resolve(rules, new[] { monster }, new[] { enemy }, FloorSeed(seed, f));
                bool won = fight.Outcome == FightOutcome.SideAWon;
                var d = EnemyDef(c, f);
                result.Floors.Add(new GauntletFloor { Floor = f, EnemyId = d.Id, Boss = d.Boss, Won = won, Ticks = fight.Ticks, FightHash = fight.Hash });
                if (won) continue;
                result.FloorReached = f;
                result.KillerId = d.Id;
                break;
            }
            if (result.KillerId == null)
            {
                result.Cleared = true;
                result.FloorReached = levels.Length + 1;
            }
            result.TitleKey = Title(c, result.FloorReached).TitleKey;
            result.Hash = Hash(result);
            return result;
        }

        static string Hash(GauntletResult r)
        {
            var w = new CanonicalWriter(256);
            w.Bytes(HashMagic);
            w.I32(r.FloorReached); w.Bool(r.Cleared); w.Str(r.KillerId); w.Str(r.TitleKey);
            w.I32(r.Floors.Count);
            foreach (var f in r.Floors) { w.I32(f.Floor); w.Str(f.EnemyId); w.Bool(f.Boss); w.Bool(f.Won); w.I32(f.Ticks); w.Str(f.FightHash); }
            return Sha256.Hex(Sha256.Compute(w.ToArray()));
        }
    }
}
