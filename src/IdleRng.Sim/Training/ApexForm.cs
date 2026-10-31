using System.Collections.Generic;
using System.Linq;
using IdleRng.Sim.Content;

namespace IdleRng.Sim.Training
{
    /// <summary>
    /// Apex forms (specs/15 section 5.3): a monster that owns a chase tree's capstone takes that tree's apex name and look.
    /// Cosmetic: nothing in a fight reads it.
    /// </summary>
    public static class ApexForm
    {
        /// <summary>
        /// The chase tree whose apex form the monster shows: the first slotted chase tree, in slot order, with every node bought.
        /// Null = the base form.
        /// </summary>
        public static string TreeOf(ContentDb c, IEnumerable<(string TreeId, int Bought)> trees)
        {
            foreach (var (id, bought) in trees)
                if (id != null && c.Trees.TryGetValue(id, out var t) && t.Kind == TreeKind.Chase && bought >= t.Nodes.Count) return id;
            return null;
        }

        public static string TreeOf(ContentDb c, RunState s) => TreeOf(c, s.Slots.Where(x => x != null).Select(x => (x.TreeId, x.Bought)));

        public static string TreeOf(ContentDb c, VaultEntry e) => TreeOf(c, e.Trees.Select(x => (x.TreeId, x.Bought)));

        /// <summary>The monster's display name: its apex form's name, or its species' name.</summary>
        public static string Name(ContentDb c, string species, string apexTree) =>
            apexTree != null ? c.Text(c.Trees[apexTree].ApexNameKey) : c.Text(c.Species[species].NameKey);
    }
}
