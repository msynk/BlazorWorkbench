# Blazor Workbench demo

**Trailhead UI** is the component library of a fictional outdoor store. The demo shows it in Blazor Workbench, which is
built to do what a demo page or a Storybook-style gallery does not: reach the states that are hard to reproduce in a
running app (a declined payment, a session about to expire, one failing endpoint, a phone without location access), and
turn every one of them into a test.

## Run it and take the tour

```bash
dotnet run --project demo/BlazorWorkbench.Demo     # http://localhost:5280
```

The overview page is a guided tour: sixteen stops, each one a link that opens a scenario with the relevant panel already
open. On any scenario page, the chips under the title list what the scenario **simulates** (fake HTTP rules, the user,
the clock, replaced services, the device, culture, viewport) and what it **verifies** (interaction tests, screenshots,
accessibility rules, budgets). Click a chip to open the panel behind it.

| Tour stop | What it shows | Code |
| --- | --- | --- |
| [A payment API that declines](http://localhost:5280/scenario/commerce-checkout--card-declined?panel=diagnostics) | Fake HTTP: `402`, `409`, `422` from a handler that reads the posted JSON, a timeout, a network error that clears on retry | [CheckoutScenarios](BlazorWorkbench.Demo/Scenarios/CommerceScenarios.cs) |
| [A spinner you can hold](http://localhost:5280/scenario/commerce-checkout--placing-order-gated?panel=environment) | Gates: a request waits until you, or a test step, release it | `.Hold("payment")`, `.ReleaseGate("payment")` |
| [Fifteen minutes in one click](http://localhost:5280/scenario/account-session-timeout--active?panel=environment) | Fake clock: `TimeProvider` and its timers advance on demand | [SessionTimeout](BlazorWorkbench.Demo.Components/Account/SessionTimeout.razor) |
| [Six users, no test accounts](http://localhost:5280/docs/account-workspace-settings) | Simulated users, roles and claims against the library's real authorization policies | [WorkspaceSettingsScenarios](BlazorWorkbench.Demo/Scenarios/AccountScenarios.cs) |
| [A partial outage](http://localhost:5280/scenario/analytics-sales-dashboard--partial-outage?panel=environment) | One endpoint of a page fails; also gated, slow and offline variants | [SalesDashboardScenarios](BlazorWorkbench.Demo/Scenarios/AnalyticsScenarios.cs) |
| [A fake API you can explore](http://localhost:5280/scenario/commerce-product-search--start-typing?panel=diagnostics) | A custom handler that filters and sorts a fixed catalog | [FakeBackends](BlazorWorkbench.Demo/Fakes/FakeBackends.cs) |
| [Services swapped per scenario](http://localhost:5280/scenario/layout-app-header--rewards-enabled?panel=environment) | `.Service<T>(...)`, `.Service<T, TImpl>()` and `.Services(...)` | [AppHeaderScenarios](BlazorWorkbench.Demo/Scenarios/AppShellScenarios.cs) |
| [Phones, themes and right to left](http://localhost:5280/scenario/layout-app-header--phone) | Viewports with phone emulation, a custom `high-contrast` theme, `ar-SA` | [DemoCatalog](BlazorWorkbench.Demo/DemoCatalog.cs) |
| [Four stores, one price tag](http://localhost:5280/docs/commerce-price-tag) | Cascading values (validated by `WB1004`) and cultures | [PriceTagScenarios](BlazorWorkbench.Demo/Scenarios/CommerceScenarios.cs) |
| [A phone in the browser](http://localhost:5280/scenario/device-location-card--permission-denied?panel=environment) | Simulated .NET MAUI Essentials on any host | [DeviceScenarios](BlazorWorkbench.Demo/Scenarios/DeviceScenarios.cs) |
| [Interaction tests](http://localhost:5280/scenario/forms-onboarding-wizard--start?panel=interactions) | Fill, select, check, keyboard, focus assertions, visual checkpoints | [OnboardingWizardScenarios](BlazorWorkbench.Demo/Scenarios/AppShellScenarios.cs) |
| [Accessibility scans](http://localhost:5280/scenario/display-card--dont-icon-actions-without-names?panel=accessibility) | axe-core on a deliberate do/don't pair; the "don't" is skipped in CI with a reason | [CardScenarios](BlazorWorkbench.Demo/Scenarios/DisplayScenarios.cs) |
| [Live controls and code](http://localhost:5280/scenario/display-badge--custom-accent?panel=controls) | Live parameters in the URL, a custom editor, export to Razor and C# | [HexColorEditor](BlazorWorkbench.Demo/Editors/HexColorEditor.razor) |
| [Errors with context](http://localhost:5280/scenario/feedback-flaky-widget--throws-while-rendering?panel=diagnostics) | Exceptions captured with the scenario id instead of a blank page | [FlakyWidget](BlazorWorkbench.Demo.Components/Feedback/FlakyWidget.razor) |
| [Screenshots that never flake](http://localhost:5280/scenario/analytics-revenue-chart--seeded-data?panel=visual) | Seeded random data, screenshots in both themes | [RevenueChartScenarios](BlazorWorkbench.Demo/Scenarios/AnalyticsScenarios.cs) |
| [Documentation for free](http://localhost:5280/docs/commerce-checkout) | Generated pages: parameters, dependencies, live previews, snippets | any component's **Docs** link |

The tour itself is Markdown in [DemoTour.cs](BlazorWorkbench.Demo/DemoTour.cs), set with `options.Introduction`; a test in
`tests/BlazorWorkbench.Components.Tests` fails if one of its links stops resolving.

## The projects

| Project | What it is |
| --- | --- |
| [BlazorWorkbench.Demo.Components](BlazorWorkbench.Demo.Components) | The component library: 32 components, from a `Button` to a checkout form and a sales dashboard. It knows nothing about Workbench. |
| [BlazorWorkbench.Demo](BlazorWorkbench.Demo) | The Interactive Server host. It declares every scenario ([Scenarios](BlazorWorkbench.Demo/Scenarios)), the fake backends ([Fakes](BlazorWorkbench.Demo/Fakes)), a custom editor, a scenario provider and the tour. |
| [BlazorWorkbench.Demo.Wasm](BlazorWorkbench.Demo.Wasm) + [.Client](BlazorWorkbench.Demo.Wasm.Client) | The same scenarios with Interactive WebAssembly. |
| [BlazorWorkbench.Demo.Maui](BlazorWorkbench.Demo.Maui) | The same scenarios in a .NET MAUI Blazor Hybrid app, inside the platform WebView. |
| [BlazorWorkbench.Demo.Components.Tests](BlazorWorkbench.Demo.Components.Tests) | bUnit tests that reuse the scenarios: a smoke test per scenario and focused tests, without a browser. |

The three hosts compile the same scenario files and call the same [DemoCatalog.Configure](BlazorWorkbench.Demo/DemoCatalog.cs),
so one definition runs in every hosting model.

```bash
# Interactive WebAssembly: http://localhost:5281
dotnet run --project demo/BlazorWorkbench.Demo.Wasm
# .NET MAUI Blazor Hybrid (Windows, needs the maui-windows workload)
dotnet run --project demo/BlazorWorkbench.Demo.Maui -f net10.0-windows10.0.19041.0
```

## One definition, every test

The 146 scenarios are also the test suite: render checks, 77 interaction tests, axe-core scans and visual comparisons
in Chromium, in WebAssembly and in the MAUI WebView, plus a bUnit smoke test per scenario. From the repository root:

```bash
dotnet test demo/BlazorWorkbench.Demo.Components.Tests                  # bUnit: every scenario plus focused tests, no browser
dotnet run --project src/BlazorWorkbench.Cli -- install-browsers        # once
dotnet run --project src/BlazorWorkbench.Cli -- test --a11y --visual    # render, interactions, axe-core, screenshots
dotnet run --project src/BlazorWorkbench.Cli -- approve                 # accept reviewed screenshots as baselines
```

The first `--visual` run reports every screenshot as missing; approve them, run again, and the **Visual** panel shows
baseline, current and difference images.

## Where each feature is shown

| Feature | Where to look |
| --- | --- |
| Guided tour on the overview page (`options.Introduction`) | [DemoTour](BlazorWorkbench.Demo/DemoTour.cs), [DemoCatalog](BlazorWorkbench.Demo/DemoCatalog.cs) |
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
| Accessibility: a do/don't pair, a scan skipped with a reason | [CardScenarios](BlazorWorkbench.Demo/Scenarios/DisplayScenarios.cs) |
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
