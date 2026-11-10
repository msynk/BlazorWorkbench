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

    /// <summary>
    /// Simulated users offered in the toolbar's <b>User</b> menu. Picking one previews any scenario as that user without
    /// changing the scenario (<c>?user=admin</c> in deep links). Add them with <see cref="AddUser"/>.
    /// </summary>
    public IList<UserPreset> Users { get; } = [];

    /// <summary>Network conditions offered in the toolbar; they slow down or cut off the fake HTTP backend of any scenario.</summary>
    public IList<NetworkProfile> NetworkProfiles { get; } = [.. NetworkProfile.Defaults];

    /// <summary>Backgrounds offered in the canvas toolbar, in addition to the theme's own background.</summary>
    public IList<BackgroundPreset> Backgrounds { get; } = [.. BackgroundPreset.Defaults];

    /// <summary>Cell size, in CSS pixels, of the grid overlay in the canvas toolbar.</summary>
    public int GridSize { get; set; } = 8;

    /// <summary>
    /// Clears <c>localStorage</c> and <c>sessionStorage</c> before each preview starts (Workbench's own preferences are kept),
    /// so state written by one scenario never leaks into another. Scenarios then seed storage with <c>.LocalStorage(...)</c>.
    /// </summary>
    public bool IsolateBrowserStorage { get; set; } = true;

    /// <summary>
    /// Standalone documentation pages (guides, foundations, patterns), listed in the explorer next to the components. Their
    /// Markdown can embed live scenarios and component tables; see <see cref="AddPage"/>.
    /// </summary>
    public IList<DocumentationPage> Pages { get; } = [];

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

    /// <summary>Adds a simulated user to the toolbar's <b>User</b> menu.</summary>
    /// <param name="name">Menu label, also used in deep links (<c>?user=</c>, case-insensitive).</param>
    /// <param name="configure">The user's name, roles and claims.</param>
    public WorkbenchOptions AddUser(string name, Action<SimulatedUserBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SimulatedUserBuilder();
        configure(builder);
        Users.Add(new UserPreset(name, builder.Build()));
        return this;
    }

    /// <summary>
    /// Adds a documentation page. <paramref name="title"/> is a catalog path such as <c>Guides/Theming</c>. The Markdown is
    /// rendered safely (raw HTML is escaped) and may contain directives on their own line:
    /// <c>{{scenario:inputs-button--primary}}</c> (live preview), <c>{{scenarios:inputs-button}}</c> (every scenario of a
    /// component), <c>{{parameters:inputs-button}}</c> (parameter table) and <c>{{tokens}}</c> (design tokens).
    /// </summary>
    public WorkbenchOptions AddPage(string title, string markdown)
    {
        Pages.Add(new DocumentationPage(title, markdown));
        return this;
    }

    public UserPreset? FindUser(string? name) =>
        name is null ? null : Users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(u.Id, name, StringComparison.OrdinalIgnoreCase));

    public NetworkProfile? FindNetworkProfile(string? name) =>
        name is null ? null : NetworkProfiles.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(n.Id, name, StringComparison.OrdinalIgnoreCase));

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

/// <summary>A simulated user offered in the toolbar (<see cref="WorkbenchOptions.AddUser"/>).</summary>
public sealed record UserPreset(string Name, SimulatedUser User)
{
    /// <summary>Kebab-case id used in deep links (<c>?user=</c>).</summary>
    public string Id => Naming.ToSlug(Name);
}

/// <summary>Network conditions applied to the fake HTTP backend: extra latency per request, or no network at all.</summary>
public sealed record NetworkProfile(string Name, TimeSpan Latency, bool Offline = false)
{
    /// <summary>Kebab-case id used in deep links (<c>?network=</c>).</summary>
    public string Id => Naming.ToSlug(Name);

    /// <summary>Latencies follow the Chrome DevTools presets (round-trip time per request).</summary>
    public static IReadOnlyList<NetworkProfile> Defaults { get; } =
    [
        new("Fast 4G", TimeSpan.FromMilliseconds(150)),
        new("Fast 3G", TimeSpan.FromMilliseconds(560)),
        new("Slow 3G", TimeSpan.FromMilliseconds(2000)),
        new("Offline", TimeSpan.Zero, Offline: true),
    ];
}

/// <summary>A preview background offered in the canvas toolbar.</summary>
/// <param name="Name">Menu label.</param>
/// <param name="Value">A CSS background value, for example <c>#ffffff</c> or <c>linear-gradient(...)</c>.</param>
public sealed record BackgroundPreset(string Name, string Value)
{
    /// <summary>Kebab-case id used in deep links (<c>?bg=</c>).</summary>
    public string Id => Naming.ToSlug(Name);

    public static IReadOnlyList<BackgroundPreset> Defaults { get; } =
    [
        new("White", "#ffffff"),
        new("Light gray", "#f3f4f6"),
        new("Dark", "#1f2933"),
        new("Black", "#000000"),
        new("Transparent", "repeating-conic-gradient(#e5e7eb 0% 25%, #ffffff 0% 50%) 50% / 16px 16px"),
    ];
}

/// <summary>A standalone documentation page (<see cref="WorkbenchOptions.AddPage"/>).</summary>
/// <param name="Title">Catalog path such as <c>Guides/Theming</c>.</param>
/// <param name="Markdown">Page content in the safe Markdown subset, with optional <c>{{...}}</c> directives.</param>
public sealed record DocumentationPage(string Title, string Markdown)
{
    /// <summary>Kebab-case id (<c>guides-theming</c>), used in the URL <c>pages/{id}</c>.</summary>
    public string Id => Naming.ToKebabCase(Title.Replace('/', '-'));

    public IReadOnlyList<string> Path => Title.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public string DisplayName => Path.Count > 0 ? Path[^1] : Title;
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
