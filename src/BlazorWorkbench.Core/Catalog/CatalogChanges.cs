using System.Text.Json.Serialization;

namespace BlazorWorkbench.Catalog;

/// <summary>How a working-copy change relates to a scenario.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScenarioChangeKind>))]
public enum ScenarioChangeKind
{
    /// <summary>The scenario's set or its component's source file is new (not yet tracked by version control).</summary>
    New,

    /// <summary>The scenario's set or its component's source (markup, code-behind or isolated CSS) changed.</summary>
    Modified,

    /// <summary>The component renders a changed component, directly or through other components.</summary>
    Related,
}

/// <summary>A changed file reported by version control.</summary>
/// <param name="Path">Absolute path.</param>
/// <param name="IsNew">True for files version control does not track yet.</param>
public sealed record ChangedFile(string Path, bool IsNew);

/// <summary>The scenarios affected by the changes in the working copy.</summary>
/// <param name="Scenarios">Change kind of every affected scenario, by scenario id.</param>
/// <param name="Files">Changed files, relative to the source root where possible.</param>
/// <param name="Description">How the changes were found, or why they could not be (for example, no git repository).</param>
public sealed record CatalogChangeSummary(IReadOnlyDictionary<string, ScenarioChangeKind> Scenarios, IReadOnlyList<string> Files, string? Description)
{
    public static CatalogChangeSummary Empty { get; } = new(new Dictionary<string, ScenarioChangeKind>(), [], null);
}

/// <summary>
/// Maps changed source files to scenarios, for the explorer's change filter: a component whose markup, code-behind
/// (<c>.razor.cs</c>) or isolated stylesheet (<c>.razor.css</c>) changed, a scenario set that changed, and the components
/// that render a changed component (related). Source paths come from the PDBs, so components without symbols are not
/// matched.
/// </summary>
public static class CatalogChanges
{
    private static readonly string[] s_companionSuffixes = [".cs", ".css", ".js"];

    public static CatalogChangeSummary Map(WorkbenchCatalog catalog, IEnumerable<ChangedFile> changes, string? sourceRoot = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(changes);
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new Dictionary<string, bool>(comparer);
        foreach (var change in changes)
        {
            foreach (var path in Candidates(Normalize(change.Path)))
                files[path] = files.TryGetValue(path, out var isNew) ? isNew && change.IsNew : change.IsNew;
        }

        var result = new Dictionary<string, ScenarioChangeKind>(StringComparer.OrdinalIgnoreCase);
        void Mark(string scenarioId, ScenarioChangeKind kind)
        {
            // New beats modified beats related.
            if (!result.TryGetValue(scenarioId, out var existing) || kind < existing)
                result[scenarioId] = kind;
        }

        var changedComponents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in catalog.Components)
        {
            if (component.Metadata.Source?.Path is { } source && files.TryGetValue(Normalize(source), out var isNew))
            {
                changedComponents.Add(component.Id);
                foreach (var scenario in component.Scenarios)
                    Mark(scenario.Id, isNew ? ScenarioChangeKind.New : ScenarioChangeKind.Modified);
            }
        }

        var setSources = new Dictionary<Type, string?>();
        foreach (var scenario in catalog.Scenarios)
        {
            if (scenario.DeclaringType is not { } declaring)
                continue;
            if (!setSources.TryGetValue(declaring, out var path))
                setSources[declaring] = path = SourceLocator.Find(declaring)?.Path is { } p ? Normalize(p) : null;
            if (path is not null && files.TryGetValue(path, out var isNew))
                Mark(scenario.Id, isNew ? ScenarioChangeKind.New : ScenarioChangeKind.Modified);
        }

        // Components that render a changed component, transitively.
        var byId = catalog.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var queue = new Queue<string>(changedComponents);
        var seen = new HashSet<string>(changedComponents, StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            if (!byId.TryGetValue(queue.Dequeue(), out var changed))
                continue;
            foreach (var user in changed.UsedBy)
            {
                if (!seen.Add(user) || !byId.TryGetValue(user, out var related))
                    continue;
                foreach (var scenario in related.Scenarios)
                    Mark(scenario.Id, ScenarioChangeKind.Related);
                queue.Enqueue(user);
            }
        }

        var root = sourceRoot is null ? null : Normalize(sourceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var listed = changes.Select(c => Normalize(c.Path))
            .Select(p => root is not null && p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p[root.Length..].Replace('\\', '/') : Path.GetFileName(p))
            .Distinct(comparer).Order(StringComparer.Ordinal).ToArray();
        return new CatalogChangeSummary(result, listed, description);
    }

    /// <summary>The file itself, and for companions of a Razor component (<c>X.razor.cs</c>, <c>X.razor.css</c>) the component file.</summary>
    private static IEnumerable<string> Candidates(string path)
    {
        yield return path;
        foreach (var suffix in s_companionSuffixes)
        {
            if (path.EndsWith(".razor" + suffix, StringComparison.OrdinalIgnoreCase))
                yield return path[..^suffix.Length];
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
