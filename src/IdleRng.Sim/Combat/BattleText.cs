using System;
using System.Collections.Generic;
using System.Text;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Combat
{
    /// <summary>One tooltip: a title, short tags, a paragraph and labelled facts (specs/16 section 9).</summary>
    public sealed class Tip
    {
        public string Title { get; set; }
        public List<string> Tags { get; } = new List<string>();
        public string Text { get; set; }
        public List<KeyValuePair<string, string>> Facts { get; } = new List<KeyValuePair<string, string>>();

        public Tip Fact(string label, string value)
        {
            Facts.Add(new KeyValuePair<string, string>(label, value));
            return this;
        }
    }

    /// <summary>
    /// Player-facing descriptions of stats, skills and tree nodes (specs/16 section 9). The words are content strings
    /// (<c>stat.*</c>, <c>mskill.*</c>, <c>rule.*</c>, <c>trigger.*</c>, <c>tip.*</c>); every number comes from the content row or
    /// constant it describes, or from the same formulas the fight uses (<see cref="BattleSim.MaxHp"/> and its neighbours), through
    /// <see cref="TextTemplate"/>. No rule lives here.
    /// </summary>
    public static class BattleText
    {
        // ------------------------------------------------------------------ stats

        /// <summary>The stat's full name (<c>stat.&lt;id&gt;.name</c>), e.g. "Strength".</summary>
        public static string StatName(ContentDb db, string stat) => db.Text("stat." + stat + ".name");

        /// <summary>
        /// How a stat is named inside a sentence: <c>stat.&lt;id&gt;.label</c> when content has one ("max HP", "crit chance"), else the
        /// upper-case id ("STR").
        /// </summary>
        public static string StatLabel(ContentDb db, string stat)
        {
            if (stat == null) return "";
            return db.Strings.TryGetValue("stat." + stat + ".label", out var s) ? s : stat.ToUpperInvariant();
        }

        /// <summary>What the stat does in battle (<c>stat.&lt;id&gt;.desc</c>), with the battle constants filled in.</summary>
        public static string StatDesc(ContentDb db, string stat) => TextTemplate.Fill(db, db.Text("stat." + stat + ".desc"));

        /// <summary>The heading over <see cref="StatNow"/> lines (<c>tip.now</c>).</summary>
        public static string NowTitle(ContentDb db, string foeName) =>
            TextTemplate.Fill(db, db.Text("tip.now"), (n, f) => n == "foe" ? foeName : null);

        /// <summary>
        /// What each trained stat of <paramref name="self"/> does right now against <paramref name="foe"/> (<c>stat.&lt;id&gt;.now</c>),
        /// from the battle formulas on the raw stat sheet: before buffs, debuffs and passives.
        /// </summary>
        public static SortedDictionary<string, string> StatNow(ContentDb db, MonsterStats self, MonsterStats foe, string foeName)
        {
            var r = FightRules.FromContent(db);
            bool magical = BattleSim.BasicAttackMagical(self.Str, self.Int);
            string Resolve(string name, string fmt)
            {
                switch (name)
                {
                    case "foe": return foeName;
                    case "max_hp": return TextTemplate.Number(BattleSim.MaxHp(r, self.Hp), fmt);
                    case "basic": return TextTemplate.Number(BattleSim.BasicAttackRaw(r, self.Str, self.Int), fmt);
                    case "basic_type": return db.Text(magical ? "tip.tag.magical" : "tip.tag.physical");
                    case "def_taken": return TextTemplate.Number(BattleSim.Mitigate(r, 1.0, self.Def), fmt);
                    case "wis_taken": return TextTemplate.Number(BattleSim.Mitigate(r, 1.0, self.Wis), fmt);
                    case "evade": return TextTemplate.Number(BattleSim.EvadeChance(r, self.Dex, foe.Dex), fmt);
                    case "foe_evade": return TextTemplate.Number(BattleSim.EvadeChance(r, foe.Dex, self.Dex), fmt);
                    case "acts": return TextTemplate.Number(BattleSim.GaugeFill(r, self.Spd) * r.TurnTicks / r.GaugeSize, fmt);
                    case "crit": return TextTemplate.Number(BattleSim.CritChance(r, self.Luk, 0), fmt);
                    default: return null;
                }
            }
            var now = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in StatIds.Trained) now[id] = TextTemplate.Fill(db, db.Text("stat." + id + ".now"), Resolve);
            return now;
        }

        // ------------------------------------------------------------------ skills and rules

        /// <summary>
        /// A skill's description (<c>mskill.&lt;id&gt;.desc</c>) with its own row filled in: {power} (or {power:pct}), {scales},
        /// {stat}, {cooldown}, {duration}, {ticks} (duration in ticks), {element}.
        /// </summary>
        public static string SkillDesc(ContentDb db, CombatSkill sk) => TextTemplate.Fill(db, db.Text("mskill." + sk.Id + ".desc"), SkillResolver(db, sk));

        /// <summary>A rule modifier's description (<c>rule.&lt;id&gt;.desc</c>) with constants filled in.</summary>
        public static string RuleDesc(ContentDb db, string ruleId) => TextTemplate.Fill(db, db.Text("rule." + ruleId + ".desc"));

        /// <summary>
        /// Daily Tower playback tooltips (specs/21 section 8) as (key, name, text): "entropy" (the gauge), "vent.&lt;kind&gt;" for each
        /// vent kind (BattleSim.NextVent; convert reads the entropy_convert rule), "corruption" (the meter; {start_turns} and
        /// {every_turns} are CORRUPT_START_TICK and CORRUPT_EVERY_TICKS in turns) and "phase.&lt;kind&gt;" for each boss phase kind.
        /// </summary>
        public static List<(string key, string name, string text)> TowerTips(ContentDb db)
        {
            var k = db.Constants;
            double turn = k.TurnTicks;
            var list = new List<(string, string, string)>
            {
                ("entropy", db.Text("tower.entropy.name"), TextTemplate.Fill(db, db.Text("tower.entropy.desc"))),
            };
            foreach (var v in new[] { BattleSim.VentRecoil, BattleSim.VentStun, BattleSim.VentEnrage })
                list.Add(("vent." + v, db.Text("vent." + v + ".name"), TextTemplate.Fill(db, db.Text("vent." + v + ".desc"))));
            list.Add(("vent." + BattleSim.VentConvert, db.Text("vent." + BattleSim.VentConvert + ".name"), RuleDesc(db, RuleModifiers.EntropyConvert)));
            list.Add(("corruption", db.Text("tower.corruption.name"), TextTemplate.Fill(db, db.Text("tower.corruption.desc"), (name, fmt) =>
                name == "start_turns" ? TextTemplate.Number(k.CorruptStartTick / turn, fmt)
                : name == "every_turns" ? TextTemplate.Number(k.CorruptEveryTicks / turn, fmt) : null)));
            foreach (var p in PhaseKinds.Known)
                list.Add(("phase." + p, db.Text("phase." + p + ".name"), TextTemplate.Fill(db, db.Text("phase." + p + ".desc"))));
            return list;
        }

        /// <summary>A battle trigger in a few words (<c>trigger.&lt;id&gt;.name</c>), e.g. "when hit".</summary>
        public static string TriggerName(ContentDb db, FightTrigger t) => db.Text("trigger." + Snake(t.ToString()) + ".name");

        static Func<string, string, string> SkillResolver(ContentDb db, CombatSkill sk)
        {
            int turnTicks = db.Constants.TurnTicks;
            return (name, fmt) =>
            {
                switch (name)
                {
                    case "power": return TextTemplate.Number(sk.Power, fmt);
                    case "cooldown": return TextTemplate.Number(sk.Cooldown, fmt);
                    case "duration": return TextTemplate.Number(sk.Duration, fmt);
                    case "ticks": return TextTemplate.Number(sk.Duration * turnTicks, fmt);
                    case "scales": return StatLabel(db, sk.Scales);
                    case "stat": return StatLabel(db, sk.Stat);
                    case "element": return sk.Element != null ? db.Text(db.Elements[sk.Element].NameKey) : db.Text("tip.element.own");
                    default: return null;
                }
            };
        }

        // ------------------------------------------------------------------ tree nodes

        /// <summary>
        /// The tooltip of a tree node: for a skill or trigger node what the skill does, how much, to whom, how often and when
        /// the AI uses it; for a rule its description; for a filler node what its stat does. <paramref name="title"/> and
        /// <paramref name="fillerDesc"/> are the node's name and one-line description as the caller shows them.
        /// </summary>
        public static Tip NodeTip(ContentDb db, TreeNodeDef n, string title, string fillerDesc, bool last)
        {
            var tip = new Tip { Title = title };
            string L(string label) => db.Text("tip.label." + label);
            string T(string key, Func<string, string, string> res = null) => TextTemplate.Fill(db, db.Text(key), res);

            switch (n.Kind)
            {
                case NodeKind.Growth:
                case NodeKind.Stat:
                    tip.Tags.Add(db.Text(n.Kind == NodeKind.Growth ? "tip.tag.growth" : "tip.tag.stat"));
                    tip.Text = fillerDesc;
                    tip.Fact(n.Stat.ToUpperInvariant() + " · " + StatName(db, n.Stat), StatDesc(db, n.Stat));
                    break;
                case NodeKind.Rule:
                    tip.Tags.Add(db.Text("tip.tag.rule"));
                    tip.Text = RuleDesc(db, n.RuleId);
                    break;
                default:
                    SkillFacts(db, tip, n.Skill, n.Kind == NodeKind.Trigger ? n.Trigger : null, L, T);
                    break;
            }
            if (last) tip.Tags.Add(db.Text("tip.tag.chase"));
            tip.Fact(L("cost"), T("tip.cost", (name, fmt) => name == "cost" ? TextTemplate.Number(n.Cost, fmt) : null));
            return tip;
        }

        static void SkillFacts(ContentDb db, Tip tip, CombatSkill sk, FightTrigger? trigger, Func<string, string> L,
                               Func<string, Func<string, string, string>, string> T)
        {
            var res = SkillResolver(db, sk);
            string kind = Snake(sk.Kind.ToString());
            tip.Tags.Add(db.Text("tip.kind." + kind));
            if (sk.Kind == SkillKind.Damage)
            {
                tip.Tags.Add(db.Text(sk.Magical ? "tip.tag.magical" : "tip.tag.physical"));
                if (sk.Element != null) tip.Tags.Add(db.Text(db.Elements[sk.Element].NameKey));
            }
            if (trigger != null) tip.Tags.Add(TriggerName(db, trigger.Value));
            tip.Text = SkillDesc(db, sk);

            tip.Fact(L("effect"), T("tip.effect." + kind, res));
            switch (sk.Kind)
            {
                case SkillKind.Damage:
                case SkillKind.Heal:
                case SkillKind.Shield:
                    tip.Fact(L("amount"), T("tip.amount.scaled", res));
                    break;
                case SkillKind.Buff:
                case SkillKind.Passive:
                    tip.Fact(L("amount"), T("tip.amount.raise", res));
                    break;
                case SkillKind.Debuff:
                    tip.Fact(L("amount"), T("tip.amount.lower", res));
                    break;
            }
            if (sk.Kind == SkillKind.Damage)
            {
                tip.Fact(L("damage_type"), db.Text(sk.Magical ? "tip.magical" : "tip.physical"));
                tip.Fact(L("element"), res("element", null));
            }
            tip.Fact(L("target"), db.Text("tip.target." + Snake(sk.Target.ToString())));
            if (sk.Kind == SkillKind.Passive) tip.Fact(L("duration"), db.Text("tip.duration.permanent"));
            else if (sk.Kind != SkillKind.Damage && sk.Kind != SkillKind.Heal && sk.Duration > 0) tip.Fact(L("duration"), T("tip.duration", res));

            if (sk.Kind == SkillKind.Passive)
                tip.Fact(L("fires"), db.Text("tip.fires.battle_start"));
            else if (trigger != null)
            {
                tip.Fact(L("fires"), db.Text("tip.fires." + Snake(trigger.Value.ToString())));
                tip.Fact(L("cooldown"), db.Text("tip.cooldown.triggered"));
                if (sk.Kind == SkillKind.Buff || sk.Kind == SkillKind.Debuff) tip.Fact(L("stacks"), T("tip.stacks.triggered", res));
            }
            else
            {
                tip.Fact(L("cooldown"), sk.Cooldown <= 1 ? db.Text("tip.cooldown.none") : T("tip.cooldown.n", res));
                tip.Fact(L("ai"), T("tip.ai." + kind, res));
                tip.Fact(L("priority"), db.Text("tip.priority"));
            }
        }

        /// <summary>PascalCase enum name to its content id: OnHitTaken -> on_hit_taken.</summary>
        public static string Snake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c)) { if (i > 0) sb.Append('_'); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
