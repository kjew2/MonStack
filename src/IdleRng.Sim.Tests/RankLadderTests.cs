using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using IdleRng.Sim.Training;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>The rank ladder (specs/14 section 5): pools per rank, the roll at run start, rematches, replay and the legacy run.</summary>
    public class RankLadderTests
    {
        static ContentDb Db => TestContent.Db;

        static void FinishCycleWithRests(RunState s)
        {
            while (s.Phase == RunPhase.Training && s.ApLeft > 0) TrainingRun.Rest(Db, s);
        }

        [Test]
        public void EveryRank_HasAPool_WhoseStatsSpendExactlyItsBudget()
        {
            foreach (var r in Db.FightRanks)
            {
                Assert.That(r.Pool, Is.Not.Empty, r.Id);
                Assert.That(r.Budget, Is.EqualTo(StatIds.Trained.Sum(id => r.Stats.Get(id))), r.Id);
                foreach (var e in r.Pool)
                {
                    Assert.That(StatIds.Trained.Sum(id => e.Stats.Get(id)), Is.EqualTo(r.Budget).Within(1e-6), e.Id);
                    Assert.That(e.RankId, Is.EqualTo(r.Id));
                    Assert.That(Db.FightRanks[e.RankIndex], Is.SameAs(r));
                    Assert.That(e.Weight, Is.GreaterThan(0));
                    Assert.That(e.Skills.Count, Is.InRange(1, 3), e.Id + ": 1-3 skills (kj, 2026-10-31)");
                    Assert.That(Db.Text(e.NameKey), Is.Not.EqualTo(e.NameKey), e.Id + " has a name");
                }
            }
            Assert.That(Db.RankEnemies.Count, Is.EqualTo(Db.FightRanks.Sum(r => r.Pool.Count)));
        }

        [Test]
        public void HigherRanks_BringMoreSkills()
        {
            double Mean(FightRankDef r) => r.Pool.Average(e => e.Skills.Count);
            for (int i = 1; i < Db.FightRanks.Count; i++)
                Assert.That(Mean(Db.FightRanks[i]), Is.GreaterThanOrEqualTo(Mean(Db.FightRanks[i - 1])), Db.FightRanks[i].Id);
            Assert.That(Mean(Db.FightRanks.Last()), Is.GreaterThan(Mean(Db.FightRanks[0])));
        }

        [Test]
        public void Start_RollsOnePoolMonsterPerRank()
        {
            var s = TrainingRun.Start(Db, "slime", 7);
            Assert.That(s.Ladder.Count, Is.EqualTo(Db.FightRanks.Count));
            for (int i = 0; i < s.Ladder.Count; i++)
            {
                Assert.That(Db.FightRanks[i].Pool.Select(e => e.Id), Does.Contain(s.Ladder[i]));
                Assert.That(TrainingRun.LadderEnemy(Db, s, i).Id, Is.EqualTo(s.Ladder[i]));
            }
            Assert.That(s.Ladder, Is.EqualTo(TrainingRun.RollLadder(Db, 7)));
        }

        [Test]
        public void Ladder_IsDeterministic_AndVariesWithTheSeed()
        {
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.That(TrainingRun.RollLadder(Db, seed), Is.EqualTo(TrainingRun.RollLadder(Db, seed)));
            var distinct = Enumerable.Range(1, 50).Select(i => string.Join(",", TrainingRun.RollLadder(Db, (ulong)i))).Distinct().Count();
            Assert.That(distinct, Is.GreaterThan(40), "different seeds roll different ladders");
        }

        [Test]
        public void Ladder_DoesNotTouchTheOtherStreams()
        {
            var rolled = TrainingRun.Start(Db, "drake", 99);
            var legacy = TrainingRun.Start(Db, "drake", 99, null, null, new List<string>());
            Assert.That(rolled.Training.Seed, Is.EqualTo(legacy.Training.Seed));
            Assert.That(rolled.Training.Counter, Is.EqualTo(legacy.Training.Counter));
            Assert.That(rolled.Draft.Seed, Is.EqualTo(legacy.Draft.Seed));
            Assert.That(rolled.Draft.Counter, Is.EqualTo(legacy.Draft.Counter));
            Assert.That(TrainingRun.FightSeed(rolled.Seed, 0), Is.EqualTo(TrainingRun.FightSeed(legacy.Seed, 0)));
        }

        [Test]
        public void EveryPoolMonster_IsRolled_RoughlyByWeight()
        {
            const int n = 4000;
            var count = Db.RankEnemies.Keys.ToDictionary(k => k, _ => 0);
            for (int i = 0; i < n; i++)
                foreach (var id in TrainingRun.RollLadder(Db, (ulong)i * 7919 + 1)) count[id]++;
            foreach (var r in Db.FightRanks)
            {
                double total = r.Pool.Sum(e => e.Weight);
                foreach (var e in r.Pool)
                {
                    double share = (double)count[e.Id] / n, expected = e.Weight / total;
                    Assert.That(count[e.Id], Is.GreaterThan(0), e.Id + " is rolled");
                    Assert.That(share, Is.EqualTo(expected).Within(0.05), e.Id + " share");
                }
            }
        }

        [Test]
        public void OpponentFighter_IsThePoolMonster_WithItsSkillsInOrder()
        {
            var s = TrainingRun.Start(Db, "sprite", 3);
            for (int i = 0; i < Db.FightRanks.Count; i++)
            {
                var e = Db.RankEnemies[s.Ladder[i]];
                var f = TrainingRun.OpponentFighter(Db, s, i);
                Assert.That(f.Element, Is.EqualTo(e.Element));
                foreach (var id in StatIds.Trained) Assert.That(f.Stats.Get(id), Is.EqualTo(e.Stats.Get(id)));
                Assert.That(f.Stats, Is.Not.SameAs(e.Stats), "the fighter owns its stats");
                Assert.That(f.Skills.Select(k => k.Id), Is.EqualTo(e.Skills));
            }
        }

        [Test]
        public void Fight_IsAgainstTheLadder_AndALossIsARematch()
        {
            var s = TrainingRun.Start(Db, "drake", 5);
            var seen = new List<RankFightResult>();
            while (s.Phase != RunPhase.Finished)
            {
                FinishCycleWithRests(s);
                if (s.Phase == RunPhase.AwaitFight) seen.Add(TrainingRun.Fight(Db, s));
            }
            Assert.That(seen, Is.Not.Empty);
            foreach (var r in seen) Assert.That(r.OpponentId, Is.EqualTo(s.Ladder[r.RankBefore]));
            var losses = seen.Where(r => !r.Won).ToList();
            Assume.That(losses.Count, Is.GreaterThan(1), "a resting drake loses some fights");
            for (int i = 1; i < seen.Count; i++)
                if (!seen[i - 1].Won) Assert.That(seen[i].OpponentId, Is.EqualTo(seen[i - 1].OpponentId), "after a loss, the same opponent again");
        }

        [Test]
        public void MaxedMonster_BeatsEveryPoolMonster()
        {
            var me = new Fighter { Id = TrainingRun.MonsterId, Element = "fire" };
            foreach (var id in StatIds.Trained) Set(me.Stats, id, Db.Constants.StatMax);
            foreach (var e in Db.RankEnemies.Values)
            {
                var s = TrainingRun.Start(Db, "drake", 1);
                s.Ladder[e.RankIndex] = e.Id;
                var foe = TrainingRun.OpponentFighter(Db, s, e.RankIndex);
                var r = BattleSim.Resolve(FightRules.FromContent(Db), new[] { me }, new[] { foe }, 1);
                Assert.That(r.Outcome, Is.EqualTo(FightOutcome.SideAWon), e.Id);
            }
        }

        static void Set(MonsterStats st, string id, double v)
        {
            switch (id)
            {
                case StatIds.Hp: st.Hp = v; break;
                case StatIds.Str: st.Str = v; break;
                case StatIds.Int: st.Int = v; break;
                case StatIds.Wis: st.Wis = v; break;
                case StatIds.Dex: st.Dex = v; break;
                case StatIds.Def: st.Def = v; break;
                case StatIds.Spd: st.Spd = v; break;
                default: st.Luk = v; break;
            }
        }

        /// <summary>The scripted run of the version 4 probe: spend in slot 0 when possible, rest below 40 energy, else drill STR.</summary>
        static RunState Scripted(RunState s)
        {
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
            return s;
        }

        [Test]
        public void LegacyRun_FightsTheFlatRanks_AndKeepsTheVersion4Hash()
        {
            // Computed by the code before the rank pools (run hash version 4) for this exact script: runs saved and Vault entries
            // sealed before content 3.14 must keep replaying to their stored hashes.
            const string V4Hash = "0e278668f40c6dfb2219cc8dc4e4a3ebbc366d9f0d1f7dc48b1fc40fdc751fc9";
            var s = Scripted(TrainingRun.Start(Db, "drake", 11, null, null, new List<string>()));
            Assert.That(s.Ladder, Is.Empty);
            Assert.That(TrainingRun.LadderEnemy(Db, s, 0), Is.Null);
            var flat = TrainingRun.OpponentFighter(Db, s, 0);
            Assert.That(flat.Id, Is.EqualTo("rank_" + Db.FightRanks[0].Id));
            Assert.That(flat.Skills, Is.Empty);
            Assert.That(TrainingRun.StateHash(s), Is.EqualTo(V4Hash));
            Assert.That((s.Rank, s.FightsWon, s.FightsFought), Is.EqualTo((2, 2, 12)));
        }

        [Test]
        public void Replay_WithTheStoredLadder_MatchesTheHash()
        {
            var s = Scripted(TrainingRun.Start(Db, "slime", 21));
            string hash = TrainingRun.StateHash(s);
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, "slime", 21, s.Log, null, null, s.Ladder)), Is.EqualTo(hash));
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, "slime", 21, s.Log)), Is.EqualTo(hash), "null rolls the same ladder again");
            var legacy = Scripted(TrainingRun.Start(Db, "slime", 21, null, null, new List<string>()));
            Assert.That(TrainingRun.StateHash(legacy), Is.Not.EqualTo(hash), "the ladder is hashed");
            Assert.That(TrainingRun.StateHash(TrainingRun.Replay(Db, "slime", 21, legacy.Log, null, null, new List<string>())),
                Is.EqualTo(TrainingRun.StateHash(legacy)));
        }

        [Test]
        public void Replay_RefusesALadderThatDoesNotFitTheContent()
        {
            var good = TrainingRun.RollLadder(Db, 1);
            var log = new List<RunCommand>();
            Assert.Throws<ArgumentException>(() => TrainingRun.Replay(Db, "slime", 1, log, null, null, good.Take(good.Count - 1).ToList()));
            var unknown = new List<string>(good) { [0] = "no_such_monster" };
            Assert.Throws<ArgumentException>(() => TrainingRun.Replay(Db, "slime", 1, log, null, null, unknown));
            var wrongRank = new List<string>(good) { [0] = good[1] };
            Assert.Throws<ArgumentException>(() => TrainingRun.Replay(Db, "slime", 1, log, null, null, wrongRank));
            var other = Db.FightRanks[0].Pool.First(e => e.Id != good[0]).Id;
            var chosen = new List<string>(good) { [0] = other };
            Assert.That(TrainingRun.Replay(Db, "slime", 1, log, null, null, chosen).Ladder, Is.EqualTo(chosen), "a valid stored ladder is used as given");
        }

        [Test]
        public void Clone_CopiesTheLadder()
        {
            var s = TrainingRun.Start(Db, "slime", 4);
            var c = s.Clone();
            Assert.That(c.Ladder, Is.EqualTo(s.Ladder));
            Assert.That(c.Ladder, Is.Not.SameAs(s.Ladder));
        }
    }
}
