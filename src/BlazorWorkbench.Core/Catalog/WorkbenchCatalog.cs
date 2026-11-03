using System.Text.Json.Serialization;

namespace BlazorWorkbench.Catalog;

[JsonConverter(typeof(JsonStringEnumConverter<IssueSeverity>))]
public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A discovery or validation finding. Codes are stable and documented in <c>docs/diagnostics-codes.md</c>.</summary>
public sealed record CatalogIssue(
    IssueSeverity Severity,
    string Code,
    string Message,
    string? ComponentId = null,
    string? ScenarioId = null,
    string? TypeName = null,
    string? Remedy = null)
{
    public override string ToString() =>
        $"{Severity.ToString().ToLowerInvariant()} {Code}: {Message}{(ScenarioId is not null ? $" [{ScenarioId}]" : ComponentId is not null ? $" [{ComponentId}]" : "")}";
}

/// <summary>A component in the catalog with its scenarios.</summary>
public sealed class ComponentEntry
{
    public required string Id { get; init; }

    /// <summary>Slash separated catalog path (<c>Inputs/Button</c>).</summary>
    public required string Title { get; init; }

    public IReadOnlyList<string> Path => Title.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public string DisplayName => Path.Count > 0 ? Path[^1] : Title;
    public required ComponentMetadata Metadata { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Documentation { get; init; }
    public string? DeprecationMessage { get; init; }
    public IReadOnlyList<FixtureDefinition> Fixtures { get; init; } = [];
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; internal set; } = [];

    /// <summary>True when the component has only a generated default scenario.</summary>
    public bool HasOnlyGeneratedScenarios => Scenarios.All(s => s.IsGenerated);

    public Type ComponentType => Metadata.Type;
}

/// <summary>
/// The immutable result of discovery: components, scenarios and issues. Thread safe; build once per process
/// (or again when assemblies change) with <see cref="CatalogBuilder"/>.
/// </summary>
public sealed class WorkbenchCatalog
{
    private readonly Dictionary<string, ScenarioDefinition> _scenarios;
    private readonly Dictionary<string, ComponentEntry> _components;

    internal WorkbenchCatalog(IReadOnlyList<ComponentEntry> components, IReadOnlyList<CatalogIssue> issues, WorkbenchOptions options)
    {
        Components = components;
        Issues = issues;
        Options = options;
        Scenarios = components.SelectMany(c => c.Scenarios).ToArray();
        _components = components.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _scenarios = new Dictionary<string, ScenarioDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in Scenarios)
            _scenarios.TryAdd(scenario.Id, scenario);
    }

    public IReadOnlyList<ComponentEntry> Components { get; }
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; }
    public IReadOnlyList<CatalogIssue> Issues { get; }
    public WorkbenchOptions Options { get; }

    /// <summary>Returns a catalog with additional issues (for example dependency checks that need the built service provider).</summary>
    public WorkbenchCatalog WithIssues(IEnumerable<CatalogIssue> additional) =>
        new(Components, [.. Issues, .. additional], Options);

    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    public ScenarioDefinition? FindScenario(string id) => _scenarios.GetValueOrDefault(id);
    public ComponentEntry? FindComponent(string id) => _components.GetValueOrDefault(id);
    public ComponentEntry? FindComponent(Type type) => Components.FirstOrDefault(c => c.ComponentType == type);
    public ComponentEntry? GetComponentOf(ScenarioDefinition scenario) => FindComponent(scenario.ComponentId);

    public IEnumerable<CatalogIssue> IssuesFor(ScenarioDefinition scenario) =>
        Issues.Where(i => string.Equals(i.ScenarioId, scenario.Id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<CatalogIssue> IssuesFor(ComponentEntry component) =>
        Issues.Where(i => string.Equals(i.ComponentId, component.Id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filters scenarios with a glob-like pattern matched against scenario ids, component titles and tags.
    /// Supports <c>*</c> wildcards, <c>tag:name</c>, and comma separated alternatives. Results keep catalog order.
    /// </summary>
    public IReadOnlyList<ScenarioDefinition> Filter(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return Scenarios;

        var terms = pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Scenarios.Where(s => terms.Any(t => Matches(s, t))).ToArray();
    }

    private bool Matches(ScenarioDefinition scenario, string term)
    {
        if (term.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            var tag = term[4..];
            var component = FindComponent(scenario.ComponentId);
            return scenario.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ||
                   (component?.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ?? false);
        }

        var title = FindComponent(scenario.ComponentId)?.Title ?? "";
        return Glob.IsMatch(scenario.Id, term) || Glob.IsMatch(scenario.ComponentId, term) ||
               Glob.IsMatch($"{title}/{scenario.Name}", term);
    }
}

/// <summary>Minimal case-insensitive glob matching (<c>*</c> matches any sequence, <c>?</c> one character).</summary>
public static class Glob
{
    public static bool IsMatch(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t])))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }
}
