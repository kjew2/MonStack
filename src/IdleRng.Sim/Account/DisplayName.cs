using System;
using System.Text;

namespace IdleRng.Sim.Account
{
    /// <summary>
    /// The player's display name (specs/27 section 3.3, the profile; shown on the Endless Tower season board, specs/28 section 6).
    /// One rule for every host, so the client and the server accept the same names: NAME_MIN to NAME_MAX characters after
    /// trimming and folding runs of spaces into one; ASCII letters, digits, spaces, '-' and '_' only, with at least one letter;
    /// and none of the blocked words, looked for in the name lowercased with look-alike digits and symbols read as letters and
    /// everything else removed. ASCII only for now, so no name can copy another with look-alike letters from other scripts.
    /// </summary>
    public static class DisplayName
    {
        /// <summary>NAME_MIN and NAME_MAX (specs/00).</summary>
        public const int Min = 3, Max = 16;

        // TODO(#129): a placeholder list. Profanity and the names of the game and its staff; kj to review, or move to a
        // moderation service with a report button before launch.
        static readonly string[] Blocked =
        {
            "fuck", "fuk", "shit", "cunt", "bitch", "nigg", "nigga", "fag", "whore", "slut", "dick", "cock", "pussy", "penis",
            "vagina", "rape", "rapist", "nazi", "hitler", "retard", "twat", "wank", "bastard", "porn", "kkk", "isis",
            "admin", "moderator", "official", "monstack", "support", "staff",
        };

        /// <summary>The name as stored: trimmed, runs of whitespace folded into one space. Null stays null.</summary>
        public static string Normalize(string name)
        {
            if (name == null) return null;
            var sb = new StringBuilder(name.Length);
            bool space = false;
            foreach (char ch in name.Trim())
            {
                if (char.IsWhiteSpace(ch)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The name to store, or null for an empty one (no name: the board shows a generated one). Throws ArgumentException with
        /// a sentence for the player when the name breaks a rule.
        /// </summary>
        public static string Check(string name)
        {
            string n = Normalize(name);
            if (string.IsNullOrEmpty(n)) return null;
            if (n.Length < Min || n.Length > Max) throw new ArgumentException($"A name has {Min} to {Max} characters.");
            bool letter = false;
            foreach (char ch in n)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == ' ' || ch == '-' || ch == '_';
                if (!ok) throw new ArgumentException("A name uses only letters A to Z, digits, spaces, '-' and '_'.");
                letter |= char.IsLetter(ch);
            }
            if (!letter) throw new ArgumentException("A name needs at least one letter.");
            string squashed = Squash(n);
            foreach (var w in Blocked)
                if (squashed.IndexOf(w, StringComparison.Ordinal) >= 0) throw new ArgumentException("That name is not allowed. Try another.");
            return n;
        }

        /// <summary>The name lowercased, with look-alike digits read as letters and everything but letters removed.</summary>
        static string Squash(string n)
        {
            var sb = new StringBuilder(n.Length);
            foreach (char raw in n)
            {
                char ch = char.ToLowerInvariant(raw);
                switch (ch)
                {
                    case '0': ch = 'o'; break;
                    case '1': ch = 'i'; break;
                    case '3': ch = 'e'; break;
                    case '4': ch = 'a'; break;
                    case '5': ch = 's'; break;
                    case '7': ch = 't'; break;
                    case '8': ch = 'b'; break;
                }
                if (ch >= 'a' && ch <= 'z') sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
