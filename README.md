# Blazor Workbench

Blazor Workbench is a component engineering and quality platform for Blazor. You can discover, preview, configure, document, test and visually validate Razor components in isolation, without writing demo pages.

What sets it apart from Storybook-style galleries:

- **Reproducible scenarios.** Scenarios are strongly typed C# definitions. Each one has a stable id, a deep link, seeded data and a fixed clock.
- **Environment simulation.** Each scenario can override services, use a fake HTTP backend (latency, failures, timeouts, offline), simulate users and roles, and set culture, RTL direction, theme and viewport. The overrides never touch production registrations.
- **Deterministic async.** *Gates* hold requests or fake services in a loading state until a developer or a test releases them. Readiness is based on tracked activity and DOM quiescence, not on sleeps.
- **Integrated testing.** One scenario serves as an interactive preview, a bUnit render test, a Playwright interaction test, an axe-core accessibility scan and a visual regression check.
- **Actionable diagnostics.** Exceptions, console errors, failed requests, intercepted navigation, callbacks and focus changes are recorded with their scenario id, a timestamp and redacted data.
- **Tools for looking closely.** Zoom, rotation and custom viewports; backgrounds, grid, outlines and a measure tool; forced `:hover` and `:focus-visible` states; color vision simulation, 200% text, text spacing and tab order; a design overlay with a difference blend; rendered HTML; browser storage; and **Compare** (one scenario across themes, viewports, cultures, users or states) and **Gallery** (every scenario, matrices as tables) views. Everything is in the URL.
- **Write less, cover more.** `set.Matrix(...)` turns parameter combinations into scenarios, with all-pairs reduction. The recorder turns clicks and typing into an interaction test, and the debugger steps through one. Lifecycle statuses, notes, design references, documentation pages with live embeds, design tokens read from your stylesheets, parameter value coverage and a "renders / rendered by" graph come with the catalog.
- **Every hosting model.** The same scenarios run with Interactive Server, Interactive WebAssembly and in a .NET MAUI Blazor Hybrid app, inside the platform WebView. Simulated MAUI Essentials services (network, location, share, battery, ...) let you preview device-dependent components anywhere.

```
┌──────────────┬────────────────────────────────────────────────────────────────┐
│ Explorer     │  Inputs / Button — Primary                    ● Ready  249 ms  │
│  ▸ Inputs    │ ┌────────────────────────────────────────────────────────────┐ │
│    Button    │ │  [ Save changes ]          (live preview in an iframe)     │ │
│     Primary  │ └────────────────────────────────────────────────────────────┘ │
│     Danger   │  Controls │ Environment │ Actions │ Diagnostics │ Interactions │
│  ▸ Data      │  Accessibility │ Visual │ Design │ HTML │ Storage │ Code        │
└──────────────┴────────────────────────────────────────────────────────────────┘
```

## Quick start

Workbench needs the .NET 10 SDK. The fastest start is the template:

```bash
dotnet new install BlazorWorkbench.Templates
dotnet new blazorworkbench -n MyComponents.Workbench
```

You can also set up a host by hand:

1. Create a host project that references your component library and the Workbench package:

   ```bash
   dotnet new web -n MyComponents.Workbench
   dotnet add MyComponents.Workbench package BlazorWorkbench.Hosting
   dotnet add MyComponents.Workbench reference ../MyComponents/MyComponents.csproj
   ```

2. Replace `Program.cs`:

   ```csharp
   using BlazorWorkbench.Hosting;

   var builder = WebApplication.CreateBuilder(args);
   builder.AddBlazorWorkbench(o => o.AddAssemblyOf<MyComponents.Button>());
   var app = builder.Build();
   app.MapBlazorWorkbench();
   app.Run();
   ```

3. Run it with `dotnet watch` and open the URL. Every component already has a generated *Default* scenario. You can add scenarios for meaningful states:

   ```csharp
   public sealed class ButtonScenarios : ScenarioSet<Button>
   {
       protected override void Configure(ScenarioSetBuilder<Button> set)
       {
           set.Add("Primary", s => s
               .ChildContent("Save")
               .Interaction("click raises OnClick", i => i
                   .Click(By.Role("button", "Save"))
                   .ExpectCallback("OnClick", times: 1)));
           set.Add("Danger", s => s.Parameter(b => b.Variant, ButtonVariant.Danger).ChildContent("Delete"));
       }
   }
   ```

4. Test from the command line or in CI:

   ```bash
   dotnet tool install -g BlazorWorkbench.Cli
   workbench init                       # writes workbench.json
   workbench install-browsers           # Playwright Chromium
   workbench test --a11y --visual       # renders, interactions, axe, screenshots
   workbench approve                    # explicitly approve new or changed screenshots
   ```

For a complete walkthrough, see [docs/getting-started.md](docs/getting-started.md). To run the scenarios in a .NET MAUI app, see [docs/hybrid.md](docs/hybrid.md).

## See it in action

The [demo](demo/README.md) is *Trailhead UI*, a component library for a fictional outdoor store, with 181 scenarios that run in all three hosting models. Clone the repository and run `dotnet run --project demo/BlazorWorkbench.Demo`: the overview page is a guided tour, and every stop opens a scenario with the right panel already open. You will decline a payment without a payment provider, freeze a spinner and release it, fast-forward a session timeout with the fake clock, see one settings page through the eyes of six simulated users, take down one endpoint of a dashboard, watch axe-core catch a button without a name, overlay a design mockup on a button, see a form through deuteranopia at 200% text, and record a test by clicking. The chips under each scenario's title list what it simulates and verifies.

## Packages

| Package | Purpose |
| --- | --- |
| `BlazorWorkbench.Core` | Scenario authoring API, discovery, validation, contracts, redaction. No UI or browser dependency. |
| `BlazorWorkbench.Components` | Workbench UI and preview runtime (session, fake HTTP, auth, clock, diagnostics). |
| `BlazorWorkbench.Hosting` | `AddBlazorWorkbench` / `MapBlazorWorkbench` for ASP.NET Core (Interactive Server or WebAssembly), catalog API and secure defaults. |
| `BlazorWorkbench.WebAssembly` | Client package for Interactive WebAssembly hosts (`AddBlazorWorkbenchAsync`). |
| `BlazorWorkbench.Maui` | .NET MAUI Blazor Hybrid Workbench app (`UseBlazorWorkbenchApp`): native scenario picker, previews in the platform WebView, automation on Windows. |
| `BlazorWorkbench.Hybrid` | Platform-neutral BlazorWebView support used by `BlazorWorkbench.Maui`: generated host page, axe-core file, loopback automation API. |
| `BlazorWorkbench.Maui.Essentials` | Simulated MAUI Essentials services and `.Device(...)` for scenarios, in every hosting model and in bUnit. |
| `BlazorWorkbench.Templates` | `dotnet new blazorworkbench` project template. |
| `BlazorWorkbench.Testing` | Playwright runner, image comparison, baselines, JSON/JUnit/HTML reports, static catalog publisher. |
| `BlazorWorkbench.Testing.Bunit` | Render scenarios in bUnit tests and smoke-test every scenario without a browser. |
| `BlazorWorkbench.Cli` | The `workbench` .NET tool: `list`, `validate`, `test`, `a11y`, `approve`, `publish`, `dev`, `init`, `mcp`. |

## Documentation

- [Demo tour](demo/README.md) · [Getting started](docs/getting-started.md) · [Scenario authoring](docs/scenarios.md) · [Environment simulation](docs/environment.md) · [Workbench tools](docs/tools.md) · [Parameter editors](docs/parameter-editors.md)
- [WebAssembly hosting](docs/webassembly.md) · [.NET MAUI Blazor Hybrid](docs/hybrid.md) · [Testing and Playwright](docs/testing.md) · [bUnit integration](docs/bunit.md) · [Accessibility](docs/accessibility.md) · [Visual regression](docs/visual-regression.md)
- [CLI reference](docs/cli.md) · [CI setup](docs/ci.md) · [Publishing catalogs](docs/publishing.md) · [MCP for coding agents](docs/mcp.md)
- [Architecture](docs/architecture.md) · [Extensibility](docs/extensibility.md) · [Security and privacy](docs/security.md) · [Compatibility](docs/compatibility.md)
- [Troubleshooting](docs/troubleshooting.md) · [Diagnostic codes](docs/diagnostics-codes.md) · [Assessment and roadmap](docs/assessment.md) · [ADRs](docs/adr/)

## Repository layout

```
src/        Core, Components, Hosting, WebAssembly, Hybrid, Maui, Maui.Essentials, Testing, Testing.Bunit, Cli, Templates
demo/       BlazorWorkbench.Demo.Components (library), BlazorWorkbench.Demo (Server host, scenarios, fake backends),
            BlazorWorkbench.Demo.Wasm + .Client (WebAssembly host reusing the same scenarios),
            BlazorWorkbench.Demo.Maui (.NET MAUI Blazor Hybrid app reusing the same scenarios),
            BlazorWorkbench.Demo.Components.Tests (bUnit tests that reuse the scenarios); see demo/README.md
tests/      Unit (Core, Components/bUnit, Hybrid, Testing, Cli) and end-to-end browser tests (E2E)
docs/       Guides, reference and architecture decision records
```

To build and test the repository (the MAUI projects need Windows and `dotnet workload install maui-windows`; elsewhere use `BlazorWorkbench.Web.slnf`):

```bash
dotnet build BlazorWorkbench.slnx
dotnet run --project src/BlazorWorkbench.Cli -- install-browsers                       # once
dotnet test BlazorWorkbench.slnx
dotnet run --project src/BlazorWorkbench.Cli -- test --a11y --visual                  # demo scenarios
dotnet run --project src/BlazorWorkbench.Cli -- test --a11y -p demo/BlazorWorkbench.Demo.Maui/BlazorWorkbench.Demo.Maui.csproj   # in the MAUI app
```

## Status and limitations

This is version 0.1.0 for .NET 10. Previews run with **Interactive Server**, **Interactive WebAssembly** or in a **.NET MAUI Blazor Hybrid** app (automated hybrid runs need Windows and WebView2); see [compatibility](docs/compatibility.md). Automated accessibility checks find only part of the WCAG issues, so manual review is still needed. Visual baselines are specific to each platform and hosting model. All limitations are listed in [docs/compatibility.md](docs/compatibility.md#known-limitations).

## License

MIT. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
