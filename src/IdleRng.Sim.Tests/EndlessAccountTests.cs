using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Account;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Tower;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Endless Tower climbs, rewards and rulebooks on the account (specs/28 sections 4.4, 5 and 6).</summary>
    public class EndlessAccountTests
    {
        static ContentDb Db => TestContent.Db;
        static Constants K => Db.Constants;
        static readonly int[] Team = { 0, 1, 2, 3 };

        /// <summary>
        /// An account with <paramref name="monsters"/> sealed monsters in the Vault: STR drills, buying slot 0's nodes when possible
        /// (RankLadderTests' scripted run), so each monster has skills a rulebook can name.
        /// </summary>
        static AccountState WithVault(int monsters, ulong seed = 28)
        {
            var a = AccountOps.New(Db, seed);
            for (int i = 0; i < monsters; i++)
            {
                var s = AccountOps.StartRun(Db, a, Db.StarterSpecies.Id);
                while (s.Phase != RunPhase.Finished)
                {
                    while (s.Offers.Count > 0) TrainingRun.DiscardOffer(Db, s);
                    var slot = s.Slots[0];
                    var nodes = Db.Trees[slot.TreeId].Nodes;
                    if (slot.Bought < nodes.Count && s.Sp >= nodes[slot.Bought].Cost) { TrainingRun.SpendPoint(Db, s, 0); continue; }
                    if (s.Phase == RunPhase.AwaitFight) TrainingRun.Fight(Db, s);
                    else if (s.Energy < 40) TrainingRun.Rest(Db, s);
                    else TrainingRun.Drill(Db, s, StatIds.Str);
                }
                AccountOps.FinishRun(Db, a);
                AccountOps.CloseVendor(Db, a);
            }
            return a;
        }

        static readonly Lazy<AccountState> Four = new Lazy<AccountState>(() => WithVault(4));

        /// <summary>A fresh copy of the four-monster account (sealing runs is the slow part, so it is done once).</summary>
        static AccountState Fresh()
        {
            var src = Four.Value;
            var a = AccountOps.New(Db, src.Seed);
            a.Vault.AddRange(src.Vault);
            a.RunsStarted = src.RunsStarted;
            return a;
        }

        /// <summary>A season in which the team clears at least two floors (searched, so the test does not depend on placeholder numbers).</summary>
        static int SeasonWithClears(AccountState a)
        {
            var team = Team.Select(i => AccountOps.TeamFighter(Db, a, i)).ToList();
            for (int season = 0; season < 50; season++)
                if (EndlessSim.Run(Db, team, season).FloorsCleared >= 2) return season;
            Assert.Inconclusive("no season in 0..49 where the placeholder team clears two floors");
            return -1;
        }

        static string ActiveSkill(AccountState a, int index) =>
            a.Vault[index].Fighter(Db).Skills.First(s => s.Kind != SkillKind.Passive).Id;

        [Test]
        public void SeasonOf_IsTheUtcWeekFromMonday()
        {
            Assert.That(EndlessSim.SeasonOf(0), Is.EqualTo(0), "Thursday 1970-01-01");
            Assert.That(EndlessSim.SeasonOf(3), Is.EqualTo(0), "Sunday 1970-01-04");
            Assert.That(EndlessSim.SeasonOf(4), Is.EqualTo(1), "Monday 1970-01-05");
            Assert.That(EndlessSim.SeasonOf(10), Is.EqualTo(1));
            Assert.That(EndlessSim.SeasonOf(11), Is.EqualTo(2));
            // 2026-11-02 is a Monday: UTC day 20759.
            Assert.That(EndlessSim.SeasonOf(20759), Is.EqualTo(EndlessSim.SeasonOf(20758) + 1));
            Assert.That(EndlessSim.SeasonOf(20765), Is.EqualTo(EndlessSim.SeasonOf(20759)), "Sunday 2026-11-08");
            Assert.Throws<ArgumentOutOfRangeException>(() => EndlessSim.SeasonOf(-1));
        }

        [Test]
        public void FloorShards_FollowTheFormula_AndEliteFloorsPayMore()
        {
            for (int f = 1; f <= 3 * K.EndlessBand; f++)
            {
                long pay = (long)Math.Floor(K.EndlessShardsBase + f * K.EndlessShardsPerFloor);
                if (EndlessSim.IsElite(Db, f)) pay *= K.EndlessEliteShardMult;
                Assert.That(AccountOps.EndlessFloorShards(Db, f), Is.EqualTo(pay), "floor " + f);
            }
            Assert.That(AccountOps.EndlessFloorShards(Db, K.EndlessBand), Is.GreaterThan(AccountOps.EndlessFloorShards(Db, K.EndlessBand + 1)));
        }

        [Test]
        public void RunEndless_PaysEachFloorOnceASeason_AndMatchesTheSim()
        {
            var a = Fresh();
            int season = SeasonWithClears(a);
            long shards = a.Shards;
            var sim = EndlessSim.Run(Db, Team.Select(i => AccountOps.TeamFighter(Db, a, i)).ToList(), season);
            var r = AccountOps.RunEndless(Db, a, Team, season);
            Assert.That(r.Result.Hash, Is.EqualTo(sim.Hash), "the account climbs the sim's climb");
            int cleared = r.Result.FloorsCleared;
            Assert.That(r.NewFloors, Is.EqualTo(Enumerable.Range(1, cleared)));
            Assert.That(r.FirstClears, Is.EqualTo(Enumerable.Range(1, cleared)));
            Assert.That(r.Shards, Is.EqualTo(Enumerable.Range(1, cleared).Sum(f => AccountOps.EndlessFloorShards(Db, f))));
            Assert.That(a.Shards, Is.EqualTo(shards + r.Shards));
            Assert.That((a.Endless.Season, a.Endless.Climbs, a.Endless.ClearedSeason, a.Endless.ClearedEver), Is.EqualTo((season, 1, cleared, cleared)));

            var again = AccountOps.RunEndless(Db, a, Team, season);
            Assert.That(again.Result.Hash, Is.EqualTo(r.Result.Hash), "free retries replay the same climb");
            Assert.That((again.Shards, again.NewFloors.Count, again.FirstClears.Count), Is.EqualTo((0L, 0, 0)), "nothing new this season");
            Assert.That(a.Endless.Climbs, Is.EqualTo(2));

            // A climb that falls lower keeps the season best; a later season pays its floors again but no first clears.
            a.Endless.ClearedSeason = cleared - 1;
            var partial = AccountOps.RunEndless(Db, a, Team, season);
            Assert.That(partial.NewFloors, Is.EqualTo(new[] { cleared }));
            a.Endless.ClearedSeason = cleared;
            a.Endless.ClearedEver = cleared + 100;
            int later = season + 1;
            var next = AccountOps.RunEndless(Db, a, Team, later);
            Assert.That(next.NewFloors, Is.EqualTo(Enumerable.Range(1, next.Result.FloorsCleared)));
            Assert.That(next.FirstClears, Is.Empty, "the best ever is above them");
            Assert.That((a.Endless.Season, a.Endless.Climbs, a.Endless.ClearedSeason), Is.EqualTo((later, 1, next.Result.FloorsCleared)));
            Assert.That(a.Endless.ClearedEver, Is.EqualTo(cleared + 100));
        }

        [Test]
        public void RunEndless_RefusesBadTeamsAndAnEarlierSeason_ChangingNothing()
        {
            var a = Fresh();
            AccountOps.RunEndless(Db, a, Team, 5);
            var before = (a.Shards, a.Endless.Season, a.Endless.Climbs, a.Endless.ClearedSeason, a.Endless.ClearedEver);
            Assert.Throws<ArgumentException>(() => AccountOps.RunEndless(Db, a, new[] { 0, 1, 2 }, 5), "exactly four");
            Assert.Throws<ArgumentException>(() => AccountOps.RunEndless(Db, a, new[] { 0, 1, 2, 2 }, 5), "twice");
            Assert.Throws<ArgumentOutOfRangeException>(() => AccountOps.RunEndless(Db, a, new[] { 0, 1, 2, 4 }, 5), "no entry 4");
            Assert.Throws<InvalidOperationException>(() => AccountOps.RunEndless(Db, a, Team, 4), "the clock went back");
            Assert.Throws<ArgumentOutOfRangeException>(() => AccountOps.RunEndless(Db, a, Team, -1));
            Assert.That((a.Shards, a.Endless.Season, a.Endless.Climbs, a.Endless.ClearedSeason, a.Endless.ClearedEver), Is.EqualTo(before));
        }

        [Test]
        public void EndlessFloorAchievement_ReadsTheBestClimbEver()
        {
            var d = Db.AchievementList.First(x => x.Kind == AchievementKind.EndlessFloor);
            var a = AccountOps.New(Db, 1);
            Assert.That(AccountOps.AchievementProgress(Db, a, d), Is.EqualTo((0.0, d.Amount)));
            a.Endless.ClearedEver = (int)d.Amount - 1;
            Assert.That(AccountOps.AchievementHolds(Db, a, d), Is.False);
            a.Endless.ClearedEver = (int)d.Amount;
            Assert.That(AccountOps.CheckAchievements(Db, a).Select(x => x.Id), Does.Contain(d.Id));

            // A climb that reaches it earns it on the spot.
            var b = Fresh();
            b.Endless.ClearedEver = (int)d.Amount;
            var r = AccountOps.RunEndless(Db, b, Team, 0);
            Assert.That(r.Achievements, Does.Contain(d.Id));
            Assert.That(b.Achievements, Does.Contain(d.Id));
        }

        [Test]
        public void SetRulebook_StoresCanonicalText_ByEntryId_WithoutTouchingTheEntry()
        {
            var a = Fresh();
            string skill = ActiveSkill(a, 1);
            var entry = a.Vault[1];
            string statsBefore = entry.Stats.Str.ToString("R");
            AccountOps.SetRulebook(Db, a, 1, $"enemy_hp_below:0.50>{skill}@lowest_hp||always>basic@auto");
            Assert.That(a.Rulebooks[entry.Id], Is.EqualTo($"enemy_hp_below:0.5>{skill}@lowest_hp|always>basic"), "canonical text");
            Assert.That(a.Rulebook(entry.Id), Is.EqualTo(a.Rulebooks[entry.Id]));
            Assert.That(a.Rulebook(a.Vault[0].Id), Is.Empty, "other monsters have none");
            Assert.That(entry.Stats.Str.ToString("R"), Is.EqualTo(statsBefore));
            var f = AccountOps.TeamFighter(Db, a, 1);
            Assert.That(f.Gambits.Count, Is.EqualTo(2));
            Assert.That(a.Vault[1].Fighter(Db).Gambits, Is.Empty, "the entry's own fighter has no rulebook");

            AccountOps.SetRulebook(Db, a, 1, "");
            Assert.That(a.Rulebooks.ContainsKey(entry.Id), Is.False, "empty text clears it");
            AccountOps.SetRulebook(Db, a, 1, null);
            Assert.That(a.Rulebooks, Is.Empty);
        }

        [Test]
        public void SetRulebook_RefusesBadRulebooks_ChangingNothing()
        {
            var a = Fresh();
            string skill = ActiveSkill(a, 0);
            AccountOps.SetRulebook(Db, a, 0, "always>" + skill);
            string kept = a.Rulebooks[a.Vault[0].Id];
            string tooMany = string.Join("|", Enumerable.Repeat("always>basic", K.GambitSlots + 1));
            Assert.Throws<ArgumentException>(() => AccountOps.SetRulebook(Db, a, 0, tooMany), "more than GAMBIT_SLOTS rules");
            Assert.Throws<ArgumentException>(() => AccountOps.SetRulebook(Db, a, 0, "always>no_such_skill"), "not one of its skills");
            Assert.Throws<ArgumentException>(() => AccountOps.SetRulebook(Db, a, 0, "sometimes>basic"), "does not parse");
            Assert.Throws<ArgumentException>(() => AccountOps.SetRulebook(Db, a, 0, "self_hp_below:2>basic"), "out of range");
            Assert.Throws<ArgumentException>(() => AccountOps.SetRulebook(Db, a, 9, "always>basic"), "no entry 9");
            var noId = Fresh();
            noId.Vault[0] = new VaultEntry { SpeciesId = a.Vault[0].SpeciesId, Stats = a.Vault[0].Stats, Trees = a.Vault[0].Trees };
            Assert.Throws<InvalidOperationException>(() => AccountOps.SetRulebook(Db, noId, 0, "always>basic"), "an entry with no id");
            Assert.That(a.Rulebooks[a.Vault[0].Id], Is.EqualTo(kept));
            AccountOps.SetRulebook(Db, a, 0, string.Join("|", Enumerable.Repeat("always>basic", K.GambitSlots)));
            Assert.That(GambitText.Parse(a.Rulebooks[a.Vault[0].Id]).Count, Is.EqualTo(K.GambitSlots), "exactly GAMBIT_SLOTS is fine");
        }

        [Test]
        public void AStaleRulebook_IsUsedAsFarAsItStillFits()
        {
            var a = Fresh();
            string skill = ActiveSkill(a, 0);
            int id = a.Vault[0].Id;
            // Stored under older content: a skill the monster no longer has, and more rules than GAMBIT_SLOTS.
            var rules = new List<string> { "always>gone_skill", "self_hp_below:0.5>" + skill };
            rules.AddRange(Enumerable.Repeat("always>basic", K.GambitSlots + 2));
            a.Rulebooks[id] = string.Join("|", rules);
            var f = AccountOps.TeamFighter(Db, a, 0);
            Assert.That(f.Gambits.Count, Is.EqualTo(K.GambitSlots));
            Assert.That(f.Gambits[0].Skill, Is.EqualTo(skill), "the rule naming a missing skill is skipped");
            Assert.That(f.Gambits.Any(g => g.Skill == "gone_skill"), Is.False);
            a.Rulebooks[id] = "no_longer_a_condition>basic";
            Assert.That(AccountOps.TeamFighter(Db, a, 0).Gambits, Is.Empty, "text that no longer parses is ignored");
            Assert.DoesNotThrow(() => AccountOps.RunEndless(Db, a, Team, 1), "the monster still fights");
        }

        [Test]
        public void Rulebooks_AreUsedInEveryTeamMode_AndLeaveWithTheMonster()
        {
            var a = Fresh();
            var plain = AccountOps.RunEndless(Db, Fresh(), Team, 3).Result.Hash;
            for (int i = 0; i < 4; i++) AccountOps.SetRulebook(Db, a, i, "always>basic@highest_hp");
            Assert.That(AccountOps.RunEndless(Db, a, Team, 3).Result.Hash, Is.Not.EqualTo(plain), "the Endless Tower uses the rulebooks");

            a.Tower.Tickets = 1;
            var tower = AccountOps.RunTower(Db, a, new[] { 0, 1 }, 20391);
            var expect = TowerSim.Run(Db, new[] { 0, 1 }.Select(i => AccountOps.TeamFighter(Db, a, i)).ToList(), 20391);
            Assert.That(tower.Result.Hash, Is.EqualTo(expect.Hash), "the Dungeon uses them too");

            int id = a.Vault[2].Id;
            AccountOps.Dismantle(Db, a, 2);
            Assert.That(a.Rulebooks.ContainsKey(id), Is.False, "dismantling removes the rulebook");
            Assert.That(a.Rulebooks.Count, Is.EqualTo(3));
        }

        [Test]
        public void Commands_ParseAndApplyLikeAccountOps()
        {
            var e = AccountCommand.Parse("runEndless", new AccountCommandsTests.ObjArgs(2913, new[] { 3, 2, 1, 0 }));
            Assert.That((e.Kind, e.A, e.Team), Is.EqualTo((CommandKind.RunEndless, 2913, new List<int> { 3, 2, 1, 0 })));
            var r = AccountCommand.Parse("setRulebook", new AccountCommandsTests.ObjArgs(1, "always>basic"));
            Assert.That((r.Kind, r.A, r.Id), Is.EqualTo((CommandKind.SetRulebook, 1, "always>basic")));
            Assert.That(AccountCommand.Parse("setRulebook", new AccountCommandsTests.ObjArgs(1)).Id, Is.EqualTo(""), "no text clears");
            Assert.That(AccountCommands.IsRunCommand(CommandKind.RunEndless), Is.False);
            Assert.That(AccountCommands.IsRunCommand(CommandKind.SetRulebook), Is.False);

            var viaCmd = Fresh();
            var direct = Fresh();
            AccountCommands.Apply(Db, viaCmd, r);
            AccountOps.SetRulebook(Db, direct, 1, "always>basic");
            var res = (EndlessRunResult)AccountCommands.Apply(Db, viaCmd, new AccountCommand { Kind = CommandKind.RunEndless, A = 7, Team = Team.ToList() });
            var dres = AccountOps.RunEndless(Db, direct, Team, 7);
            Assert.That((res.Result.Hash, res.Shards), Is.EqualTo((dres.Result.Hash, dres.Shards)));
            Assert.Throws<ArgumentException>(() => AccountCommands.Apply(Db, viaCmd, new AccountCommand { Kind = CommandKind.RunEndless, A = 7, Team = new List<int> { 0, 1, 2, 9 } }));
            Assert.Throws<ArgumentException>(() => AccountCommands.Apply(Db, viaCmd, new AccountCommand { Kind = CommandKind.RunEndless, A = 7 }), "no team");
        }
    }
}
