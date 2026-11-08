# Blazor Workbench demo

The demo is a small component library for a fictional outdoor store, **Trailhead**, plus the Workbench hosts that
preview and test it. It is built to show what Workbench does that a demo page or a Storybook-style gallery does not:
reach the states that are hard to reproduce in a running app, and turn every one of them into a test.

| Project | What it is |
| --- | --- |
| [BlazorWorkbench.Demo.Components](BlazorWorkbench.Demo.Components) | The component library: 32 components, from a `Button` to a checkout form and a sales dashboard. It knows nothing about Workbench. |
| [BlazorWorkbench.Demo](BlazorWorkbench.Demo) | The Interactive Server host. It declares every scenario ([Scenarios](BlazorWorkbench.Demo/Scenarios)), the fake backends ([Fakes](BlazorWorkbench.Demo/Fakes)), a custom editor and a scenario provider. |
| [BlazorWorkbench.Demo.Wasm](BlazorWorkbench.Demo.Wasm) + [.Client](BlazorWorkbench.Demo.Wasm.Client) | The same scenarios with Interactive WebAssembly. |
| [BlazorWorkbench.Demo.Maui](BlazorWorkbench.Demo.Maui) | The same scenarios in a .NET MAUI Blazor Hybrid app, inside the platform WebView. |
| [BlazorWorkbench.Demo.Components.Tests](BlazorWorkbench.Demo.Components.Tests) | bUnit tests that reuse the scenarios: a smoke test per scenario and focused tests, without a browser. |

The three hosts compile the same scenario files and call the same [DemoCatalog.Configure](BlazorWorkbench.Demo/DemoCatalog.cs),
so one definition runs in every hosting model.

## Run it

```bash
# Interactive Server: http://localhost:5280
dotnet run --project demo/BlazorWorkbench.Demo
# Interactive WebAssembly: http://localhost:5281
dotnet run --project demo/BlazorWorkbench.Demo.Wasm
# .NET MAUI Blazor Hybrid (Windows, needs the maui-windows workload)
dotnet run --project demo/BlazorWorkbench.Demo.Maui -f net10.0-windows10.0.19041.0

# bUnit: every scenario plus focused tests, no browser
dotnet test demo/BlazorWorkbench.Demo.Components.Tests

# Render checks, interactions, axe-core and screenshots in Chromium (from the repository root)
dotnet run --project src/BlazorWorkbench.Cli -- install-browsers
dotnet run --project src/BlazorWorkbench.Cli -- test --a11y --visual
```

## A ten-minute tour

Start the Interactive Server host and follow these stops. Each link opens the scenario in the Workbench UI.

1. **A payment API that declines on demand.**
   [Checkout · Card declined](http://localhost:5280/scenario/commerce-checkout--card-declined): click **Place order**.
   Every answer the payment service can give is a scenario: `402`, `409`, `422` from a custom handler that reads the
   posted JSON, a timeout, a connection that fails once and then recovers. Requests never leave the process; open
   **Diagnostics** to see each one with its matched rule.

2. **Freeze a loading state, then release it.**
   [Checkout · Placing order (gated)](http://localhost:5280/scenario/commerce-checkout--placing-order-gated): click
   **Place order**, look at the spinner for as long as you like, then open **Environment** and release the `payment`
   gate. Tests use the same gate (`.ReleaseGate("payment")`), so loading states are tested and screenshotted without sleeps.

3. **Fifteen minutes in one click.**
   [Session timeout · Active](http://localhost:5280/scenario/account-session-timeout--active): in **Environment**,
   advance the clock by one minute at a time until the warning appears, then by one second to watch the countdown.
   The component uses `TimeProvider` and its timers; the scenario freezes the clock with `.Clock(...)`. The
   [flash sale banner](http://localhost:5280/scenario/commerce-flash-sale-banner--last-minute) works the same way.

4. **Six users, no test accounts.**
   [Workspace settings](http://localhost:5280/docs/account-workspace-settings) renders one component for a viewer, an
   editor, an admin, an owner on the Pro plan, a billing contact (a claim, not a role) and a signed-out visitor. The
   component's `AuthorizeView Policy="..."` blocks evaluate the real policies the library registers.

5. **A page whose services fail one at a time.**
   [Sales dashboard · Partial outage](http://localhost:5280/scenario/analytics-sales-dashboard--partial-outage): the
   orders endpoint answers 500 and only that widget shows an error. Try *Loading (gated)*, *Slow network* (1.2 s
   latency, and the preview still turns ready only when the data arrived) and *Phone*. Change **Period**: the request
   goes to `?range=6m`, which a more specific fake rule answers.

6. **A fake backend you can explore.**
   [Product search · Start typing](http://localhost:5280/scenario/commerce-product-search--start-typing): type
   `jacket` and sort by price. A custom handler filters a fixed catalog, so the component behaves as against a real API.

7. **Breakpoints, phones and right-to-left.**
   [App header · Phone](http://localhost:5280/scenario/layout-app-header--phone): open the menu. Switch the toolbar's
   viewport, theme (including the custom `high-contrast` theme) and culture (`ar-SA` mirrors the layout) on any scenario.
   Click a navigation link: the preview stays put and **Diagnostics** records the intercepted navigation.

8. **Interaction tests as living documentation.**
   [Onboarding wizard · Start](http://localhost:5280/scenario/forms-onboarding-wizard--start): open **Interactions** and
   run *completes every step*. It fills, selects, checks, asserts focus moves to each step's heading, and records a
   visual checkpoint per step in `--visual` runs.

9. **Screenshots that never flake.**
   [Revenue chart · Seeded data](http://localhost:5280/scenario/analytics-revenue-chart--seeded-data) draws random data
   from the scenario's seeded `Random`: different per seed, identical on every run. *Light and dark* captures it in
   both themes. Approve a `--visual` run with `workbench approve`, then open **Visual** to compare with the baseline.

10. **One definition, every test.** Run the commands under [Run it](#run-it). The same 144 scenarios become render
    checks, 76 interaction tests, axe-core scans and visual comparisons in Chromium, in WebAssembly and in the MAUI
    WebView, and bUnit smoke tests in [BlazorWorkbench.Demo.Components.Tests](BlazorWorkbench.Demo.Components.Tests).

## Where each feature is shown

| Feature | Where to look |
| --- | --- |
| Fake HTTP: status codes, problem details, latency, timeouts, offline, `Times(n)` retries | [CheckoutScenarios](BlazorWorkbench.Demo/Scenarios/CommerceScenarios.cs), [WeatherScenarios](BlazorWorkbench.Demo/Scenarios/DataScenarios.cs) |
| Fake HTTP: custom handlers (`.Handle(...)`) that read the query string or the body | [FakeBackends](BlazorWorkbench.Demo/Fakes/FakeBackends.cs), used by product search and checkout |
| Fake HTTP: query matching, shared rules in `set.Setup(...)`, `IHttpClientFactory` | [SalesDashboardScenarios](BlazorWorkbench.Demo/Scenarios/AnalyticsScenarios.cs), [ProductSearch](BlazorWorkbench.Demo.Components/Commerce/ProductSearch.razor) |
| Gates on HTTP rules and in fake services | Checkout, product search and dashboard scenarios; [FakeProductService](BlazorWorkbench.Demo/Fakes/FakeProductService.cs) |
| Fake clock and `TimeProvider` timers | [SessionTimeout](BlazorWorkbench.Demo.Components/Account/SessionTimeout.razor), [FlashSaleBanner](BlazorWorkbench.Demo.Components/Commerce/FlashSaleBanner.razor), `DateLabel`, the dashboard greeting |
| Simulated users: roles, claims, named policies | [WorkspaceSettingsScenarios](BlazorWorkbench.Demo/Scenarios/AccountScenarios.cs), [WorkspacePolicies](BlazorWorkbench.Demo.Components/Account/WorkspacePolicies.cs), `UserGreeting` |
| Service overrides: `.Service<T>(factory)`, `.Service<T, TImpl>()`, `.Services(...)` | [AppHeaderScenarios](BlazorWorkbench.Demo/Scenarios/AppShellScenarios.cs), [ProductListScenarios](BlazorWorkbench.Demo/Scenarios/DataScenarios.cs) |
| Cascading values (`.Cascade(...)`, validated by `WB1004`) | [PriceTagScenarios](BlazorWorkbench.Demo/Scenarios/CommerceScenarios.cs) |
| Culture, calendars, currencies and right-to-left | Price tag, checkout, revenue chart (`fa-IR`), app header (`ar-SA`), `DateLabel`, `Toolbar` |
| Viewports, mobile emulation, themes, a custom theme | App header, dashboard *Phone* and *Evening, dark theme*, [DemoCatalog](BlazorWorkbench.Demo/DemoCatalog.cs) |
| Seeded data and fixtures | Revenue chart (`.Seed(7)`), [PeopleTableScenarios](BlazorWorkbench.Demo/Scenarios/DataScenarios.cs), checkout cart fixtures |
| Interaction tests: forms, keyboard, focus, drag and drop, upload, checkpoints | Onboarding wizard, checkout, `SortableList`, `AvatarUpload`, `Dialog`, `Counter` |
| Device simulation (.NET MAUI Essentials) | [DeviceScenarios](BlazorWorkbench.Demo/Scenarios/DeviceScenarios.cs) |
| Diagnostics: exceptions, failed requests, intercepted navigation | `FlakyWidget`, the app header's links, any scenario's **Diagnostics** panel |
| Performance budgets | `Button · Primary`, `Sales dashboard · Loaded` |
| Custom parameter editor and scenario provider | [HexColorEditor](BlazorWorkbench.Demo/Editors/HexColorEditor.razor), [ToneMatrixProvider](BlazorWorkbench.Demo/Providers/ToneMatrixProvider.cs) |
| bUnit: smoke test every scenario, gates, fake clock, policies | [BlazorWorkbench.Demo.Components.Tests](BlazorWorkbench.Demo.Components.Tests) |

## What Workbench found in this library

Running the scenarios found real defects while the demo was written, which is the point of the exercise:

- `AppHeader` labeled the cart "Cart, 128 items" while its badge showed "99+". axe-core reported
  `label-content-name-mismatch` (WCAG 2.5.3) in the *Busy cart* scenario; the accessible name now contains the badge text.
- `ProductList` gave a sold-out button a name that did not contain its visible text (the same rule).
- `Counter` and `SortableList` discarded the user's changes when the parent re-rendered.

The richer scenarios also exposed a race in Workbench itself: with several requests finishing at once, the preview could
keep a stale pending count and wait for readiness until a timeout. The dashboard scenarios now cover it in every run.

## Add your own

Copy a scenario set next to the existing ones and run `dotnet watch --project demo/BlazorWorkbench.Demo`: the catalog
reloads on save. `workbench validate` checks parameters, services, cultures, viewports and interaction steps without
rendering, and `workbench approve` turns reviewed screenshots into baselines. The guides in [docs](../docs) explain each
API used here.
