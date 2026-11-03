using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Contracts;

/// <summary>JSON settings shared by the HTTP API, CLI reports and MCP tools.</summary>
public static class WorkbenchJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Readable output for files, CLI and MCP; consumers must still HTML-encode before embedding JSON in markup.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Serializable catalog, served at <c>/_workbench/api/catalog</c> and consumed by the CLI and MCP server.</summary>
public sealed class CatalogDto
{
    /// <summary>Contract version. Increment the major part on breaking changes.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    public string Title { get; init; } = "";
    public string? WorkbenchVersion { get; init; }

    /// <summary>How previews run: <c>Server</c> or <c>WebAssembly</c>. Visual baselines are kept per hosting model.</summary>
    public string HostingModel { get; init; } = "Server";
    public IReadOnlyList<ComponentDto> Components { get; init; } = [];
    public IReadOnlyList<CatalogIssue> Issues { get; init; } = [];
    public IReadOnlyList<ViewportPreset> Viewports { get; init; } = [];
    public IReadOnlyList<ThemeDto> Themes { get; init; } = [];
    public IReadOnlyList<string> Cultures { get; init; } = [];

    [JsonIgnore] public IEnumerable<ScenarioDto> Scenarios => Components.SelectMany(c => c.Scenarios);
}

public sealed record ThemeDto(string Name, ColorScheme ColorScheme, string? CssClass);

public sealed class ComponentDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string? Namespace { get; init; }
    public required string AssemblyName { get; init; }
    public string? Summary { get; init; }
    public string? Remarks { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Documentation { get; init; }
    public string? DeprecationMessage { get; init; }
    public bool IsGeneric { get; init; }
    public string? BaseType { get; init; }

    /// <summary>Source path relative to the content root when it could be resolved (never absolute).</summary>
    public string? SourcePath { get; init; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<FixtureDto> Fixtures { get; init; } = [];
    public IReadOnlyList<ScenarioDto> Scenarios { get; init; } = [];
}

public sealed record FixtureDto(string Name, string ValueType, string? Description);

public sealed class ScenarioDto
{
    public required string Id { get; init; }
    public required string ComponentId { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsGenerated { get; init; }

    /// <summary>Serializable parameter values (inspector-compatible).</summary>
    public IReadOnlyDictionary<string, JsonElement> Args { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>Descriptions of values that are not serializable (fragments, fixtures, factories).</summary>
    public IReadOnlyDictionary<string, string> ArgDescriptions { get; init; } = new Dictionary<string, string>();

    public EnvironmentDto Environment { get; init; } = new();
    public ScenarioLayout Layout { get; init; }
    public string? ReadySelector { get; init; }
    public IReadOnlyList<InteractionTest> Interactions { get; init; } = [];
    public VisualOptions Visual { get; init; } = new();
    public AccessibilityOptions Accessibility { get; init; } = new();
    public PerformanceBudgetDto Budget { get; init; } = new();
    public string? SkipReason { get; init; }
    public IReadOnlyList<string> Services { get; init; } = [];
    public string? Snippet { get; init; }

    /// <summary>Source file declaring the scenario, relative to the source root (used for change impact).</summary>
    public string? SourcePath { get; init; }
}

public sealed class EnvironmentDto
{
    public string? Culture { get; init; }
    public TextDirection Direction { get; init; }
    public string? Theme { get; init; }
    public string? Viewport { get; init; }
    public string? User { get; init; }
    public IReadOnlyList<string> UserRoles { get; init; } = [];
    public bool? Authenticated { get; init; }
    public IReadOnlyList<HttpRuleDto> Http { get; init; } = [];
    public DateTimeOffset? Clock { get; init; }
    public int? Seed { get; init; }
    public bool Offline { get; init; }
}

public sealed record HttpRuleDto(string Method, string Pattern, int Status, string? Fault, string? Gate, int? Times, double? LatencyMs, bool Custom);

public sealed class PerformanceBudgetDto
{
    public double? ReadyWithinMs { get; init; }
    public double? InteractionWithinMs { get; init; }
    public int? MaxDomUpdates { get; init; }
}

/// <summary>Result of validating the catalog (served at <c>/_workbench/api/validate</c>).</summary>
public sealed class ValidationReportDto
{
    public int Components { get; init; }
    public int Scenarios { get; init; }
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public IReadOnlyList<CatalogIssue> Issues { get; init; } = [];
}

/// <summary>Maps catalog objects to contracts.</summary>
public static class CatalogMapper
{
    public static CatalogDto ToDto(WorkbenchCatalog catalog, Func<string, string?>? sourcePathMapper = null, string? version = null, string hostingModel = "Server") => new()
    {
        Title = catalog.Options.Title,
        WorkbenchVersion = version,
        HostingModel = hostingModel,
        Components = catalog.Components.Select(c => ToDto(c, sourcePathMapper)).ToArray(),
        Issues = catalog.Issues,
        Viewports = catalog.Options.Viewports.ToArray(),
        Themes = catalog.Options.Themes.Select(t => new ThemeDto(t.Name, t.ColorScheme, t.CssClass)).ToArray(),
        Cultures = catalog.Options.Cultures.ToArray(),
    };

    public static ComponentDto ToDto(ComponentEntry component, Func<string, string?>? sourcePathMapper = null) => new()
    {
        Id = component.Id,
        Title = component.Title,
        Name = component.Metadata.Name,
        FullName = component.Metadata.FullName,
        Namespace = component.Metadata.Namespace,
        AssemblyName = component.Metadata.AssemblyName,
        Summary = component.Metadata.Summary,
        Remarks = component.Metadata.Remarks,
        Description = component.Description,
        Category = component.Category,
        Tags = component.Tags,
        Documentation = component.Documentation,
        DeprecationMessage = component.DeprecationMessage,
        IsGeneric = component.Metadata.IsGeneric,
        BaseType = component.Metadata.BaseType,
        SourcePath = component.Metadata.Source is { } source && sourcePathMapper is not null ? sourcePathMapper(source.Path) : null,
        Parameters = component.Metadata.Parameters,
        Dependencies = component.Metadata.Injections.Select(i => i.ServiceTypeName).Distinct().ToArray(),
        Fixtures = component.Fixtures.Select(f => new FixtureDto(f.Name, Naming.FormatTypeName(f.ValueType), f.Description)).ToArray(),
        Scenarios = component.Scenarios.Select(s => ToDto(s, component.Metadata, sourcePathMapper)).ToArray(),
    };

    public static ScenarioDto ToDto(ScenarioDefinition s, ComponentMetadata metadata, Func<string, string?>? sourcePathMapper = null)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in s.Parameters)
        {
            if (p.Kind is ParameterValueKind.Literal or ParameterValueKind.Text && ParameterValueConverter.TrySerialize(p.Value, out var json))
                args[p.Name] = json;
            else
                descriptions[p.Name] = p.Display ?? (p.Value is null ? "null" : Naming.FormatTypeName(p.Value.GetType()));
        }

        var env = s.Environment;
        return new ScenarioDto
        {
            Id = s.Id,
            ComponentId = s.ComponentId,
            Key = s.Key,
            Name = s.Name,
            Description = s.Description,
            Tags = s.Tags,
            IsGenerated = s.IsGenerated,
            Args = args,
            ArgDescriptions = descriptions,
            Environment = new EnvironmentDto
            {
                Culture = env.Culture,
                Direction = env.Direction,
                Theme = env.Theme,
                Viewport = env.Viewport,
                User = env.User?.IsAuthenticated == true ? env.User.Name : null,
                UserRoles = env.User?.Roles ?? [],
                Authenticated = env.User?.IsAuthenticated,
                Http = (env.Http?.Rules ?? []).Select(r => new HttpRuleDto(r.Method, r.Pattern, (int)r.Status,
                    r.Fault == HttpFault.None ? null : r.Fault.ToString(), r.Gate, r.Times, (r.Latency ?? env.Http!.Latency).TotalMilliseconds is var l && l > 0 ? l : null,
                    r.Handler is not null)).ToArray(),
                Clock = env.Clock,
                Seed = env.Seed,
                Offline = env.Offline,
            },
            Layout = s.Layout,
            ReadySelector = s.ReadySelector,
            Interactions = s.Interactions,
            Visual = s.Visual,
            Accessibility = s.Accessibility,
            Budget = new PerformanceBudgetDto
            {
                ReadyWithinMs = s.Budget.ReadyWithin?.TotalMilliseconds,
                InteractionWithinMs = s.Budget.InteractionWithin?.TotalMilliseconds,
                MaxDomUpdates = s.Budget.MaxDomUpdates,
            },
            SkipReason = s.SkipReason,
            Services = s.ServiceTypes.Select(t => Naming.FormatTypeName(t)).ToArray(),
            Snippet = Documentation.RazorSnippetGenerator.Generate(s, metadata),
            SourcePath = s.DeclaringType is { } declaring && sourcePathMapper is not null && SourceLocator.Find(declaring) is { } location
                ? sourcePathMapper(location.Path)
                : null,
        };
    }

    public static ValidationReportDto ToValidationReport(WorkbenchCatalog catalog) => new()
    {
        Components = catalog.Components.Count,
        Scenarios = catalog.Scenarios.Count,
        Errors = catalog.Issues.Count(i => i.Severity == IssueSeverity.Error),
        Warnings = catalog.Issues.Count(i => i.Severity == IssueSeverity.Warning),
        Issues = catalog.Issues,
    };
}

/// <summary>A diagnostic as exchanged with the browser and test runner.</summary>
public sealed record DiagnosticDto(string Timestamp, DiagnosticSource Source, DiagnosticSeverity Severity, string ScenarioId, string Message, string? Details, string? SessionId = null);
