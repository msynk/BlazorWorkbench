namespace BlazorWorkbench.Catalog;

/// <summary>
/// The order of the explorer tree, shared by the explorer, keyboard navigation (Alt+arrows) and published catalogs. Within a
/// group, entries named in <see cref="WorkbenchOptions.ExplorerOrder"/> come first, in that order; the others follow:
/// groups, then documentation pages, then components, each alphabetically. Scenarios keep their declaration order.
/// </summary>
public static class ExplorerOrder
{
    /// <summary>The kind of an explorer entry, which decides its default position among its siblings.</summary>
    public enum EntryKind
    {
        Group = 0,
        Page = 1,
        Component = 2,
    }

    /// <summary>
    /// Sorts sibling entries of the explorer tree.
    /// </summary>
    /// <param name="order">The configured order (<see cref="WorkbenchOptions.ExplorerOrder"/>).</param>
    /// <param name="entries">The siblings.</param>
    /// <param name="describe">An entry's catalog path (for example <c>Inputs/Button</c>), display name and kind.</param>
    public static IEnumerable<T> Sort<T>(IList<string> order, IEnumerable<T> entries, Func<T, (string Path, string Name, EntryKind Kind)> describe)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(describe);
        return entries
            .Select(e => (Entry: e, Info: describe(e)))
            .OrderBy(x => Rank(order, x.Info.Path))
            .ThenBy(x => (int)x.Info.Kind)
            .ThenBy(x => x.Info.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Info.Path, StringComparer.Ordinal)
            .Select(x => x.Entry);
    }

    /// <summary>
    /// The position of <paramref name="path"/> in <paramref name="order"/>: the index of the first entry equal to the path
    /// (case-insensitive, ignoring spaces around slashes), or <see cref="int.MaxValue"/> when it is not listed.
    /// </summary>
    public static int Rank(IList<string> order, string path)
    {
        var normalized = Normalize(path);
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(Normalize(order[i]), normalized, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    /// <summary>Every component of the catalog, in the order the explorer tree shows them.</summary>
    public static IReadOnlyList<ComponentEntry> Components(WorkbenchCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var order = catalog.Options.ExplorerOrder;
        var result = new List<ComponentEntry>(catalog.Components.Count);
        Visit(catalog.Components.ToList(), 0);
        return result;

        void Visit(List<ComponentEntry> components, int depth)
        {
            // Siblings at this depth: a component whose path ends here, or a group holding deeper components.
            var siblings = components
                .GroupBy(c => c.Path.Count - 1 <= depth ? "c:" + c.Id : "g:" + c.Path[depth], StringComparer.Ordinal)
                .Select(g => (Key: g.Key, Items: g.ToList()));
            var sorted = Sort(order, siblings, s =>
            {
                var first = s.Items[0];
                var path = string.Join('/', first.Path.Take(depth + 1));
                return s.Key.StartsWith("c:", StringComparison.Ordinal)
                    ? (first.Title, first.DisplayName, EntryKind.Component)
                    : (path, first.Path[depth], EntryKind.Group);
            });
            foreach (var (key, items) in sorted)
            {
                if (key.StartsWith("c:", StringComparison.Ordinal))
                    result.AddRange(items);
                else
                    Visit(items, depth + 1);
            }
        }
    }

    private static string Normalize(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
