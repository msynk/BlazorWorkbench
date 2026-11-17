# bUnit integration

`BlazorWorkbench.Testing.Bunit` renders scenarios in [bUnit](https://bunit.dev) tests. These tests are fast and run without a browser. Scenario services, the fake HTTP backend, simulated users, the clock, cascading values and fixtures are installed in the test context exactly as in the preview, through the same `ScenarioHost` component.

```bash
dotnet add package BlazorWorkbench.Testing.Bunit
```

## Rendering a scenario

```csharp
public class ButtonTests : BunitContext
{
    [Fact]
    public void Primary_raises_OnClick()
    {
        var cut = this.RenderScenario<ButtonScenarios>("primary");   // key or name

        cut.Find("button").Click();

        Assert.Equal(1, cut.CallbackCount("OnClick"));
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void Loading_then_loaded()
    {
        var cut = this.RenderScenario<WeatherScenarios>("loading");
        cut.WaitForAssertion(() => Assert.Contains("Loading", cut.Markup));

        cut.Session().Gates.Release("weather");
        cut.WaitForAssertion(() => Assert.Contains("31.5", cut.Find("[data-testid=temperature]").TextContent));
    }
}
```

| API | Purpose |
| --- | --- |
| `RenderScenario<TSet>(key, args?)` | Renders a scenario from a set (`args` applies inspector-style overrides). |
| `RenderScenario(id, params Assembly[])` | Renders a scenario by id from the catalog of the given assemblies. |
| `RenderScenario(catalog, scenario, args?)` | Lowest-level overload. |
| `cut.Session()` | The `ScenarioSession`: diagnostics, callbacks, gates, `Http.Exchanges`, clock. |
| `cut.WaitForScenarioIdle()` | Waits until tracked work (fake HTTP) is done. |
| `cut.AssertNoScenarioErrors()` | Fails on error diagnostics or an error boundary. |
| `cut.CallbackCount(name)` | Calls of an instrumented `EventCallback`. |
| `WorkbenchBunit.ScenarioIds(params Assembly[])` | Data source for `[MemberData]` or `[TestCaseSource]`. Excludes `SkipTests` scenarios. |

## Smoke-testing every scenario

```csharp
public class AllScenarios : BunitContext
{
    private static readonly Assembly[] Assemblies = [typeof(ButtonScenarios).Assembly, typeof(Button).Assembly];
    public static IEnumerable<object[]> Ids => WorkbenchBunit.ScenarioIds(Assemblies);

    public AllScenarios() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Theory, MemberData(nameof(Ids))]
    public void Renders_without_errors(string id)
    {
        var cut = this.RenderScenario(id, Assemblies);
        cut.WaitForScenarioIdle();
        cut.AssertNoScenarioErrors();
    }
}
```

Assemblies that declare scenario sets or providers are scanned only for scenarios, like a host's entry assembly. The other assemblies are scanned for components.

## Markup snapshots

`MatchMarkupSnapshot()` compares a scenario's rendered HTML with a committed `{scenario-id}.verified.html` file, like Verify
or Jest snapshots. It needs no browser and gives the same result on every operating system, so it is a cheap regression
check for every CI job, next to the screenshots of `workbench test --visual` (which catch CSS changes that markup cannot show).

```csharp
[Theory, MemberData(nameof(Ids))]
public void Markup_is_unchanged(string id)
{
    var cut = this.RenderScenario(id, Assemblies);
    cut.WaitForScenarioIdle();
    cut.MatchMarkupSnapshot();          // __snapshots__/{id}.verified.html next to this test file
}
```

The markup is the inside of the preview root, pretty-printed, without Blazor's internal `blazor:` attributes. A missing or
different snapshot fails the test and writes `{id}.received.html` next to it, with the first different line in the message.
Review it and rename it to `.verified.html` to accept it, or run the tests with `WORKBENCH_UPDATE_SNAPSHOTS=1` to accept
every change at once. Use fixed data, a fixed clock and a fixed culture so snapshots stay stable.

## A complete example

[demo/BlazorWorkbench.Demo.Components.Tests](../demo/BlazorWorkbench.Demo.Components.Tests) tests the demo library this way: a smoke test for every scenario, plus focused tests that submit a checkout against the scenario's fake backend, release a gate, evaluate authorization policies for each simulated user, and fast-forward a session timeout with the scenario clock:

```csharp
var cut = this.RenderScenario<SessionTimeoutScenarios>("active");
cut.Session().Clock!.Advance(TimeSpan.FromMinutes(8));
cut.WaitForAssertion(() => Assert.Equal("2:00", cut.Find("[data-testid=countdown]").TextContent));
```

Services a component needs in every app, such as authorization policies, are registered in the test context the same way the host registers them (`Services.AddDemoComponents()` in the demo).

## Notes

- Use one `BunitContext` per scenario. A context hosts one session.
- Workbench replaces bUnit's placeholder `HttpClient` and authorization services with the scenario simulations. Do not call `AddAuthorization()` in the same context.
- JavaScript interop: calls the scenario plans with `.JSInterop(...)` are answered by Workbench, exactly as in the browser; the others go to bUnit's `JSInterop` and follow its mode. Set `JSInterop.Mode = JSRuntimeMode.Loose` for components that make other calls, or set them up. `cut.JSCallCount(identifier)` counts the component's calls, planned or not.
- Culture: the host sets `CurrentCulture` while rendering. Tests that assert culture-specific output should set the culture explicitly, for example to invariant, before rendering scenarios without `.Culture(...)`.
- bUnit does not run CSS or a layout engine. Use the Playwright runner for focus, visibility, layout and visual checks.
