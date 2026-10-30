using System;
using System.Globalization;
using System.Text;

namespace IdleRng.Sim.Content
{
    /// <summary>
    /// Fills placeholders in player-visible strings (specs/05 section 5), so a number in a description comes from the data it
    /// describes and cannot drift from it. A placeholder is <c>{name}</c> or <c>{name:pct}</c>. An UPPER_CASE name is a constant
    /// (<see cref="ContentDb.ConstantValues"/>); a lower-case name is a value of the thing described (a skill's power, a stat sheet's
    /// max HP), resolved by the caller. A placeholder nothing resolves is left as written, so a gap is visible rather than blank.
    /// </summary>
    public static class TextTemplate
    {
        /// <summary>
        /// Replace each placeholder with <paramref name="resolve"/>(name, format), or the constant of that name. A null result keeps
        /// the placeholder text.
        /// </summary>
        public static string Fill(ContentDb db, string text, Func<string, string, string> resolve = null)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder(text.Length + 16);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string body = text.Substring(open + 1, close - open - 1);
                int colon = body.IndexOf(':');
                string name = colon < 0 ? body : body.Substring(0, colon);
                string fmt = colon < 0 ? null : body.Substring(colon + 1);
                string v = resolve?.Invoke(name, fmt);
                if (v == null && db != null && db.ConstantValues.TryGetValue(name, out var c)) v = Number(c, fmt);
                sb.Append(v ?? text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>A number for a template: up to two decimals, or with format "pct" as a percentage (0.05 -> "5%").</summary>
        public static string Number(double v, string fmt)
        {
            if (fmt == "pct") return (v * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>True when <paramref name="text"/> still holds a placeholder (an unresolved name).</summary>
        public static bool HasPlaceholder(string text)
        {
            if (text == null) return false;
            int open = text.IndexOf('{');
            return open >= 0 && text.IndexOf('}', open + 1) > open;
        }
    }
}
