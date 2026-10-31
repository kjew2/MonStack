using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IdleRng.Bots.Report;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

namespace IdleRng.Bots.Golden
{
    /// <summary>
    /// golden/battle-sim-vectors.txt (specs/16 section 8): fixed fights through BattleSim.Resolve, covering 1v1, 4v1 and 4v4
    /// plus every skill kind, a trigger and every chase capstone rule modifier. Any change to the battle rules or the content they read moves
    /// this file. The fighters are placeholder monsters defined here until species content exists (specs/13 T3).
    /// </summary>
    public static class BattleVectors
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>One fixed fight: side A against side B at a seed. Public so the server spike (server/ugs) sends the same fights.</summary>
        public sealed record Vector(string Name, ulong Seed, Fighter[] A, Fighter[] B);

        public static string Generate(ContentDb db)
        {
            var rules = FightRules.FromContent(db);
            var sb = new StringBuilder();
            sb.Append("# battle-sim vectors, hash v").Append(BattleSim.HashVersion)
              .Append(" content=").Append(TableWriter.ContentTag(db.Version, db.ContentHash)).Append('\n');
            foreach (var v in Vectors())
            {
                var r = BattleSim.Resolve(rules, v.A, v.B, v.Seed);
                sb.Append(string.Format(Inv, "\n{0} seed={1} outcome={2} ticks={3}\n", v.Name, v.Seed, r.Outcome, r.Ticks));
                foreach (var u in r.Final.Units)
                {
                    var t = u.Tally;
                    sb.Append(string.Format(Inv,
                        "  {0}{1} {2,-8} hp {3,9:0.00}/{4,9:0.00} act {5,3} dealt {6,10:0.00} taken {7,10:0.00} heal {8,9:0.00} crit {9,2} evade {10,2} kill {11} counter {12,2}\n",
                        u.Side == 0 ? "A" : "B", u.Slot, u.Id, u.Hp, u.MaxHp, t.Actions, t.DamageDealt, t.DamageTaken, t.HealingDone,
                        t.Crits, t.Evades, t.Kills, t.Counters));
                }
                sb.Append("  hash ").Append(r.Hash).Append('\n');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ fixtures

        static MonsterStats S(double hp, double str, double intel, double wis, double dex, double def, double spd, double luk) =>
            new MonsterStats { Hp = hp, Str = str, Int = intel, Wis = wis, Dex = dex, Def = def, Spd = spd, Luk = luk };

        static CombatSkill Dmg(string id, string scales, double power, int cd, SkillTarget target = SkillTarget.Enemy, string element = null) =>
            new CombatSkill { Id = id, Kind = SkillKind.Damage, Target = target, Scales = scales, Power = power, Cooldown = cd, Element = element };

        static CombatSkill Fx(string id, SkillKind kind, SkillTarget target, string stat, string scales, double power, int cd, int dur) =>
            new CombatSkill { Id = id, Kind = kind, Target = target, Stat = stat, Scales = scales, Power = power, Cooldown = cd, Duration = dur };

        static Fighter Brawler(string id, string el = "fire") => new Fighter
        {
            Id = id, Element = el, Stats = S(320, 420, 80, 150, 260, 220, 240, 180),
            Skills = { Dmg("heavy_strike", StatIds.Str, 2.0, 3) },
        };

        static Fighter Mage(string id, string el = "water") => new Fighter
        {
            Id = id, Element = el, Stats = S(240, 60, 450, 300, 180, 140, 220, 200),
            Skills = { Dmg("tidal_wave", StatIds.Int, 0.9, 4, SkillTarget.AllEnemies), Dmg("bolt", StatIds.Int, 1.6, 2) },
        };

        static Fighter Tank(string id, string el = "nature") => new Fighter
        {
            Id = id, Element = el, Stats = S(520, 200, 60, 240, 120, 480, 150, 100),
            Skills =
            {
                Fx("taunt", SkillKind.Taunt, SkillTarget.Self, null, null, 0, 4, 3),
                Fx("guard", SkillKind.Shield, SkillTarget.AllAllies, null, StatIds.Def, 0.6, 5, 3),
                Fx("iron_skin", SkillKind.Passive, SkillTarget.Self, StatIds.Def, null, 0.2, 0, 0),
            },
            Rules = { RuleModifiers.CounterMitigated },
        };

        static Fighter Healer(string id, string el = "light") => new Fighter
        {
            Id = id, Element = el, Stats = S(300, 80, 260, 420, 200, 180, 230, 160),
            Skills =
            {
                Fx("mend", SkillKind.Heal, SkillTarget.LowestAlly, null, StatIds.Wis, 1.2, 2, 0),
                Fx("blessing", SkillKind.Heal, SkillTarget.AllAllies, null, StatIds.Wis, 0.5, 5, 0),
            },
        };

        static Fighter Support(string id, string el = "dark") => new Fighter
        {
            Id = id, Element = el, Stats = S(260, 180, 200, 220, 340, 160, 320, 300),
            Skills =
            {
                Fx("rally", SkillKind.Buff, SkillTarget.AllAllies, StatIds.Str, null, 0.25, 4, 3),
                Fx("hex", SkillKind.Debuff, SkillTarget.AllEnemies, StatIds.Def, null, 0.2, 4, 3),
            },
            Triggers = { new TriggeredSkill(FightTrigger.OnCrit, Fx("lucky_break", SkillKind.Buff, SkillTarget.Self, StatIds.CritRate, null, 0.1, 0, 2)) },
        };

        static Fighter Boss(string id) => new Fighter
        {
            Id = id, Element = "dark", Stats = S(999, 520, 400, 380, 200, 360, 260, 120),
            Skills = { Dmg("cataclysm", StatIds.Int, 0.8, 4, SkillTarget.AllEnemies), Dmg("crush", StatIds.Str, 1.8, 2) },
        };

        // Chase capstone carriers (specs/15 section 2): one rule modifier each.
        static Fighter Tide(string id) => new Fighter
        {
            Id = id, Element = "water", Stats = S(480, 120, 200, 300, 160, 300, 200, 120),
            Skills = { Fx("regenerate", SkillKind.Heal, SkillTarget.Self, null, StatIds.Wis, 0.8, 3, 0), Dmg("slam", StatIds.Str, 1.4, 2) },
            Rules = { RuleModifiers.TidalOverflow },
        };

        static Fighter Sovereign(string id) => new Fighter
        {
            Id = id, Element = "fire", Stats = S(360, 400, 120, 160, 180, 260, 210, 140),
            Skills = { Dmg("flame", StatIds.Str, 1.6, 2, SkillTarget.Enemy, "fire") },
            Rules = { RuleModifiers.FirePierce },
        };

        static Fighter Bloom(string id) => new Fighter
        {
            Id = id, Element = "water", Stats = S(360, 80, 160, 380, 160, 200, 210, 140),
            Skills = { Dmg("surge", StatIds.Wis, 1.0, 3, SkillTarget.AllEnemies, "water") },
            Rules = { RuleModifiers.AbyssalDrain },
        };

        static Fighter Starlit(string id) => new Fighter
        {
            Id = id, Element = "light", Stats = S(240, 60, 380, 220, 260, 140, 330, 220),
            Skills = { Dmg("lance", StatIds.Spd, 1.4, 2, SkillTarget.Enemy, "light") },
            Rules = { RuleModifiers.DoubleTurnStart },
        };

        static Fighter Echo(string id)
        {
            var f = Mage(id, "dark");
            f.Rules.Add(RuleModifiers.EchoCast);
            return f;
        }

        // The Mandrake's and Shade Hound's capstones, two per carrier (specs/24 section 5.1).
        static Fighter Rootwood(string id) => new Fighter
        {
            Id = id, Element = "nature", Stats = S(520, 140, 160, 360, 100, 320, 140, 140),
            Skills = { Dmg("scream", StatIds.Wis, 1.1, 3, SkillTarget.AllEnemies, "nature") },
            Rules = { RuleModifiers.RootRebirth, RuleModifiers.WailStagger },
        };

        static Fighter Shade(string id) => new Fighter
        {
            Id = id, Element = "dark", Stats = S(260, 360, 100, 140, 420, 160, 380, 260),
            Skills = { Dmg("devour", StatIds.Str, 1.8, 3, SkillTarget.Enemy, "dark") },
            Rules = { RuleModifiers.UmbralVeil, RuleModifiers.NightFeast },
        };

        public static IEnumerable<Vector> Vectors()
        {
            yield return new Vector("1v1 brawler vs mage", 1, new[] { Brawler("brawler") }, new[] { Mage("mage") });
            yield return new Vector("1v1 brawler vs counter tank", 2, new[] { Brawler("brawler") }, new[] { Tank("tank") });
            yield return new Vector("4v1 party vs boss", 3,
                new[] { Tank("tank"), Brawler("brawler"), Mage("mage"), Healer("healer") }, new[] { Boss("boss") });
            yield return new Vector("4v4 mirror elements", 4,
                new[] { Tank("a_tank"), Brawler("a_brawler"), Mage("a_mage"), Support("a_support") },
                new[] { Tank("b_tank", "fire"), Brawler("b_brawler", "water"), Mage("b_mage", "nature"), Healer("b_healer", "dark") });
            yield return new Vector("2v3 uneven", 5,
                new[] { Brawler("a_brawler", "light"), Support("a_support") }, new[] { Mage("b_mage"), Mage("b_mage2", "fire"), Healer("b_healer") });
            yield return new Vector("1v1 tidal overflow vs fire pierce", 6, new[] { Tide("tide") }, new[] { Sovereign("sovereign") });
            yield return new Vector("2v2 drain and echo vs double turn", 7,
                new[] { Bloom("a_bloom"), Echo("a_echo") }, new[] { Starlit("b_starlit"), Brawler("b_brawler") });
            yield return new Vector("2v2 rebirth and stagger vs veil and feast", 8,
                new[] { Rootwood("a_root"), Mage("a_mage") }, new[] { Shade("b_shade"), Healer("b_healer") });
        }
    }
}
