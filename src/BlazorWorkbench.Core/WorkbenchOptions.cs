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

    /// <summary>
    /// Opens source files in your editor from the scenario page (<b>Edit scenario</b>, <b>Edit component</b>): a URL with
    /// <c>{path}</c> (the absolute file path, URL-encoded except for slashes) and <c>{line}</c>, for example
    /// <c>vscode://file/{path}:{line}</c>, <c>vscode-insiders://file/{path}:{line}</c>,
    /// <c>rider://open?file={path}&amp;line={line}</c> or <c>idea://open?file={path}&amp;line={line}</c>. Null or empty hides
    /// the links. ASP.NET Core hosts default to Visual Studio Code in Development when remote access is off, because the
    /// links reveal absolute paths of the machine running the Workbench.
    /// </summary>
    public string? EditorUrlTemplate { get; set; }

    /// <summary>The editor link for <paramref name="source"/>, or null when <see cref="EditorUrlTemplate"/> is not set.</summary>
    public string? EditorUrl(Catalog.SourceLocation? source)
    {
        if (string.IsNullOrWhiteSpace(EditorUrlTemplate) || source is null || string.IsNullOrEmpty(source.Path))
            return null;
        // Forward slashes, every segment URL-encoded (spaces, #, ?), drive colons kept: C:/src/My App/Button.razor.
        var segments = source.Path.Replace('\\', '/').Split('/');
        var encoded = string.Join('/', segments.Select((s, i) => i == 0 && s.Length == 2 && s[1] == ':' ? s : Uri.EscapeDataString(s)));
        return EditorUrlTemplate
            .Replace("{path}", encoded, StringComparison.Ordinal)
            .Replace("{line}", Math.Max(1, source.Line).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>Title shown in the Workbench header and published catalogs.</summary>
    public string Title { get; set; } = "Blazor Workbench";

    /// <summary>Your design system's logo in the Workbench header, instead of the Workbench mark: a URL relative to the host or absolute.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// The accent color of the Workbench shell (links, selection, focus rings), for example <c>#0f766e</c>, in the light theme.
    /// Only color values are accepted: hex, <c>rgb()</c>, <c>hsl()</c>, <c>oklch()</c> or a color name.
    /// </summary>
    public string? AccentColor { get; set; }

    /// <summary>The accent color in the dark Workbench theme; defaults to <see cref="AccentColor"/>. Pick a light color there for contrast.</summary>
    public string? AccentColorDark { get; set; }

    /// <summary>Stylesheets added to the Workbench shell (not to previews), to brand it further. Shell classes start with <c>wb-</c>.</summary>
    public IList<string> ShellStylesheets { get; } = [];

    /// <summary>True when <paramref name="value"/> is a plain CSS color value that is safe to place in a stylesheet.</summary>
    public static bool IsSafeCssColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '#' or '(' or ')' or ',' or '.' or '%' or ' ' or '-' or '/'))
            return false;
        var open = value.IndexOf('(');
        if (open < 0)
            return value.IndexOf(')') < 0; // a hex value or a color name
        // Only color functions: never url(), image(), var() or anything else that loads or computes.
        var function = value[..open].Trim().ToLowerInvariant();
        return function is "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color" &&
               value.TrimEnd().EndsWith(')') && value.Count(c => c == '(') == 1 && value.Count(c => c == ')') == 1;
    }

    /// <summary>
    /// Markdown shown at the top of the Overview page, for example what the library is, how to find your way around the
    /// catalog, or a guided tour with links to scenarios (<c>scenario/{id}?panel=environment</c>) and documentation pages
    /// (<c>docs/{id}</c>). Rendered with the safe Markdown subset: raw HTML is escaped.
    /// </summary>
    public string? Introduction { get; set; }

    /// <summary>
    /// Catalog paths listed first in the explorer, in this order, among their siblings: top-level groups (<c>"Introduction"</c>,
    /// <c>"Foundations"</c>), nested groups (<c>"Inputs/Forms"</c>), pages (<c>"Guides/Getting started"</c>) or components
    /// (<c>"Inputs/Button"</c>). Entries not listed follow: groups, then pages, then components, each alphabetically.
    /// Keyboard navigation and published catalogs use the same order.
    /// </summary>
    public IList<string> ExplorerOrder { get; } = [];

    public IList<ViewportPreset> Viewports { get; } = [.. ViewportPreset.Defaults];

    /// <summary>
    /// Named width breakpoints of the design system, such as <c>sm</c> at 640 px or <c>md</c> at 768 px. Breakpoints used in
    /// the preview stylesheets' media queries are detected without being declared; declaring them names them on the canvas
    /// ruler, offers them in the Compare view before a preview has loaded, and adds their edges to responsive sweeps.
    /// </summary>
    public IList<BreakpointPreset> Breakpoints { get; } = [];

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

    /// <summary>
    /// Your own toolbar menus (Storybook's globals): density, brand, tenant, a feature flag. The chosen value is cascaded to
    /// components by name (<c>[CascadingParameter(Name = "density")] public string? Density { get; set; }</c>), set as a
    /// <c>data-{id}</c> attribute on the preview's <c>&lt;html&gt;</c> for CSS, and available to service factories through
    /// <see cref="ScenarioContext.Global"/>. Add them with <see cref="AddGlobal"/>; scenarios fix values with <c>.Global(...)</c>.
    /// </summary>
    public IList<ToolbarGlobal> Globals { get; } = [];

    /// <summary>
    /// Panels of your own next to Controls, Environment and the others (Storybook's addon panels): a theme editor, a data
    /// inspector, your design system's checks. Add them with <see cref="AddPanel{TPanel}"/>.
    /// </summary>
    public IList<WorkbenchPanel> Panels { get; } = [];

    /// <summary>
    /// Other teams' component catalogs, published with <c>workbench publish</c>, listed in the explorer next to your own
    /// (Storybook's composition): their components appear in a group and in search, and their pages open inside the
    /// Workbench. Add them with <see cref="AddCatalogReference"/>.
    /// </summary>
    public IList<CatalogReference> CatalogReferences { get; } = [];

    /// <summary>Components of your own in the header toolbar while a scenario is open. Add them with <see cref="AddToolbarItem{TItem}"/>.</summary>
    public IList<Type> ToolbarItems { get; } = [];

    /// <summary>Network conditions offered in the toolbar; they slow down or cut off the fake HTTP backend of any scenario.</summary>
    public IList<NetworkProfile> NetworkProfiles { get; } = [.. NetworkProfile.Defaults];

    /// <summary>Backgrounds offered in the canvas toolbar, in addition to the theme's own background.</summary>
    public IList<BackgroundPreset> Backgrounds { get; } = [.. BackgroundPreset.Defaults];

    /// <summary>Cell size, in CSS pixels, of the grid overlay in the canvas toolbar.</summary>
    public int GridSize { get; set; } = 8;

    /// <summary>
    /// Gives each preview its own empty, in-memory <c>localStorage</c> and <c>sessionStorage</c> before it starts, so state
    /// written by one scenario never leaks into another, previews loaded side by side (Gallery, Compare, docs pages, the test
    /// runner) cannot disturb each other, and the browser's real storage is left alone. Scenarios seed storage with
    /// <c>.LocalStorage(...)</c>. Browsers that refuse the replacement get their storage cleared instead.
    /// </summary>
    public bool IsolateBrowserStorage { get; set; } = true;

    /// <summary>
    /// Hands the components of an active scenario an <c>IJSRuntime</c> that records their JavaScript interop calls and answers
    /// the calls planned with <c>.JSInterop(...)</c>. Turn it off only for a component library that casts <c>IJSRuntime</c> to
    /// a framework type; planned answers then do not apply.
    /// </summary>
    public bool InterceptJSInterop { get; set; } = true;

    /// <summary>
    /// Standalone documentation pages (guides, foundations, patterns), listed in the explorer next to the components. Their
    /// Markdown can embed live scenarios and component tables; see <see cref="AddPage"/>.
    /// </summary>
    public IList<DocumentationPage> Pages { get; } = [];

    /// <summary>
    /// Decorators applied to every scenario of the catalog, outside the scenarios' own <c>.Wrap(...)</c> decorators: an app
    /// shell, a theme provider or a global stylesheet container. Add them with <see cref="AddDecorator(string, Func{RenderFragment, RenderFragment})"/>.
    /// </summary>
    public IList<ScenarioDecorator> Decorators { get; } = [];

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
    /// component), <c>{{parameters:inputs-button}}</c> (parameter table), <c>{{tokens}}</c> (design tokens),
    /// <c>{{source:inputs-button--primary}}</c> (Razor markup) and <c>{{facts:inputs-button--primary}}</c> (what it simulates).
    /// Quotes starting with <c>[!NOTE]</c>, <c>[!TIP]</c>, <c>[!WARNING]</c>, <c>[!DO]</c> or <c>[!DONT]</c> become callouts.
    /// </summary>
    public WorkbenchOptions AddPage(string title, string markdown)
    {
        Pages.Add(new DocumentationPage(title, markdown));
        return this;
    }

    /// <summary>Wraps every scenario of the catalog (see <see cref="Decorators"/>).</summary>
    /// <param name="name">Name shown in the scenario facts.</param>
    /// <param name="wrap">Returns the wrapped content for a scenario's content.</param>
    public WorkbenchOptions AddDecorator(string name, Func<RenderFragment, RenderFragment> wrap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(wrap);
        Decorators.Add(new ScenarioDecorator { Name = name, Wrap = (story, _) => wrap(story) });
        return this;
    }

    /// <inheritdoc cref="AddDecorator(string, Func{RenderFragment, RenderFragment})"/>
    public WorkbenchOptions AddDecorator(string name, Func<RenderFragment, ScenarioContext, RenderFragment> wrap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(wrap);
        Decorators.Add(new ScenarioDecorator { Name = name, Wrap = wrap });
        return this;
    }

    /// <summary>Wraps every scenario of the catalog in <typeparamref name="TWrapper"/>, which receives it as <c>ChildContent</c>.</summary>
    /// <param name="parameters">Other parameters of the wrapper, by name.</param>
    /// <param name="name">Name shown in the scenario facts; defaults to the component name.</param>
    public WorkbenchOptions AddDecorator<TWrapper>(IReadOnlyDictionary<string, object?>? parameters = null, string? name = null) where TWrapper : IComponent
    {
        Decorators.Add(ScenarioDecorator.Component(typeof(TWrapper), parameters, name));
        return this;
    }

    /// <summary>
    /// Adds a panel. <typeparamref name="TPanel"/> receives the scenario, the preview and helpers through a parameter named
    /// <c>Context</c> of type <c>WorkbenchExtensionContext</c> (package BlazorWorkbench.Components). The panel renders in the
    /// Workbench shell, inside an error boundary, and its tab is in the URL (<c>?panel=x-{title}</c>).
    /// </summary>
    /// <param name="title">Tab label.</param>
    public WorkbenchOptions AddPanel<TPanel>(string title) where TPanel : IComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var panel = new WorkbenchPanel(title, typeof(TPanel));
        RequireId(panel.Id[2..], title, nameof(title));
        if (Panels.Any(p => p.Id == panel.Id))
            throw new ArgumentException($"A panel with the id '{panel.Id}' already exists.", nameof(title));
        Panels.Add(panel);
        return this;
    }

    /// <summary>
    /// Adds a component to the header toolbar while a scenario is open. Like a panel, it receives a <c>Context</c> parameter
    /// of type <c>WorkbenchExtensionContext</c>.
    /// </summary>
    public WorkbenchOptions AddToolbarItem<TItem>() where TItem : IComponent
    {
        if (ToolbarItems.Contains(typeof(TItem)))
            throw new ArgumentException($"{Naming.FormatTypeName(typeof(TItem))} is already a toolbar item.", nameof(TItem));
        ToolbarItems.Add(typeof(TItem));
        return this;
    }

    private static void RequireId(string id, string label, string parameter)
    {
        if (id.Length == 0)
            throw new ArgumentException($"'{label}' has no letters or digits to make an id from.", parameter);
    }

    // Attributes the preview itself sets on <html>; a toolbar global's data-{id} attribute must not overwrite them.
    private static readonly string[] s_reservedGlobalIds = ["theme", "dir", "lang"];

    /// <summary>
    /// Lists a published catalog (the folder <c>workbench publish</c> writes, with its <c>search.json</c>) in the explorer.
    /// </summary>
    /// <param name="title">Group label in the explorer, for example <c>Design system</c>.</param>
    /// <param name="url">The catalog folder: an absolute <c>http(s)</c> URL or a path relative to the Workbench host.</param>
    public WorkbenchOptions AddCatalogReference(string title, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var reference = new CatalogReference(title, url);
        RequireId(reference.Id, title, nameof(title));
        // "/catalogs/x/" is a path on this host (on Linux and macOS it would otherwise parse as an absolute file URI).
        var relative = url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal);
        if (!relative && Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is not ("http" or "https"))
            throw new ArgumentException("A catalog reference must be an http(s) URL or a relative path.", nameof(url));
        if (CatalogReferences.Any(r => r.Id == reference.Id))
            throw new ArgumentException($"A catalog reference with the id '{reference.Id}' already exists.", nameof(title));
        CatalogReferences.Add(reference);
        return this;
    }

    /// <summary>Adds a toolbar menu (see <see cref="Globals"/>).</summary>
    /// <param name="label">Menu label, for example <c>Density</c>; its kebab-case form is the id (<c>density</c>).</param>
    /// <param name="values">The values offered.</param>
    /// <param name="defaultValue">The value when neither the toolbar nor the scenario chooses one; null leaves it unset.</param>
    public WorkbenchOptions AddGlobal(string label, IEnumerable<string> values, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(values);
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
        if (list.Length == 0)
            throw new ArgumentException("A toolbar global needs at least one value.", nameof(values));
        if (defaultValue is not null && !list.Contains(defaultValue, StringComparer.Ordinal))
            throw new ArgumentException($"The default value '{defaultValue}' is not one of the values.", nameof(defaultValue));
        var global = new ToolbarGlobal(label, list) { DefaultValue = defaultValue };
        RequireId(global.Id, label, nameof(label));
        if (!global.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new ArgumentException($"The id '{global.Id}' of a toolbar global must use ASCII letters, digits and hyphens: it becomes an HTML attribute name.", nameof(label));
        if (s_reservedGlobalIds.Contains(global.Id) || global.Id.StartsWith("workbench", StringComparison.Ordinal) || global.Id.StartsWith("wb-", StringComparison.Ordinal))
            throw new ArgumentException($"The id '{global.Id}' is used by the Workbench preview; choose another label.", nameof(label));
        if (Globals.Any(g => g.Id == global.Id))
            throw new ArgumentException($"A toolbar global with the id '{global.Id}' already exists.", nameof(label));
        Globals.Add(global);
        return this;
    }

    /// <summary>The value of every toolbar global for a scenario: <paramref name="chosen"/> (the toolbar), else the scenario's, else the default.</summary>
    public IReadOnlyDictionary<string, string> ResolveGlobals(EnvironmentDefinition environment, IReadOnlyDictionary<string, string>? chosen)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var global in Globals)
        {
            var value = chosen?.GetValueOrDefault(global.Id) is { } c && global.Values.Contains(c, StringComparer.Ordinal) ? c
                : environment.Globals.GetValueOrDefault(global.Id) is { } s && global.Values.Contains(s, StringComparer.Ordinal) ? s
                : global.DefaultValue;
            if (value is not null)
                result[global.Id] = value;
        }

        return result;
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

/// <summary>
/// A named width breakpoint (<see cref="WorkbenchOptions.Breakpoints"/>): layouts change between <c>MinWidth - 1</c> and
/// <paramref name="MinWidth"/> CSS pixels, as with <c>@media (min-width: 768px)</c> or <c>@media (max-width: 767.98px)</c>.
/// </summary>
/// <param name="Name">Short label, for example <c>md</c>.</param>
/// <param name="MinWidth">The first width, in CSS pixels, at which the breakpoint applies.</param>
public sealed record BreakpointPreset(string Name, int MinWidth)
{
    /// <summary>The WCAG 1.4.10 (Reflow) width: content must fit 320 CSS pixels without horizontal scrolling.</summary>
    public const int ReflowWidth = 320;

    /// <summary>Smallest viewport width the Workbench offers.</summary>
    public const int MinViewportWidth = 120;

    /// <summary>Largest viewport width or height the Workbench offers.</summary>
    public const int MaxViewportSize = 3840;

    /// <summary>Most widths a responsive sweep checks.</summary>
    public const int MaxSweepWidths = 24;

    /// <summary>
    /// The widths a responsive sweep checks: <see cref="ReflowWidth"/>, then both edges of every breakpoint (the last width
    /// before it, <c>t - 1</c>, and the first width at it, <c>t</c>), in ascending order, without duplicates.
    /// </summary>
    /// <param name="thresholds">Breakpoint widths (the first width at which each applies).</param>
    public static IReadOnlyList<int> SweepWidths(IEnumerable<int> thresholds)
    {
        var widths = new SortedSet<int> { ReflowWidth };
        foreach (var t in thresholds)
        {
            widths.Add(t - 1);
            widths.Add(t);
        }

        return widths.Where(w => w is >= MinViewportWidth and <= MaxViewportSize).Take(MaxSweepWidths).ToArray();
    }
}

/// <summary>A published catalog listed in the explorer (<see cref="WorkbenchOptions.AddCatalogReference"/>).</summary>
/// <param name="Title">Group label.</param>
/// <param name="Url">The catalog folder (absolute <c>http(s)</c> URL or a path relative to the host).</param>
public sealed record CatalogReference(string Title, string Url)
{
    /// <summary>Kebab-case id used in the URL (<c>refs/{id}/{component}</c>).</summary>
    public string Id => Naming.ToSlug(Title);

    /// <summary>The catalog folder as an absolute URL, resolved against <paramref name="baseUri"/>, ending with a slash.</summary>
    public Uri Resolve(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        var folder = Url.EndsWith('/') ? Url : Url + "/";
        return new Uri(baseUri, folder);
    }
}

/// <summary>A panel of your own (<see cref="WorkbenchOptions.AddPanel{TPanel}"/>).</summary>
/// <param name="Title">Tab label.</param>
/// <param name="ComponentType">The panel component.</param>
public sealed record WorkbenchPanel(string Title, Type ComponentType)
{
    /// <summary>The tab key in the URL (<c>?panel=</c>): <c>x-</c> and the kebab-case title, so it never clashes with built-in panels.</summary>
    public string Id => "x-" + Naming.ToSlug(Title);
}

/// <summary>A toolbar menu of your own (<see cref="WorkbenchOptions.AddGlobal"/>).</summary>
/// <param name="Label">Menu label.</param>
/// <param name="Values">The values offered.</param>
public sealed record ToolbarGlobal(string Label, IReadOnlyList<string> Values)
{
    /// <summary>Kebab-case id: the cascading value's name, the <c>data-{id}</c> attribute and the URL key (<c>?g.{id}=</c>).</summary>
    public string Id => Naming.ToSlug(Label);

    /// <summary>The value when neither the toolbar nor the scenario chooses one; null leaves it unset.</summary>
    public string? DefaultValue { get; init; }
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
