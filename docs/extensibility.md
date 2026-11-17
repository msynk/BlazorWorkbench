# Extensibility

| Extension point | Interface or API | Lifetime and rules |
| --- | --- | --- |
| Scenario providers | `IScenarioProvider` | Discovered when public with a parameterless constructor, or added with `options.ScenarioProviders`. Called once per catalog build. An exception becomes `WB0008` and does not stop discovery. |
| Component discovery | `options.Assemblies`, `options.Components`, `options.ComponentFilter`, `options.ExcludedNamespaces`, `[WorkbenchIgnore]`, `IncludeRoutableComponents` | Evaluated once per catalog build. |
| Parameter editors | `ParameterEditorBase<T>` + `options.AddParameterEditor<T, TEditor>()` | Rendered in the shell. Values must be JSON serializable. |
| Fixtures | `set.Fixture<T>(name, factory)` | Factory runs per activation and its result is cached for the session. |
| Environment simulation | `.Services(...)`, `.Http(...).Handle(...)`, gates, `ScenarioContext` | Per activation. Services are disposed with the session. |
| Diagnostic collectors | `IDiagnosticCollector` (register in DI) | Called synchronously for every diagnostic after redaction. Must not throw; exceptions are swallowed. |
| Visual results | `IVisualResultsProvider` | Replaces the file-based provider of the Visual panel, for example with results from a CI server. |
| Test results | `ITestResultsProvider` | Supplies the last run's scenario statuses to the explorer (dots, failure counts, `is:failing`). The default reads `report.json`. |
| Toolbar presets | `options.Users`/`AddUser`, `options.NetworkProfiles`, `options.Backgrounds`, `options.GridSize` | Entries of the User, Network and Background menus. |
| Toolbar globals | `options.AddGlobal(label, values)`, `.Global(id, value)`, `ctx.Global(id)` | Your own toolbar menus; the value is cascaded by id, set as `data-{id}` on the preview and given to service factories. See [tools](tools.md#canvas-toolbar). |
| Panels and toolbar items | `options.AddPanel<T>(title)`, `options.AddToolbarItem<T>()`, `WorkbenchExtensionContext` | Rendered in the shell inside an error boundary; see below. |
| Decorators | `.Wrap(...)`, `options.AddDecorator(...)` | Wrap scenarios in markup or components; see [scenarios](scenarios.md#decorators). |
| Documentation pages | `options.AddPage(title, markdown)` | Standalone pages with `{{scenario:id}}`, `{{scenarios:id}}`, `{{parameters:id}}` and `{{tokens}}` embeds. |
| Test runners | `WorkbenchTestRunner`, `WorkbenchHost`, `PlaywrightSteps`, `ITestRunObserver` | Use them from any test framework. |
| Report exporters | `TestRunReport` (JSON contract), `ReportWriter` | Write your own exporter from `report.json`. |
| Documentation | `set.Documentation(markdown)`, XML docs, `CatalogPublisher`, `CatalogDto` | The published catalog is generated from `CatalogDto`, so a custom generator can use the same contract. |
| AI and agents | `workbench mcp` | See [MCP](mcp.md). |
| Telemetry | `WorkbenchTelemetry` (`ActivitySource` and `Meter` named `BlazorWorkbench`) | Standard OpenTelemetry sources; see [testing](testing.md#tracing-with-opentelemetry). |
| Hosting models | `WorkbenchHostOptions.Interactivity`, `BlazorWorkbench.WebAssembly`, `BlazorWorkbench.Maui` | See [WebAssembly](webassembly.md) and [Blazor Hybrid](hybrid.md). Other BlazorWebView hosts (WPF, Windows Forms) can use `AddBlazorWorkbenchHybrid`, `WorkbenchHybridFileProvider` and `HybridAutomation` from `BlazorWorkbench.Hybrid`. |
| Platform services | `BlazorWorkbench.Maui.Essentials` fakes (`FakeConnectivity`, `FakeGeolocation`, ...), `.Device(...)`, `AddSimulatedDevice()` | Use the fakes directly in tests, or write your own simulation with `.Service<T>(ctx => ...)` and report calls through `ctx.Diagnostics` (source `Platform`). |
| Project template | `dotnet new blazorworkbench` | Package `BlazorWorkbench.Templates`. |

## Panels and toolbar items of your own

A panel is a component with a `Context` parameter. It appears as a tab next to Controls, Environment and the others (its
key in the URL is `x-` and its kebab-case title), and renders in the Workbench shell, inside an error boundary:

```razor
@* Register with options.AddPanel<StylesPanel>("Styles"). *@
<button class="wb-button" @onclick="ReadAsync">Read</button> <code>@_radius</code>

@code {
    private string? _radius;

    [Parameter] public WorkbenchExtensionContext Context { get; set; } = default!;

    private async Task ReadAsync() =>
        _radius = (await Context.EvaluateAsync("return getComputedStyle(root.firstElementChild).borderRadius;")).GetString();
}
```

`WorkbenchExtensionContext` gives the open `Scenario` and `Component`, the `Options`, the Controls panel's `Args`, the
preview's `Diagnostics` and `PreviewState`, and `PreviewVersion`, which increases every time the preview becomes ready (compare
it in `OnParametersSetAsync` to refresh). Actions: `SetArgsAsync(...)` changes the arguments live, `ReloadAsync()` starts a new
session, `HighlightAsync(selector)` outlines elements, and `EvaluateAsync(script)` runs JavaScript in the preview document:
the script is the body of an async function with `root` (the preview root element) and `workbench` (the automation API), and
its JSON-serializable result comes back as a `JsonElement`. `EvaluateAsync` needs Canvas mode (`HasPreview`) and times out
after 10 seconds by default. A toolbar item (`options.AddToolbarItem<T>()`) receives the same context and renders in the
header toolbar. Extension components are excluded from component discovery. The demo's
[Styles panel](../demo/BlazorWorkbench.Demo/Extensions/StylesPanel.razor) is a complete example.

Use the shell's CSS classes (`wb-button`, `wb-table`, `wb-muted`, `wb-inspector-actions`) to match its look in both themes.

## Example: forwarding diagnostics to logging

```csharp
public sealed class LoggingCollector(ILogger<LoggingCollector> logger) : IDiagnosticCollector
{
    public void OnDiagnostic(DiagnosticEvent e)
    {
        if (e.Severity >= DiagnosticSeverity.Warning)
            logger.LogWarning("[{Scenario}] {Source}: {Message}", e.ScenarioId, e.Source, e.Message);
    }
}

builder.Services.AddSingleton<IDiagnosticCollector, LoggingCollector>();
```

## Example: data-driven provider

The demo's [ToneMatrixProvider](../demo/BlazorWorkbench.Demo/Providers/ToneMatrixProvider.cs) creates one scenario per enum value, so new tones are previewed, accessibility-scanned and baselined automatically.

## Third-party component libraries

Scenario sets can target components from any referenced assembly, for example `ScenarioSet<SomeVendor.Grid<Order>>`, without scanning the vendor assembly. Add the vendor's stylesheets and scripts with `options.PreviewStylesheets` and `options.PreviewScripts`. Register the services the vendor needs in the host before `AddBlazorWorkbench`.

## Compatibility of extension points

Extension interfaces follow semantic versioning. New members are added only as default interface members or as new interfaces in minor versions. Obsolete APIs carry `[Obsolete]` for at least one minor version before they are removed in a major version.
