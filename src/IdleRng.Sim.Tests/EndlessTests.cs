using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The Endless Tower (specs/28): content, season lineup, squads, mutators, the climb, rulebook text.</summary>
    public class EndlessTests
    {
        static ContentDb Db => TestContent.Db;

        static MonsterStats Sheet(double all) => new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all };

        static Fighter Mon(string id, double all) => new Fighter
        {
            Id = id, Element = "fire", Stats = Sheet(all),
            Skills = { Db.MonsterSkills["flame_breath"], Db.MonsterSkills["smash"] },
        };

        static Fighter[] Team(double all) => Enumerable.Range(0, Db.Constants.MonSideSize).Select(i => Mon("m" + i, all)).ToArray();

        static Dictionary<string, string> Files()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory);
            return Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        }

        static ContentDb WithConstant(string key, double value)
        {
            var files = Files();
            var doc = JsonNode.Parse(files["constants.json"]);
            foreach (var row in doc["rows"].AsArray())
                if ((string)row["key"] == key) row["value"] = value;
            files["constants.json"] = doc.ToJsonString();
            return ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false);
        }

        // ------------------------------------------------------------------ content

        [Test]
        public void Content_Loads()
        {
            Assert.That(Db.EndlessEnemies, Is.Not.Empty);
            Assert.That(Db.EndlessShapes.Any(s => s.Check == EndlessShapeDef.AoeCheck));
            Assert.That(Db.EndlessShapes.Any(s => s.Check == EndlessShapeDef.FocusCheck));
            Assert.That(Db.Mutators, Is.Not.Empty);
            Assert.That(Db.EndlessEnemies.Any(e => e.Gambits.Count > 0), "some enemies carry rulebooks");
            foreach (var s in Db.EndlessShapes) Assert.That(s.Slots.Count, Is.EqualTo(Db.Constants.MonSideSize), s.Id);
        }

        [Test]
        public void Content_IsValidated()
        {
            var files = Files();
            files["endless_enemies.json"] = files["endless_enemies.json"]
                .Replace("\"gambits\": \"always>quick_jab@lowest_hp\"", "\"gambits\": \"always>fireball\"")
                .Replace("\"gambits\": \"first_actions:1>provoke\"", "\"gambits\": \"sometimes>provoke\"");
            files["endless_shapes.json"] = files["endless_shapes.json"].Replace("\"slots\": \"minion|minion|minion|minion\"", "\"slots\": \"minion|minion|minion\"");
            string m = files["mutators.json"];
            int at = m.IndexOf("\"id\": \"haste\"", StringComparison.Ordinal), end = m.IndexOf('}', at);
            files["mutators.json"] = m.Substring(0, at) + m.Substring(at, end - at).Replace("\"value\": 1.3", "\"value\": 0.0") + m.Substring(end);
            var ex = Assert.Throws<ContentLoadException>(() => ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false));
            var msgs = ex.Errors.Select(e => e.Code + " " + e.Location + ": " + e.Message).ToList();
            Assert.That(msgs, Has.Some.Contains("E023 endless_enemies[ember_imp]: gambit action 'fireball' is not one of the enemy's active skills"));
            Assert.That(msgs, Has.Some.Contains("E043 endless_enemies[bastion_golem]: rule 'sometimes>provoke': unknown condition 'sometimes'"));
            Assert.That(msgs, Has.Some.Contains("E043 endless_shapes[swarm]: slots must list exactly MON_SIDE_SIZE (4) roles"));
            Assert.That(msgs, Has.Some.Contains("E043 mutators[haste]: value out of range for kind 'speed_mult'"));
        }

        [Test]
        public void Constants_AreValidated()
        {
            var ex = Assert.Throws<ContentLoadException>(() => WithConstant("ENDLESS_BAND", 2));
            Assert.That(ex.Errors.Select(e => e.Code + " " + e.Location).ToList(), Has.Some.EqualTo("E043 constants"));
        }

        // ------------------------------------------------------------------ lineup

        [Test]
        public void Level_GrowsByRepeatedMultiplication()
        {
            var k = Db.Constants;
            Assert.That(EndlessSim.Level(Db, 1), Is.EqualTo(k.EndlessBase));
            Assert.That(EndlessSim.Level(Db, 3), Is.EqualTo(k.EndlessBase * k.EndlessGrowth * k.EndlessGrowth));
        }

        [Test]
        public void EveryBand_HasAnAoeAndAFocusFloor_AndEndsOnAnElite()
        {
            int b = Db.Constants.EndlessBand;
            for (int season = 0; season < 20; season++)
                for (int band = 0; band < 4; band++)
                {
                    var plans = EndlessSim.Band(Db, season, band);
                    Assert.That(plans.Select(p => p.Floor), Is.EqualTo(Enumerable.Range(band * b + 1, b)));
                    var regular = plans.Where(p => !p.Elite).ToList();
                    Assert.That(regular.Count, Is.EqualTo(b - 1));
                    Assert.That(regular.Any(p => EndlessSim.Shape(Db, p.ShapeId).Check == EndlessShapeDef.AoeCheck), $"season {season} band {band}: an aoe floor");
                    Assert.That(regular.Any(p => EndlessSim.Shape(Db, p.ShapeId).Check == EndlessShapeDef.FocusCheck), $"season {season} band {band}: a focus floor");
                    var elite = plans.Last();
                    Assert.That(elite.Elite && EndlessSim.IsElite(Db, elite.Floor));
                    Assert.That(Db.TowerMarquees.Select(m => m.Id), Does.Contain(elite.EliteId));
                    Assert.That(elite.EnemyIds[0], Is.EqualTo(elite.EliteId));
                    foreach (var p in plans)
                    {
                        Assert.That(p.EnemyIds.Count, Is.EqualTo(Db.Constants.MonSideSize));
                        Assert.That(p.Mutators, Is.EqualTo(p.Floor / b));
                        var shape = EndlessSim.Shape(Db, p.ShapeId);
                        for (int slot = p.Elite ? 1 : 0; slot < shape.Slots.Count; slot++)
                            Assert.That(EndlessSim.Enemy(Db, p.EnemyIds[slot]).Role, Is.EqualTo(shape.Slots[slot]));
                    }
                }
        }

        [Test]
        public void Lineup_IsFixedForASeason_DiffersAcrossSeasons_AndIsPrefixStable()
        {
            string Key(List<EndlessFloorPlan> l) => string.Join(";", l.Select(p => p.ShapeId + ":" + string.Join(",", p.EnemyIds)));
            var a = EndlessSim.Lineup(Db, 5, 25);
            Assert.That(Key(EndlessSim.Lineup(Db, 5, 25)), Is.EqualTo(Key(a)));
            Assert.That(Key(EndlessSim.Lineup(Db, 5, 13)), Is.EqualTo(Key(a.Take(13).ToList())));
            Assert.That(Key(EndlessSim.Lineup(Db, 6, 25)), Is.Not.EqualTo(Key(a)));
            Assert.Throws<ArgumentOutOfRangeException>(() => EndlessSim.Lineup(Db, -1, 5));
        }

        [Test]
        public void SeasonMutators_DoNotRepeatUntilTheTableIsUsedUp_AndArePrefixStable()
        {
            int n = Db.Mutators.Count;
            var list = EndlessSim.SeasonMutators(Db, 3, 2 * n);
            Assert.That(list.Take(n).Select(m => m.Id).Distinct().Count(), Is.EqualTo(n));
            Assert.That(list.Skip(n).Select(m => m.Id).Distinct().Count(), Is.EqualTo(n));
            Assert.That(EndlessSim.SeasonMutators(Db, 3, 4).Select(m => m.Id), Is.EqualTo(list.Take(4).Select(m => m.Id)));
            Assert.That(EndlessSim.Rules(Db, new List<MutatorDef>()).Mutators, Is.Null);
            Assert.That(EndlessSim.Rules(Db, list.Take(2).ToList()).Mutators, Is.Not.Null);
        }

        // ------------------------------------------------------------------ squads

        [Test]
        public void Squad_ScalesByLevel_CarriesRulebooks_AndBoostsTheAnchorAndTheElite()
        {
            var plans = EndlessSim.Lineup(Db, 0, Db.Constants.EndlessBand * 6);
            foreach (var p in plans)
            {
                var squad = EndlessSim.Squad(Db, p);
                Assert.That(squad.Select(f => f.Id).Distinct().Count(), Is.EqualTo(squad.Length));
                var shape = EndlessSim.Shape(Db, p.ShapeId);
                for (int slot = 0; slot < squad.Length; slot++)
                {
                    if (p.Elite && slot == 0)
                    {
                        var w = Db.TowerBosses[p.EliteId].Weights;
                        Assert.That(squad[0].Stats.Hp, Is.EqualTo(w.Hp * p.Level * Db.Constants.EndlessEliteHpMult).Within(1e-9));
                        Assert.That(squad[0].Phases.Count, Is.EqualTo(Db.TowerBosses[p.EliteId].Phases.Count));
                        continue;
                    }
                    var d = EndlessSim.Enemy(Db, p.EnemyIds[slot]);
                    double anchor = slot == 0 ? shape.AnchorHp : 1;
                    Assert.That(squad[slot].Stats.Hp, Is.EqualTo(d.Weights.Hp * p.Level * anchor).Within(1e-9));
                    Assert.That(squad[slot].Stats.Str, Is.EqualTo(d.Weights.Str * p.Level).Within(1e-9));
                    Assert.That(squad[slot].Gambits.Count, Is.EqualTo(d.Gambits.Count));
                }
                Assert.DoesNotThrow(() => BattleSim.Start(EndlessSim.Rules(Db, null), EndlessSim.Team(Db, Team(300)), squad), "floor " + p.Floor);
            }
        }

        // ------------------------------------------------------------------ the climb

        [Test]
        public void Run_IsDeterministic_AndStopsAtTheFirstLoss()
        {
            var a = EndlessSim.Run(Db, Team(400), 1);
            var b = EndlessSim.Run(Db, Team(400), 1);
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            Assert.That(a.FloorReached, Is.EqualTo(a.Floors.Count));
            Assert.That(a.Floors.Take(a.Floors.Count - 1).All(f => f.Won));
            Assert.That(a.Floors.Last().Won, Is.False);
            Assert.That(a.FloorsCleared, Is.GreaterThan(0), "a decent team clears some floors");
            Assert.That(EndlessSim.Run(Db, Team(400), 2).Hash, Is.Not.EqualTo(a.Hash));
        }

        [Test]
        public void Run_KeepsEachFloorsTeamTallies_TheSameAsTheFloorsOwnFight()
        {
            var r = EndlessSim.Run(Db, Team(400), 1);
            foreach (var f in r.Floors)
            {
                var fight = EndlessSim.FightFloor(Db, Team(400), 1, f.Floor).fight;
                var team = fight.Final.Units.Where(u => u.Side == 0 && !u.IsAdd).ToList();
                Assert.That(f.Team.Count, Is.EqualTo(team.Count));
                for (int i = 0; i < team.Count; i++)
                {
                    Assert.That(f.Team[i].Dealt, Is.EqualTo(team[i].Tally.DamageDealt), "floor " + f.Floor + " slot " + i);
                    Assert.That(f.Team[i].Taken, Is.EqualTo(team[i].Tally.DamageTaken));
                    Assert.That(f.Team[i].Healed, Is.EqualTo(team[i].Tally.HealingDone));
                }
            }
        }

        [Test]
        public void Run_NeedsExactlyAFullTeam()
        {
            Assert.Throws<ArgumentException>(() => EndlessSim.Run(Db, Team(400).Take(3).ToArray(), 0));
            Assert.Throws<ArgumentException>(() => EndlessSim.Run(Db, Team(400).Concat(new[] { Mon("x", 400) }).ToArray(), 0));
        }

        [Test]
        public void Rulebooks_ChangeTheClimb()
        {
            var plain = EndlessSim.Run(Db, Team(400), 4);
            var ruled = Team(400);
            foreach (var f in ruled) f.Gambits.Add(new GambitRule(GambitConditions.Always, 0, "smash", GambitTargets.HighestHp));
            Assert.That(EndlessSim.Run(Db, ruled, 4).Hash, Is.Not.EqualTo(plain.Hash));
        }

        [Test]
        public void Run_StopsAtTheSafetyCap()
        {
            var db = WithConstant("ENDLESS_MAX_FLOORS", 3);
            var r = EndlessSim.Run(db, Team(3000), 0);
            Assert.That(r.Capped);
            Assert.That(r.FloorReached, Is.EqualTo(4));
            Assert.That(r.Floors.Count, Is.EqualTo(3));
        }

        [Test]
        public void Run_AppliesTheSeasonsMutatorsFromTheFirstFullBand()
        {
            var seen = new List<int>();
            EndlessSim.Run(WithConstant("ENDLESS_MAX_FLOORS", 12), Team(3000), 0, null, (plan, muts, s) => seen.Add(muts.Count));
            Assert.That(seen, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1 }));
        }

        // ------------------------------------------------------------------ rulebook text

        [Test]
        public void GambitText_ParsesAndFormats()
        {
            var rules = GambitText.Parse("enemy_hp_below:0.4>night_fang@lowest_hp|always>basic|first_actions:2>roar@auto");
            Assert.That(rules.Select(g => (g.When, g.Value, g.Skill, g.Target)), Is.EqualTo(new[]
            {
                (GambitConditions.EnemyHpBelow, 0.4, "night_fang", GambitTargets.LowestHp),
                (GambitConditions.Always, 0.0, GambitRule.Basic, GambitTargets.Auto),
                (GambitConditions.FirstActions, 2.0, "roar", GambitTargets.Auto),
            }));
            Assert.That(GambitText.Format(rules), Is.EqualTo("enemy_hp_below:0.4>night_fang@lowest_hp|always>basic|first_actions:2>roar"));
            Assert.That(GambitText.Parse(GambitText.Format(rules)).Count, Is.EqualTo(3));
            Assert.That(GambitText.Parse(null), Is.Empty);
            Assert.That(GambitText.Parse(""), Is.Empty);
            foreach (var bad in new[] { "always", "always>a>b", "never>x", "always>x@random", "always>", "self_hp_below>x",
                                        "self_hp_below:1.5>x", "first_actions:0>x", "enemies_at_least:2.5>x", "always:3>x", "ally_hp_below:nan>x" })
                Assert.Throws<FormatException>(() => GambitText.Parse(bad), bad);
        }

        [Test]
        public void FightFloor_IsTheFightTheClimbFought()
        {
            var team = Team(400);
            var climb = EndlessSim.Run(Db, team, 2913);
            foreach (var f in climb.Floors)
            {
                var (plan, mutators, fight) = EndlessSim.FightFloor(Db, team, 2913, f.Floor);
                Assert.That(fight.Hash, Is.EqualTo(f.FightHash), "floor " + f.Floor);
                Assert.That(plan.ShapeId, Is.EqualTo(f.ShapeId));
                Assert.That(mutators.Count, Is.EqualTo(f.Floor / Db.Constants.EndlessBand));
            }
            var beyond = EndlessSim.FightFloor(Db, team, 2913, climb.FloorReached + 3);   // floors past the fall can be fought too
            Assert.That(beyond.plan.Floor, Is.EqualTo(climb.FloorReached + 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => EndlessSim.FightFloor(Db, team, 2913, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EndlessSim.FightFloor(Db, team, 2913, Db.Constants.EndlessMaxFloors + 1));
            Assert.That(climb.Floors.Count, Is.GreaterThan(Db.Constants.EndlessBand), "the check covers a floor with a mutator");
        }
    }
}
