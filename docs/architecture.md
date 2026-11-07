# Architecture

Blazor Workbench is a modular monolith: packages with one-way dependencies and no services that must run elsewhere.

```
                 ┌───────────────────────────────┐
                 │ BlazorWorkbench.Core          │  scenario API, discovery, metadata, validation,
                 │ (no UI, no browser)           │  contracts, redaction, docs/snippets, value conversion
                 └──────┬───────────────┬────────┘
                        │               │
     ┌──────────────────▼──┐        ┌───▼──────────────────────────┐
     │ .Components (RCL)   │        │ .Testing                     │  Playwright runner, PNG codec,
     │ UI + preview runtime│        │ (no ASP.NET dependency)      │  image diff, baselines, reports,
     └──────┬────────┬─────┘        └───┬──────────────────────────┘  catalog publisher
            │        │                  │
   ┌────────▼───┐ ┌──▼──────────────┐ ┌─▼─────────────────────────┐
   │ .Hosting   │ │ .Testing.Bunit  │ │ .Cli (workbench tool)     │  commands, exit codes, MCP server
   │ ASP.NET    │ │ bUnit adapters  │ └───────────────────────────┘
   └────────────┘ └─────────────────┘
   ┌──────────────────────┐  ┌───────────────────────────┐
   │ .WebAssembly (client)│  │ .Templates (dotnet new)   │
   └──────────────────────┘  └───────────────────────────┘
   ┌──────────────────────┐  ┌───────────────────────────┐  ┌──────────────────────────────┐
   │ .Hybrid (BlazorWebView│─▶│ .Maui (MAUI app, picker,  │  │ .Maui.Essentials (simulated  │
   │ host page, automation)│  │ WebView, Essentials regs) │  │ device services, .Device())  │
   └──────────────────────┘  └───────────────────────────┘  └──────────────────────────────┘
```

The CLI never loads user assemblies. It talks to a running host over HTTP and drives the preview pages with Playwright. The host is the single source of truth for the catalog. This keeps the CLI independent of the user's dependency graph, and each hosting model only has to implement the preview contract. A .NET MAUI Workbench app implements the same contract: a loopback automation endpoint serves the catalog, and the runner drives the app's WebView2 through the DevTools Protocol ([Blazor Hybrid](hybrid.md)).

## Runtime flow

```
 Browser (shell page, Blazor circuit #1)                Browser (preview iframe, circuit #2)
 ┌──────────────────────────────────────┐   postMessage   ┌───────────────────────────────────────┐
 │ WorkbenchShell: explorer, inspector, │ ◀────────────▶ │ workbench-preview.js (loaded first):   │
 │ panels, URL state                    │  args, gates,   │  console/error/network capture,       │
 │ workbench-shell.js (message bridge)  │  theme, a11y,   │  readiness, axe, interaction runner,  │
 └──────────────────────────────────────┘  interactions   │  window.__workbench                    │
                                                          │ PreviewPage → ScenarioHost (.NET)      │
                                                          │  ScenarioSession (scoped):             │
                                                          │   overrides, fake HTTP, auth, clock,   │
                                                          │   gates, activity, diagnostics         │
                                                          └───────────────────────────────────────┘
 CLI / Playwright ──────────── navigates directly to /_workbench/preview/{id} ─────┘
                  └─ GET /_workbench/api/catalog | validate | health
```

1. **Discovery** (`CatalogBuilder`, at startup and after hot reload) scans the configured assemblies. It finds components and public scenario sets and providers, assigns stable ids, reads metadata (parameters, injections, XML docs, PDB source paths and defaults), and validates every scenario.
2. **Service planning** (`ServiceOverrides`) collects the service types that scenarios override, together with the simulated types (`HttpClient`, `IHttpClientFactory`, `AuthenticationStateProvider`). Right before the container is built (`IHostApplicationBuilder.ConfigureContainer`), it rewrites those registrations to scoped factories that resolve through the current `ScenarioSession`, and keeps the original as a keyed fallback. `TimeProvider` stays a singleton that follows the session of the current circuit activity. See [ADR 0003](adr/0003-scenario-service-overrides.md).
3. **Preview**: the iframe loads `/_workbench/preview/{id}?args=…&culture=…&theme=…&dir=…` without prerendering. `ScenarioHost` activates the session (fresh fixtures, services, gates and clock), builds the parameters (scenario values, then overrides, then instrumented callbacks, bindings and expressions), adds cascading values and an error boundary, and renders the component through `DynamicComponent`.
4. **Bridge**: after the first render, `ScenarioHost` attaches to the preview script. Diagnostics, callbacks, gate and pending-activity changes flow to the browser and then to the shell. Shell commands (arguments, theme, gates, clock, accessibility, interactions) flow back.
5. **Readiness**: tracked activity is zero, the DOM is quiet, fonts and images are loaded, and the optional selector matches. The state is published on `data-workbench-state` and `window.__workbench`.

## Key decisions

| Decision | Why | ADR |
| --- | --- | --- |
| Modular packages, Core without UI | Discovery and validation run in tests, the CLI and MCP without a browser. | [0001](adr/0001-modular-architecture.md) |
| Preview in an iframe, CLI talks to the host over HTTP | Real CSS, media query and viewport isolation; one session per preview; no loading of user assemblies in the CLI. | [0002](adr/0002-iframe-preview-and-host-api.md) |
| Container rewrite for scenario services | Blazor injects from the circuit scope; per-subtree DI is not available. | [0003](adr/0003-scenario-service-overrides.md) |
| Depend on `Microsoft.AspNetCore.App.Internal.Assets` | Hosts without `.razor` files would otherwise lack `blazor.web.js`. | [0004](adr/0004-blazor-framework-assets.md) |
| Own PNG codec and perceptual diff | Avoids native or commercially licensed imaging dependencies. | [0005](adr/0005-image-comparison.md) |
| Serializable interaction DSL, Playwright authoritative | One definition serves the UI, the CLI and MCP; mature automation. | [0006](adr/0006-interaction-dsl.md) |
| Platform-specific baselines | Text rendering differs between operating systems. | [0007](adr/0007-platform-baselines.md) |
| WebAssembly hosting, baselines per hosting model | Components that need the browser runtime; the models render differently. | [0008](adr/0008-webassembly-hosting.md) |
| .NET MAUI Blazor Hybrid host, WebView2 driven over CDP, simulated Essentials | Components must be checked in the platform WebView with platform services; Playwright cannot launch a WebView. | [0009](adr/0009-blazor-hybrid.md) |

## Isolation model

| Concern | Isolation |
| --- | --- |
| CSS and layout | Separate document (iframe). Shell styles are not loaded into previews. |
| State | One `ScenarioSession` per circuit. Reset creates a new circuit. |
| Services | Created per activation and disposed with the session. Host registrations are untouched for other sessions. |
| Network | Fake backend on `https://workbench.invalid/`; unmatched requests return 404. |
| Navigation | Intercepted and reported. |
| Failures | Error boundary around the scenario. Diagnostics are bounded (500 per session by default). |

## Public API surface

Stable (semantic versioning applies; see [compatibility](compatibility.md)): the scenario authoring API (`ScenarioSet<T>`, builders, `By`, `InteractionBuilder`), `IScenarioProvider`, `WorkbenchOptions`/`WorkbenchHostOptions`, `ParameterEditorBase<T>`, `IVisualResultsProvider`, `IDiagnosticCollector`, the JSON contracts (`CatalogDto`, `TestRunReport`, `workbench.json`), the CLI commands and exit codes, `window.__workbench`, and the bUnit extensions.

Internal and subject to change: the runtime classes in `BlazorWorkbench.Components.Runtime` other than those listed above, the shell components, the bridge message formats and the CSS class names.
