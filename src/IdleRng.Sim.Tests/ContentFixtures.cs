using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IdleRng.Sim.Content;
using NUnit.Framework;

namespace IdleRng.TestFixtures
{
    /// <summary>
    /// Shipped content with a small edit, for the species groundwork (#160) before any species lane uses it: generic trees turned
    /// native-only, and a second chase tree with no book. Linked into the Sim, Runtime and Bots test projects. Hashes are not
    /// verified (the edit changes them).
    /// </summary>
    internal static class ContentFixtures
    {
        static Dictionary<string, string> Files()
        {
            string dir = FileContentSource.FindContentDirectory(TestContext.CurrentContext.TestDirectory)
                         ?? throw new InvalidOperationException("Content/manifest.json not found above the test directory");
            return Directory.GetFiles(dir, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        }

        /// <summary>
        /// The shipped content with these generic trees turned native-only, as a species lane does (kj, 2026-11-03): kind native, no
        /// offer stat or threshold; ids, nodes and rule capstone kept. No species owns them in the fixture. A tree a species lane has
        /// already turned native is left as it is, so the tests keep working as the lanes land.
        /// </summary>
        public static ContentDb WithNativeTrees(params string[] ids)
        {
            var files = Files();
            foreach (var id in ids)
            {
                var re = new Regex("(\"id\": \"" + Regex.Escape(id) + "\",\\s*\"name_key\": \"[^\"]*\",\\s*)\"kind\": \"generic\",(\\s*)\"offer_stat\": (null|\"[a-z]+\"),(\\s*)\"offer_threshold\": [0-9.]+,");
                string before = files["trees.json"];
                files["trees.json"] = re.Replace(before, "$1\"kind\": \"native\",$2\"offer_stat\": null,$4\"offer_threshold\": null,");
                if (files["trees.json"] == before && !Regex.IsMatch(before, "\"id\": \"" + Regex.Escape(id) + "\",\\s*\"name_key\": \"[^\"]*\",\\s*\"kind\": \"native\""))
                    throw new InvalidOperationException($"'{id}' is not a generic or native tree in the shipped content");
            }
            return ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false);
        }

        /// <summary>
        /// The shipped content where drake's second chase tree, Inferno Sovereign, needs no book (a harder in-run chase, kj
        /// 2026-11-03): its Ember Codex condition becomes Heavy Armor maxed, so it asks for both drake natives and no book.
        /// </summary>
        public static ContentDb WithDrakeSecondChaseWithoutBook()
        {
            var files = Files();
            string before = files["chase_conditions.json"];
            files["chase_conditions.json"] = before.Replace(
                "\"id\": \"inferno_sovereign_c0\",\n      \"tree_id\": \"inferno_sovereign\",\n      \"type\": \"node_owned\",\n      \"ref\": \"ember_codex_n0\",",
                "\"id\": \"inferno_sovereign_c0\",\n      \"tree_id\": \"inferno_sovereign\",\n      \"type\": \"tree_maxed\",\n      \"ref\": \"heavy_armor\",");
            if (files["chase_conditions.json"] == before) throw new InvalidOperationException("inferno_sovereign_c0 is not the Ember Codex condition any more");
            return ContentDb.Load(new DictionaryContentSource(files), verifyHashes: false);
        }

        /// <summary>The 17 idea-pool trees species lanes turn native-only (#160).</summary>
        public static readonly string[] SpeciesTrees =
        {
            "blood_magic", "elemental_avatar", "fortress_fist", "vantage", "barricade", "mana_ward", "brave_heart", "guard_break",
            "latent_power", "hemorrhage", "equilibrium", "corruption", "lone_wolf", "arise", "mimic_core", "limit_break", "devourer",
        };
    }
}
