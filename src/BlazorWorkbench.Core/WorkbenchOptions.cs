using System.Reflection;
using BlazorWorkbench.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench;

/// <summary>Configures discovery, environments and presets. Shared by the preview host, the CLI and bUnit integration.</summary>
public class WorkbenchOptions
{
    /// <summary>Assemblies scanned for components and scenario sets.</summary>
    public IList<Assembly> Assemblies { get; } = [];

    /// <summary>Assemblies scanned only for scenario sets and providers (not for components). The preview host adds its entry assembly.</summary>
    public IList<Assembly> ScenarioAssemblies { get; } = [];

    /// <summary>Components registered explicitly (for example closed generic types or components outside scanned assemblies).</summary>
    public IList<Type> Components { get; } = [];

    /// <summary>Scenario sets registered explicitly (in addition to discovered ones).</summary>
    public IList<IScenarioSet> ScenarioSets { get; } = [];

    /// <summary>Custom scenario providers (in addition to discovered ones).</summary>
    public IList<IScenarioProvider> ScenarioProviders { get; } = [];

    /// <summary>Additional filter applied to automatically discovered component types. Return false to exclude.</summary>
    public Func<Type, bool>? ComponentFilter { get; set; }

    /// <summary>Namespaces (and their children) excluded from automatic component discovery.</summary>
    public ISet<string> ExcludedNamespaces { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Discover components that declare <c>@page</c> routes (off by default; pages usually need app infrastructure).</summary>
    public bool IncludeRoutableComponents { get; set; }

    /// <summary>Discover components that have no scenarios and generate a "Default" scenario for them (on by default).</summary>
    public bool IncludeComponentsWithoutScenarios { get; set; } = true;

    /// <summary>Construct components once during discovery to read parameter default values.</summary>
    public bool ReadParameterDefaults { get; set; } = true;

    /// <summary>Resolve component source files from portable PDBs (absolute paths are never published).</summary>
    public bool ResolveSourceLocations { get; set; } = true;

    /// <summary>Title shown in the Workbench header and published catalogs.</summary>
    public string Title { get; set; } = "Blazor Workbench";

    /// <summary>
    /// Markdown shown at the top of the Overview page, for example what the library is, how to find your way around the
    /// catalog, or a guided tour with links to scenarios (<c>scenario/{id}?panel=environment</c>) and documentation pages
    /// (<c>docs/{id}</c>). Rendered with the safe Markdown subset: raw HTML is escaped.
    /// </summary>
    public string? Introduction { get; set; }

    public IList<ViewportPreset> Viewports { get; } = [.. ViewportPreset.Defaults];
    public IList<ThemeDefinition> Themes { get; } = [.. ThemeDefinition.Defaults];

    /// <summary>Cultures offered in the toolbar.</summary>
    public IList<string> Cultures { get; } = ["en-US", "fr-FR", "de-DE", "ja-JP", "ar-SA", "fa-IR"];

    /// <summary>Custom parameter editors (value type to editor component type deriving from <c>ParameterEditorBase&lt;T&gt;</c>).</summary>
    public IDictionary<Type, Type> ParameterEditors { get; } = new Dictionary<Type, Type>();

    public RedactionOptions Redaction { get; } = new();

    /// <summary>Maximum diagnostics retained per preview session (oldest are dropped).</summary>
    public int MaxDiagnostics { get; set; } = 500;

    /// <summary>Adds the assembly containing <typeparamref name="T"/>.</summary>
    public WorkbenchOptions AddAssemblyOf<T>() => AddAssembly(typeof(T).Assembly);

    public WorkbenchOptions AddAssembly(Assembly assembly)
    {
        if (!Assemblies.Contains(assembly))
            Assemblies.Add(assembly);
        return this;
    }

    /// <summary>Registers a component explicitly (works for closed generic types).</summary>
    public WorkbenchOptions AddComponent<TComponent>() where TComponent : IComponent => AddComponent(typeof(TComponent));

    public WorkbenchOptions AddComponent(Type componentType)
    {
        if (!Components.Contains(componentType))
            Components.Add(componentType);
        return this;
    }

    public WorkbenchOptions AddScenarios(IScenarioSet set)
    {
        ScenarioSets.Add(set);
        return this;
    }

    /// <summary>Registers a custom editor component for parameters of type <typeparamref name="TValue"/>.</summary>
    public WorkbenchOptions AddParameterEditor<TValue, TEditor>() where TEditor : IComponent
    {
        ParameterEditors[typeof(TValue)] = typeof(TEditor);
        return this;
    }

    public ViewportPreset? FindViewport(string? name) =>
        name is null ? null : Viewports.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    public ThemeDefinition? FindTheme(string? name) =>
        name is null ? null : Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A named viewport size.</summary>
public sealed record ViewportPreset(string Name, int Width, int Height, double DeviceScaleFactor = 1, bool IsMobile = false)
{
    public static IReadOnlyList<ViewportPreset> Defaults { get; } =
    [
        new("mobile", 375, 667, 1, true),
        new("tablet", 768, 1024),
        new("laptop", 1280, 800),
        new("desktop", 1440, 900),
    ];
}

/// <summary>Color scheme reported to the browser for a theme.</summary>
public enum ColorScheme
{
    Light,
    Dark,
}

/// <summary>
/// A preview theme. Applied to the preview document's <c>&lt;html&gt;</c> element as <c>data-theme</c> (and <see cref="CssClass"/>),
/// with <c>color-scheme</c> set. Automated runs additionally emulate <c>prefers-color-scheme</c>.
/// </summary>
public sealed record ThemeDefinition(string Name, ColorScheme ColorScheme = ColorScheme.Light, string? CssClass = null)
{
    /// <summary>Additional stylesheets loaded in the preview when the theme is active.</summary>
    public IReadOnlyList<string> Stylesheets { get; init; } = [];

    public static IReadOnlyList<ThemeDefinition> Defaults { get; } =
    [
        new("light", ColorScheme.Light),
        new("dark", ColorScheme.Dark),
    ];
}
