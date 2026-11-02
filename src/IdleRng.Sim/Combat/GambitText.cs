using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IdleRng.Sim.Combat
{
    /// <summary>
    /// A rulebook as one line of text (specs/28 section 7): rules split by '|', each <c>when[:value]&gt;action[@target]</c>, for
    /// example <c>enemy_hp_below:0.4&gt;night_fang@lowest_hp|always&gt;basic</c>. Content uses it for enemy rulebooks. Mirrors
    /// parse_gambits in tools/content_build.py. Checks the vocabulary and value ranges; whether the action is one of the
    /// unit's skills is checked where the skills are known (ContentDb, BattleSim.Start).
    /// </summary>
    public static class GambitText
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Parses a rulebook; null or empty text is an empty rulebook. Throws FormatException naming the bad rule.</summary>
        public static List<GambitRule> Parse(string text)
        {
            var rules = new List<GambitRule>();
            if (string.IsNullOrEmpty(text)) return rules;
            foreach (var part in text.Split('|'))
            {
                if (part.Length == 0) continue;
                int gt = part.IndexOf('>');
                if (gt < 0 || part.IndexOf('>', gt + 1) >= 0) throw new FormatException($"rule '{part}' needs exactly one '>'");
                string cond = part.Substring(0, gt), act = part.Substring(gt + 1);
                int colon = cond.IndexOf(':');
                string when = colon < 0 ? cond : cond.Substring(0, colon);
                string val = colon < 0 ? "" : cond.Substring(colon + 1);
                int at = act.IndexOf('@');
                string action = at < 0 ? act : act.Substring(0, at);
                string target = at < 0 || at == act.Length - 1 ? GambitTargets.Auto : act.Substring(at + 1);
                if (!GambitConditions.IsKnown(when)) throw new FormatException($"rule '{part}': unknown condition '{when}'");
                if (!GambitTargets.IsKnown(target)) throw new FormatException($"rule '{part}': unknown target '{target}'");
                if (action.Length == 0) throw new FormatException($"rule '{part}' names no action");
                double value = 0;
                if (when != GambitConditions.Always)
                {
                    if (!double.TryParse(val, NumberStyles.Float, Inv, out value) || double.IsNaN(value) || double.IsInfinity(value))
                        throw new FormatException($"rule '{part}': '{when}' needs a number");
                    if (GambitConditions.TakesFraction(when) && !(value > 0 && value <= 1))
                        throw new FormatException($"rule '{part}': an HP fraction must be in (0, 1]");
                    if (GambitConditions.TakesCount(when) && !(value >= 1 && value == Math.Floor(value)))
                        throw new FormatException($"rule '{part}': a count must be a whole number >= 1");
                }
                else if (val.Length > 0) throw new FormatException($"rule '{part}': 'always' takes no value");
                rules.Add(new GambitRule(when, value, action, target));
            }
            return rules;
        }

        /// <summary>The text form of a rulebook: Parse(Format(rules)) gives the same rules.</summary>
        public static string Format(IEnumerable<GambitRule> rules)
        {
            var sb = new StringBuilder();
            foreach (var g in rules)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(g.When);
                if (g.When != GambitConditions.Always) sb.Append(':').Append(g.Value.ToString("R", Inv));
                sb.Append('>').Append(g.Skill);
                if (g.Target != GambitTargets.Auto) sb.Append('@').Append(g.Target);
            }
            return sb.ToString();
        }
    }
}
