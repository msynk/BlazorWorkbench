# Getting started

This guide adds Blazor Workbench to an existing Razor component library. At the end you can preview, edit, test and publish the library's components.

## Prerequisites

- .NET 10 SDK (10.0.100 or later).
- A Razor class library (or a Blazor app) that contains the components.
- For automated tests: Chromium installed through Playwright (`workbench install-browsers`).

## 1. Create a Workbench host

The host is a small ASP.NET Core project that exists only for development. It is not deployed with your product.

The quickest way to create one is the template. It includes an example component, scenarios and `workbench.json`:

```bash
dotnet new install BlazorWorkbench.Templates
dotnet new blazorworkbench -n MyComponents.Workbench
dotnet add MyComponents.Workbench reference ../MyComponents/MyComponents.csproj
```

You can also create the host by hand:

```bash
dotnet new web -n MyComponents.Workbench
cd MyComponents.Workbench
dotnet add package BlazorWorkbench.Hosting
dotnet add reference ../MyComponents/MyComponents.csproj
```

Replace `Program.cs`:

```csharp
using BlazorWorkbench.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Register services your components need (real or development implementations) here.
// AddBlazorWorkbench must run after them so scenarios can override them.
builder.AddBlazorWorkbench(options =>
{
    options.Title = "My Components";
    options.AddAssemblyOf<MyComponents.Button>();                   // scanned for components
    options.PreviewStylesheets.Add("_content/MyComponents/theme.css"); // global CSS your components expect
});

var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
```

Start it:

```bash
dotnet watch
```

Open the printed URL, for example `http://localhost:5000`. The explorer lists every public component in the assembly. A component without scenarios gets a generated **Default** scenario, so you can preview and edit it right away.

> The host refuses to start outside the `Development` environment and accepts loopback connections only. See [security](security.md).

## 2. Write scenarios

A *scenario* is a named, reproducible state of a component. You declare scenarios in C# in the host project (or in any assembly the host references):

```csharp
using BlazorWorkbench;
using MyComponents;

public sealed class AlertScenarios : ScenarioSet<Alert>
{
    protected override void Configure(ScenarioSetBuilder<Alert> set)
    {
        set.Title("Feedback/Alert").Tags("feedback");

        set.Add("Info", s => s.Parameter(a => a.Message, "A new version is available."));

        set.Add("Error dismissible", s => s
            .Parameter(a => a.Severity, AlertSeverity.Error)
            .Parameter(a => a.Message, "Payment failed.")
            .Parameter(a => a.Dismissible, true)
            .Interaction("dismiss removes the alert", i => i
                .Click(By.Role("button", "Dismiss"))
                .ExpectHidden(By.Role("alert"))
                .ExpectCallback("OnDismiss", times: 1)));
    }
}
```

Scenario ids are stable and derived from the title and name. For example, `feedback-alert--error-dismissible` is used in URLs, reports and baselines. Read [scenario authoring](scenarios.md) for parameters, fixtures, templates, services, HTTP, users, culture and more.

## 3. Use the Workbench UI

| Area | What it does |
| --- | --- |
| Overview | The start page: your `options.Introduction` (Markdown, for example a guided tour), catalog statistics and catalog issues. |
| Explorer | Groups components by title path. Press <kbd>/</kbd> to search names, tags, parameters and scenarios, then <kbd>Enter</kbd> to open the first result. |
| Scenario header | The description, then chips that summarize what the scenario **simulates** (HTTP rules, user, clock, services, device, cascading values, culture, viewport, seed) and what it **verifies** (interaction tests, screenshots, accessibility rules, budgets). A chip opens the panel behind it. Documentation pages show the same chips for every scenario. |
| Toolbar | Viewport presets, theme, culture and direction. **Reload** (<kbd>R</kbd>) keeps your edits; **Reset** (<kbd>Shift</kbd>+<kbd>R</kbd>) restores the declared scenario. |
| Controls | Edits parameters live. Shows null, unset and fixture values separately. Invalid input is rejected with a reason. |
| Environment | Simulated user, clock (with *advance* buttons), HTTP rules and exchanges, service sources and **gates** to release or fail. |
| Actions | Calls of `EventCallback` parameters that the scenario does not handle. |
| Diagnostics | Exceptions, console errors, failed resources and requests, intercepted navigation and focus changes, all redacted. |
| Interactions | Runs the scenario's interaction tests in the browser (best effort; the CLI is authoritative). |
| Accessibility | Runs axe-core on the preview. |
| Visual | Baseline, current and difference images from the last `workbench test --visual` run. |
| Code | Razor usage, the current controls exported as C# scenario code, and the arguments as JSON. |

Every state is in the URL, so you can share a deep link. `?panel=environment` (or `controls`, `actions`, `diagnostics`, `interactions`, `accessibility`, `visual`, `code`) opens a panel, which is how the demo's tour links straight to the part of the UI that matters.

To greet people on the overview page, set an introduction. It is rendered with the same safe Markdown subset as documentation, and relative links such as `scenario/{id}?panel=...` and `docs/{id}` open scenarios and documentation pages:

```csharp
options.Introduction = """
    **Contoso UI** is our design system. Start with the [button](docs/inputs-button) or try a
    [declined payment](scenario/commerce-checkout--card-declined?panel=diagnostics).
    """;
```

## 4. Automate

Create `workbench.json` at the repository root:

```bash
dotnet tool install -g BlazorWorkbench.Cli
workbench init
workbench install-browsers
```

Then run:

```bash
workbench validate            # static checks, no browser
workbench test                # render health + interaction tests
workbench test --a11y         # + axe-core scans
workbench test --visual       # + screenshot comparison (first run reports missing baselines)
workbench approve             # review, then approve screenshots as baselines
```

Commit `workbench.json` and the baseline directory. Ignore the output directory (`artifacts/workbench` by default). For a pipeline setup, see [CI](ci.md).

## 5. Unit-test scenarios with bUnit (optional)

```csharp
public class ScenarioSmokeTests : BunitContext
{
    public static IEnumerable<object[]> All => WorkbenchBunit.ScenarioIds(typeof(AlertScenarios).Assembly, typeof(Alert).Assembly);

    [Theory, MemberData(nameof(All))]
    public void Renders(string id) => this.RenderScenario(id, typeof(AlertScenarios).Assembly, typeof(Alert).Assembly).AssertNoScenarioErrors();
}
```

See [bUnit integration](bunit.md).

## Next steps

- [Environment simulation](environment.md): fake HTTP, gates, users, clock and culture.
- [WebAssembly hosting](webassembly.md), if your components depend on the WebAssembly runtime.
- [.NET MAUI Blazor Hybrid](hybrid.md), to run scenarios in a MAUI app and simulate device APIs.
- [Visual regression](visual-regression.md) and [accessibility](accessibility.md).
- [Publish a static catalog](publishing.md) for your team.
- [Connect a coding agent over MCP](mcp.md).
