using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench;

/// <summary>The built (immutable) result of a <see cref="ScenarioSetBuilder{TComponent}"/>.</summary>
public sealed class ScenarioSetDefinition
{
    public required Type ComponentType { get; init; }

    /// <summary>The type that declared the set (used for diagnostics and source links). May be null for providers.</summary>
    public Type? DeclaringType { get; init; }

    /// <summary>Catalog path such as <c>Inputs/Button</c>. Defaults to the component namespace and name.</summary>
    public string? Title { get; init; }

    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Markdown documentation rendered on the component's documentation page.</summary>
    public string? Documentation { get; init; }

    public string? DeprecationMessage { get; init; }
    public IReadOnlyList<FixtureDefinition> Fixtures { get; init; } = [];
    public IReadOnlyList<ScenarioDefinition> Scenarios { get; init; } = [];
}

/// <summary>
/// A named, reproducible component state: parameters, services, cascading values, environment and tests.
/// Instances are immutable once the catalog is built and safe to share across sessions; all mutable state
/// is created per activation from the factories declared here.
/// </summary>
public sealed class ScenarioDefinition
{
    /// <summary>Stable identifier: <c>{componentId}--{key}</c>. Assigned when the catalog is built.</summary>
    public string Id { get; internal set; } = "";

    /// <summary>Component identifier (kebab case catalog path). Assigned when the catalog is built.</summary>
    public string ComponentId { get; internal set; } = "";

    /// <summary>Scenario key, unique within its component (kebab case).</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }
    public required Type ComponentType { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>True when Workbench generated the scenario because the component declares none.</summary>
    public bool IsGenerated { get; init; }

    public IReadOnlyList<ParameterAssignment> Parameters { get; init; } = [];
    public IReadOnlyList<CascadingValueDefinition> CascadingValues { get; init; } = [];
    public IReadOnlyList<Action<IServiceCollection, ScenarioContext>> ServiceConfigurations { get; init; } = [];
    public IReadOnlyList<FixtureDefinition> Fixtures { get; init; } = [];
    public EnvironmentDefinition Environment { get; init; } = new();
    public ScenarioLayout Layout { get; init; } = ScenarioLayout.Padded;

    /// <summary>CSS selector that must match before the scenario is considered ready (in addition to the built-in idle checks).</summary>
    public string? ReadySelector { get; init; }

    public IReadOnlyList<InteractionTest> Interactions { get; init; } = [];
    public VisualOptions Visual { get; init; } = new();
    public AccessibilityOptions Accessibility { get; init; } = new();
    public PerformanceBudget Budget { get; init; } = new();

    /// <summary>When set, automated test runs skip the scenario (it remains available for interactive preview).</summary>
    public string? SkipReason { get; init; }

    /// <summary>The scenario set that declared the scenario.</summary>
    public Type? DeclaringType { get; init; }

    /// <summary>Service types registered by <see cref="ServiceConfigurations"/> (computed when the catalog is built).</summary>
    public IReadOnlyList<Type> ServiceTypes { get; internal set; } = [];

    public ParameterAssignment? FindParameter(string name) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    public override string ToString() => Id.Length > 0 ? Id : Key;
}

/// <summary>How a parameter value is produced.</summary>
public enum ParameterValueKind
{
    /// <summary>A literal value captured at definition time (serializable values can be edited in the inspector).</summary>
    Literal,

    /// <summary>A string rendered as text content for a <c>RenderFragment</c> parameter.</summary>
    Text,

    /// <summary>A value created per activation from a factory (fixtures, render fragments, delegates).</summary>
    Factory,
}

/// <summary>A parameter value supplied by a scenario.</summary>
public sealed class ParameterAssignment
{
    public required string Name { get; init; }
    public required ParameterValueKind Kind { get; init; }

    /// <summary>The literal value or text (for <see cref="ParameterValueKind.Literal"/> and <see cref="ParameterValueKind.Text"/>).</summary>
    public object? Value { get; init; }

    /// <summary>Produces the value for <see cref="ParameterValueKind.Factory"/> assignments.</summary>
    public Func<ScenarioContext, object?>? Factory { get; init; }

    /// <summary>Optional human readable description used in documentation and code snippets.</summary>
    public string? Display { get; init; }

    /// <summary>Name of the fixture used to produce the value, if any.</summary>
    public string? FixtureName { get; init; }
}

/// <summary>A cascading value placed around the rendered component.</summary>
public sealed class CascadingValueDefinition
{
    public required Type ValueType { get; init; }
    public string? Name { get; init; }
    public required Func<ScenarioContext, object?> Factory { get; init; }
    public bool IsFixed { get; init; } = true;
}

/// <summary>A named value factory selectable in the parameter inspector for complex parameter types.</summary>
public sealed class FixtureDefinition
{
    public required string Name { get; init; }
    public required Type ValueType { get; init; }
    public required Func<ScenarioContext, object?> Factory { get; init; }
    public string? Description { get; init; }
}

/// <summary>Controls how the preview root lays out the component.</summary>
public enum ScenarioLayout
{
    /// <summary>Component rendered with 1rem padding (default).</summary>
    Padded,

    /// <summary>Component centered in the viewport.</summary>
    Centered,

    /// <summary>Component rendered edge to edge without padding.</summary>
    Fullscreen,
}

/// <summary>Visual regression settings for a scenario.</summary>
public sealed class VisualOptions
{
    public bool Skip { get; init; }
    public string? SkipReason { get; init; }

    /// <summary>Per pixel color distance (0..1) below which pixels are considered equal. Null uses the run default.</summary>
    public double? Threshold { get; init; }

    /// <summary>Maximum ratio (0..1) of differing pixels tolerated. Null uses the run default.</summary>
    public double? MaxDiffPixelRatio { get; init; }

    /// <summary>CSS selectors of regions masked during capture (for example timestamps). Use sparingly.</summary>
    public IReadOnlyList<string> IgnoreSelectors { get; init; } = [];

    /// <summary>Viewport preset names captured in addition to (or instead of) the run defaults.</summary>
    public IReadOnlyList<string> Viewports { get; init; } = [];

    /// <summary>Theme names captured in addition to (or instead of) the run defaults.</summary>
    public IReadOnlyList<string> Themes { get; init; } = [];

    public bool FullPage { get; init; }
}

/// <summary>Automated accessibility scan settings for a scenario.</summary>
public sealed class AccessibilityOptions
{
    public bool Skip { get; init; }
    public string? SkipReason { get; init; }

    /// <summary>axe-core rule ids disabled for this scenario. Each entry should be justified in <see cref="Justification"/>.</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = [];

    public string? Justification { get; init; }
}

/// <summary>Performance budgets checked by automated runs.</summary>
public sealed class PerformanceBudget
{
    /// <summary>Maximum time from navigation until the scenario reports ready.</summary>
    public TimeSpan? ReadyWithin { get; init; }

    /// <summary>Maximum duration of any single interaction test.</summary>
    public TimeSpan? InteractionWithin { get; init; }

    /// <summary>Maximum number of DOM update batches observed until ready.</summary>
    public int? MaxDomUpdates { get; init; }

    public bool IsEmpty => ReadyWithin is null && InteractionWithin is null && MaxDomUpdates is null;
}
