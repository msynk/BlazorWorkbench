---
name: blazor-workbench
description: Write and check Blazor Workbench scenarios for Razor components - ScenarioSet<T> definitions with parameters, fake HTTP, users, clock, JS interop, decorators and interaction tests - and verify them with the workbench CLI or its MCP tools. Use when adding or changing a Razor component in a repository that has a Blazor Workbench host (a project referencing BlazorWorkbench.Hosting, or a workbench.json).
---

# Blazor Workbench scenarios

A scenario is a named, reproducible state of one Razor component, written in C# in the Workbench host project. One
scenario is at the same time an interactive preview, a render check, an accessibility scan, a screenshot and the place
for interaction tests. Write one scenario per meaningful state, not one per parameter.

## Where scenarios live

- Find the host: the project that references `BlazorWorkbench.Hosting` (or `workbench.json` → `"project"`).
- Scenario sets are public classes deriving from `ScenarioSet<TComponent>`, usually in a `Scenarios` folder there.
- Ids are `{component-id}--{scenario-key}`; keys come from the names. Do not rename scenarios casually: ids are used in
  URLs and screenshot baselines.

```csharp
public sealed class ButtonScenarios : ScenarioSet<Button>
{
    protected override void Configure(ScenarioSetBuilder<Button> set)
    {
        set.Title("Inputs/Button").Description("The primary action of a view.");
        set.Add("Primary", s => s
            .ChildContent("Save changes")
            .Interaction("click raises OnClick", i => i
                .Click(By.Role("button", "Save changes"))
                .ExpectCallback("OnClick", times: 1)));
        set.Add("Disabled", s => s.ChildContent("Save").Parameter(b => b.Disabled, true));
        set.Matrix("Variants", m => m.Vary(b => b.Variant).Vary(b => b.Size).Pairwise(), s => s.ChildContent("Save"));
    }
}
```

## The environment, per scenario

Never call real services or the network from a scenario. Use the simulations:

| Need | API |
| --- | --- |
| Backend responses, errors, latency, offline | `.Http(h => h.Get("/api/orders").RespondJson(data).Delay(...))`, `.Fail(HttpStatusCode.InternalServerError)`, `.Timeout()`, `.NetworkError()`, `.Offline()` |
| A loading state that stays put | `.Http(h => h.Get("/api/x").Hold())` (released from the Environment panel or `.ReleaseGate(...)` in a test) |
| Services | `.Service<IOrders>(ctx => new FakeOrders())` |
| Signed-in user, roles, claims | `.User("Ada", "Admin")`, `.User(u => u.Claim("plan", "pro"))`, `.Anonymous()` |
| Time | `.Clock(new DateTimeOffset(...))` (never `DateTime.Now` in components; inject `TimeProvider`) |
| Culture and direction | `.Culture("fa-IR")`, `.Direction(TextDirection.Rtl)` |
| JavaScript interop | `.JSInterop(js => js.Setup("localStorage.getItem").Returns("x"))` |
| Browser storage | `.LocalStorage(key, value)` |
| Parent markup (a form, a provider, a narrow column) | `.Wrap<CascadingAuthenticationState>()`, `.WrapStyle("width: 320px")` |
| Cascading values | `.Cascade(new ThemeInfo("dark"))` |
| Toolbar globals of the host | `.Global("density", "compact")` |

Cover the states users meet: empty, loading, error, offline, long text, many items, signed out, right-to-left,
narrow widths. Prefer fixed data and a fixed clock so screenshots are stable.

## Interaction tests

Use accessible locators: `By.Role("button", "Save")`, `By.Label("Email")`, `By.Text(...)`, `By.TestId(...)`. Steps:
`Click`, `Fill`, `Select`, `Check`, `Press("Tab")`, `ExpectVisible`, `ExpectText`, `ExpectValue`, `ExpectFocused`,
`ExpectCallback(name, times)`, `ExpectJSCall(identifier, times)`, `ExpectNoErrors()`.

## Check your work

Prefer the MCP tools when they are available (server `workbench mcp`), otherwise the CLI:

1. `validate_scenarios` / `workbench validate` - fix every error (codes `WBxxxx` are explained in docs/diagnostics-codes.md).
2. `run_tests(filter: "component-id-*", accessibility: true)` / `workbench test -f "component-id-*" --a11y`.
3. `screenshot_scenario(id)` to look at the result; `get_scenario_url(id)` to give a person a link.
4. `list_changed_scenarios` after editing a shared component, and run those scenarios too.
5. Never approve visual baselines or accessibility baselines yourself (`workbench approve`): a person reviews them.

Accessibility findings at `serious` or `critical` impact fail the run; fix the component rather than disabling rules.
If a rule must be disabled, use `.Accessibility(a => a.DisableRule("id", "justification"))` with a real reason.
