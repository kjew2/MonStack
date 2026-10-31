using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;
using NUnit.Framework;

namespace IdleRng.Sim.Tests
{
    /// <summary>Stat, skill and node descriptions (specs/16 section 9): every placeholder resolves and the numbers are the battle's.</summary>
    public class BattleTextTests
    {
        static ContentDb Db => TestContent.Db;

        static MonsterStats Sheet(double all) => new MonsterStats { Hp = all, Str = all, Int = all, Wis = all, Dex = all, Def = all, Spd = all, Luk = all };

        [Test]
        public void TextTemplate_FillsConstantsAndCallerValues_AndKeepsUnknownPlaceholders()
        {
            string t = TextTemplate.Fill(Db, "{MON_DEF_K} / {MON_CRIT_BASE:pct} / {x} / {nope} / {MON_DEF_K", (n, f) => n == "x" ? "X" : null);
            Assert.That(t, Is.EqualTo("300 / 5% / X / {nope} / {MON_DEF_K"));
            Assert.That(TextTemplate.HasPlaceholder(t), Is.True);
            Assert.That(TextTemplate.Number(0.0005, "pct"), Is.EqualTo("0.05%"));
            Assert.That(TextTemplate.Number(1.0 / 3, null), Is.EqualTo("0.33"));
        }

        [Test]
        public void EveryStat_HasNameDescAndNowLine_WithNothingLeftUnfilled()
        {
            var now = BattleText.StatNow(Db, Sheet(250), Sheet(300), "Rank C");
            foreach (var id in StatIds.Trained)
            {
                foreach (var key in new[] { "name", "desc", "now" })
                    Assert.That(Db.Strings.ContainsKey("stat." + id + "." + key), Is.True, "stat." + id + "." + key);
                Assert.That(TextTemplate.HasPlaceholder(BattleText.StatDesc(Db, id)), Is.False, id);
                Assert.That(TextTemplate.HasPlaceholder(now[id]), Is.False, id + ": " + now[id]);
            }
            Assert.That(TextTemplate.HasPlaceholder(BattleText.NowTitle(Db, "Rank C")), Is.False);
        }

        [Test]
        public void NowLines_UseTheBattleFormulas()
        {
            var r = FightRules.FromContent(Db);
            var self = new MonsterStats { Hp = 140, Str = 90, Int = 150, Wis = 300, Dex = 200, Def = 300, Spd = 150, Luk = 100 };
            var foe = Sheet(100);
            var now = BattleText.StatNow(Db, self, foe, "Rank E");
            Assert.That(now["hp"], Does.Contain(TextTemplate.Number(BattleSim.MaxHp(r, 140), null)));
            Assert.That(now["def"], Does.Contain("50%"), "DEF = K halves physical damage");
            Assert.That(now["wis"], Does.Contain("50%"));
            Assert.That(now["int"], Does.Contain("150").And.Contain(Db.Text("tip.tag.magical")), "INT > STR: the basic attack is magical");
            Assert.That(now["str"], Is.EqualTo(now["int"]));
            Assert.That(now["dex"], Does.Contain(TextTemplate.Number(BattleSim.EvadeChance(r, 200, 100), "pct")).And.Contain("Rank E"));
            Assert.That(now["spd"], Does.Contain("1.5"));
            Assert.That(now["luk"], Does.Contain(TextTemplate.Number(BattleSim.CritChance(r, 100, 0), "pct")));
        }

        [Test]
        public void EverySkillRuleAndNode_Describes_WithNothingLeftUnfilled()
        {
            foreach (var sk in Db.MonsterSkills.Values)
                Assert.That(TextTemplate.HasPlaceholder(BattleText.SkillDesc(Db, sk)), Is.False, sk.Id);
            foreach (var t in Db.TreeList)
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    var n = t.Nodes[i];
                    var tip = BattleText.NodeTip(Db, n, n.Id, "filler", i == t.Nodes.Count - 1);
                    Assert.That(tip.Text, Is.Not.Empty, n.Id);
                    Assert.That(tip.Tags, Is.Not.Empty, n.Id);
                    foreach (var s in tip.Tags.Append(tip.Text).Concat(tip.Facts.SelectMany(f => new[] { f.Key, f.Value })))
                    {
                        Assert.That(TextTemplate.HasPlaceholder(s), Is.False, n.Id + ": " + s);
                        Assert.That(s, Does.Not.StartWith("tip.").And.Not.StartWith("stat.").And.Not.StartWith("trigger."), n.Id + ": missing string " + s);
                    }
                    Assert.That(tip.Tags.Contains(Db.Text("tip.tag.chase")), Is.EqualTo(i == t.Nodes.Count - 1), n.Id);
                }
        }

        [Test]
        public void SkillDescription_NumbersComeFromTheSkillRow()
        {
            var smash = Db.MonsterSkills["smash"];
            Assert.That(BattleText.SkillDesc(Db, smash), Is.EqualTo("Damage for " + TextTemplate.Number(smash.Power, "pct") + " STR. Cooldown " + smash.Cooldown + "."));
            Assert.That(BattleText.SkillDesc(Db, Db.MonsterSkills["second_wind"]), Does.Contain("max HP"));
            Assert.That(BattleText.SkillDesc(Db, Db.MonsterSkills["jackpot"]), Does.Contain("crit damage"));
            Assert.That(BattleText.RuleDesc(Db, RuleModifiers.EchoCast), Does.Contain(TextTemplate.Number(Db.Constants.MonEchoPower, "pct")));
        }

        [Test]
        public void DebuffTooltip_NamesTheStatFloorConstant()
        {
            var confuse = BattleText.NodeTip(Db, NodeWith("confuse"), "x", "x", false);
            Assert.That(Fact(confuse, "effect"), Does.Contain(TextTemplate.Number(Db.Constants.StatFloorFrac, "pct") + " of its base"));
        }

        static TreeNodeDef NodeWith(string skillId) => Db.TreeNodes.Values.First(n => n.Skill != null && n.Skill.Id == skillId);

        static string Fact(Tip t, string label) => t.Facts.FirstOrDefault(f => f.Key == Db.Text("tip.label." + label)).Value;

        [Test]
        public void ActiveSkillTip_ShowsDamageTypeCooldownAndAiCondition()
        {
            var bolt = BattleText.NodeTip(Db, NodeWith("arcane_blast"), "x", "x", false);
            Assert.That(bolt.Tags, Does.Contain(Db.Text("tip.tag.magical")));
            Assert.That(Fact(bolt, "damage_type"), Is.EqualTo(Db.Text("tip.magical")));
            Assert.That(Fact(bolt, "cooldown"), Does.Contain("every 3"));
            Assert.That(Fact(bolt, "ai"), Is.Not.Null);
            Assert.That(Fact(bolt, "fires"), Is.Null);

            var breath = BattleText.NodeTip(Db, NodeWith("flame_breath"), "x", "x", false);
            Assert.That(breath.Tags, Does.Contain(Db.Text("tip.tag.physical")).And.Contain(Db.Text("element.fire.name")));
            Assert.That(Fact(breath, "amount"), Is.EqualTo("90% of STR"));

            var jab = BattleText.NodeTip(Db, NodeWith("quick_jab"), "x", "x", false);
            Assert.That(Fact(jab, "cooldown"), Is.EqualTo(Db.Text("tip.cooldown.none")), "cooldown 1 is ready again on the next action");

            var heal = BattleText.NodeTip(Db, NodeWith("gel_mend"), "x", "x", false);
            Assert.That(Fact(heal, "ai"), Does.Contain(TextTemplate.Number(Db.Constants.AiHealBelow, "pct")));
        }

        [Test]
        public void TriggeredAndPassiveTips_SayWhenTheyFire_NotACooldown()
        {
            var node = NodeWith("ooze_back");
            var ooze = BattleText.NodeTip(Db, node, "x", "x", true);
            Assert.That(ooze.Tags, Does.Contain(BattleText.TriggerName(Db, node.Trigger.Value)));
            Assert.That(Fact(ooze, "fires"), Is.EqualTo(Db.Text("tip.fires.on_hit_taken")));
            Assert.That(Fact(ooze, "cooldown"), Is.EqualTo(Db.Text("tip.cooldown.triggered")));
            Assert.That(Fact(ooze, "ai"), Is.Null);
            Assert.That(Fact(ooze, "amount"), Is.EqualTo("8% of max HP"));

            var surge = BattleText.NodeTip(Db, NodeWith("overcharge"), "x", "x", true);
            Assert.That(Fact(surge, "stacks"), Does.Contain(Db.Constants.EffectStackCap.ToString()), "a triggered buff stacks up to the cap");

            var plate = BattleText.NodeTip(Db, NodeWith("plate_mastery"), "x", "x", true);
            Assert.That(Fact(plate, "fires"), Is.EqualTo(Db.Text("tip.fires.battle_start")));
            Assert.That(Fact(plate, "duration"), Is.EqualTo(Db.Text("tip.duration.permanent")));
            Assert.That(Fact(plate, "cooldown"), Is.Null);
        }

        [Test]
        public void FillerAndRuleNodeTips()
        {
            var growth = Db.TreeNodes["pyromancy_n0"];
            var tip = BattleText.NodeTip(Db, growth, "+0.1 STR growth", "Drills raise STR faster.", false);
            Assert.That(tip.Text, Is.EqualTo("Drills raise STR faster."));
            Assert.That(tip.Facts[0].Value, Is.EqualTo(BattleText.StatDesc(Db, "str")));
            Assert.That(Fact(tip, "cost"), Is.EqualTo(growth.Cost + " SP"));

            var rule = Db.TreeNodes.Values.First(n => n.Kind == NodeKind.Rule);
            var rt = BattleText.NodeTip(Db, rule, "x", "x", true);
            Assert.That(rt.Text, Is.EqualTo(BattleText.RuleDesc(Db, rule.RuleId)));
            Assert.That(rt.Tags, Does.Contain(Db.Text("tip.tag.rule")).And.Contain(Db.Text("tip.tag.chase")));
        }

        [Test]
        public void Snake() => Assert.That(BattleText.Snake("OnHitTaken"), Is.EqualTo("on_hit_taken"));
    }
}
