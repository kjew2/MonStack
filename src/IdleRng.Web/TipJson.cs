using System;
using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Combat;
using IdleRng.Sim.Content;

namespace IdleRng.Web
{
    /// <summary>
    /// Tooltip data for web/tooltips.js (web/wasm/BRIDGE.md, "Tooltips"). The text is built by BattleText from content strings and
    /// the battle formulas; this only maps it to JSON.
    /// </summary>
    internal static class TipJson
    {
        /// <summary>statInfo{id:{name,desc}}: what each trained stat does in battle.</summary>
        public static void StatInfo(JsonOut w, ContentDb db)
        {
            w.Key("statInfo").Obj();
            foreach (var id in StatIds.Trained)
                w.Key(id).Obj().P("name", BattleText.StatName(db, id)).P("desc", BattleText.StatDesc(db, id)).EndObj();
            w.EndObj();
        }

        /// <summary>
        /// nodeTips{nodeId:tip} for every tree node, and skillNode{skillId:nodeId} (the first node, by tree id then index, that
        /// grants each skill), so a skill named in the battle log can open its tooltip.
        /// </summary>
        public static void NodeTips(JsonOut w, ContentDb db, Func<TreeNodeDef, (string name, string desc)> nodeText)
        {
            var skillNode = new SortedDictionary<string, string>(StringComparer.Ordinal);
            w.Key("nodeTips").Obj();
            foreach (var t in db.Trees.Values.OrderBy(t => t.Id, StringComparer.Ordinal))
                for (int i = 0; i < t.Nodes.Count; i++)
                {
                    var n = t.Nodes[i];
                    var (name, desc) = nodeText(n);
                    w.Key(n.Id); Tip(w, BattleText.NodeTip(db, n, name, desc, i == t.Nodes.Count - 1));
                    if (n.Skill != null && !skillNode.ContainsKey(n.Skill.Id)) skillNode[n.Skill.Id] = n.Id;
                }
            w.EndObj();
            w.Key("skillNode").Obj();
            foreach (var kv in skillNode) w.P(kv.Key, kv.Value);
            w.EndObj();
        }

        /// <summary>towerTips{key:{name,text}}: the Daily Tower playback's entropy, vent, corruption and phase tooltips (BattleText.TowerTips).</summary>
        public static void TowerTips(JsonOut w, ContentDb db)
        {
            w.Key("towerTips").Obj();
            foreach (var (key, name, text) in BattleText.TowerTips(db)) w.Key(key).Obj().P("name", name).P("text", text).EndObj();
            w.EndObj();
        }

        /// <summary>statNow{id:text} and nowTitle for one side of the coming rank fight.</summary>
        public static void StatNow(JsonOut w, ContentDb db, MonsterStats self, MonsterStats foe, string foeName)
        {
            w.P("nowTitle", BattleText.NowTitle(db, foeName));
            w.Key("statNow").Obj();
            foreach (var kv in BattleText.StatNow(db, self, foe, foeName)) w.P(kv.Key, kv.Value);
            w.EndObj();
        }

        private static void Tip(JsonOut w, Tip t)
        {
            w.Obj().P("title", t.Title).P("text", t.Text);
            w.Key("tags").Arr();
            foreach (var s in t.Tags) w.Str(s);
            w.EndArr();
            w.Key("facts").Arr();
            foreach (var f in t.Facts) w.Arr().Str(f.Key).Str(f.Value).EndArr();
            w.EndArr();
            w.EndObj();
        }
    }
}
