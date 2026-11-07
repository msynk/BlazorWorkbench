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
| Test runners | `WorkbenchTestRunner`, `WorkbenchHost`, `PlaywrightSteps`, `ITestRunObserver` | Use them from any test framework. |
| Report exporters | `TestRunReport` (JSON contract), `ReportWriter` | Write your own exporter from `report.json`. |
| Documentation | `set.Documentation(markdown)`, XML docs, `CatalogPublisher`, `CatalogDto` | The published catalog is generated from `CatalogDto`, so a custom generator can use the same contract. |
| AI and agents | `workbench mcp` | See [MCP](mcp.md). |
| Telemetry | `WorkbenchTelemetry` (`ActivitySource` and `Meter` named `BlazorWorkbench`) | Standard OpenTelemetry sources; see [testing](testing.md#tracing-with-opentelemetry). |
| Hosting models | `WorkbenchHostOptions.Interactivity`, `BlazorWorkbench.WebAssembly`, `BlazorWorkbench.Maui` | See [WebAssembly](webassembly.md) and [Blazor Hybrid](hybrid.md). Other BlazorWebView hosts (WPF, Windows Forms) can use `AddBlazorWorkbenchHybrid`, `WorkbenchHybridFileProvider` and `HybridAutomation` from `BlazorWorkbench.Hybrid`. |
| Platform services | `BlazorWorkbench.Maui.Essentials` fakes (`FakeConnectivity`, `FakeGeolocation`, ...), `.Device(...)`, `AddSimulatedDevice()` | Use the fakes directly in tests, or write your own simulation with `.Service<T>(ctx => ...)` and report calls through `ctx.Diagnostics` (source `Platform`). |
| Project template | `dotnet new blazorworkbench` | Package `BlazorWorkbench.Templates`. |

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

The sample's [ToneMatrixProvider](../samples/SampleWorkbench/Providers/ToneMatrixProvider.cs) creates one scenario per enum value, so new tones are previewed, accessibility-scanned and baselined automatically.

## Third-party component libraries

Scenario sets can target components from any referenced assembly, for example `ScenarioSet<SomeVendor.Grid<Order>>`, without scanning the vendor assembly. Add the vendor's stylesheets and scripts with `options.PreviewStylesheets` and `options.PreviewScripts`. Register the services the vendor needs in the host before `AddBlazorWorkbench`.

## Compatibility of extension points

Extension interfaces follow semantic versioning. New members are added only as default interface members or as new interfaces in minor versions. Obsolete APIs carry `[Obsolete]` for at least one minor version before they are removed in a major version.
