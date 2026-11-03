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

## Notes

- Use one `BunitContext` per scenario. A context hosts one session.
- Workbench replaces bUnit's placeholder `HttpClient` and authorization services with the scenario simulations. Do not call `AddAuthorization()` in the same context.
- JavaScript interop follows bUnit's mode. Set `JSInterop.Mode = JSRuntimeMode.Loose` for components that call JavaScript, or set up the specific calls.
- Culture: the host sets `CurrentCulture` while rendering. Tests that assert culture-specific output should set the culture explicitly, for example to invariant, before rendering scenarios without `.Culture(...)`.
- bUnit does not run CSS or a layout engine. Use the Playwright runner for focus, visibility, layout and visual checks.
