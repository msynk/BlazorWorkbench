# Changelog

All notable changes are documented here. The project follows [Semantic Versioning](https://semver.org/).

## Unreleased

### Added
- **Canvas toolbar**: zoom (25–300%, Alt+=/Alt+-/Alt+0), viewport rotation and custom sizes (`?viewport=390x844`), backgrounds (`options.Backgrounds`), a layout grid (`options.GridSize`), outlines, a measure tool (size, padding, margin), DOM-update flashing, focus mode (Alt+F) and the panel on the right (Alt+D). Tool state is in the URL; tools never reload the preview and are ignored by readiness, locators, scans and screenshots. See [docs/tools.md](docs/tools.md).
- **Accessibility simulations**: color vision deficiencies (protan-, deuteran-, tritan- opia and anomaly, achromatopsia, achromatomaly), blurred vision and reduced contrast; text at 125–200%; WCAG 1.4.12 text spacing; a numbered tab-order overlay; **Highlight** for each axe finding.
- **Forced pseudo-states**: the toolbar forces `:hover`, `:focus`, `:focus-visible`, `:focus-within` and `:active` on every element; scenarios force them on chosen elements with `.ForceState(PseudoState.FocusVisible, ".btn")`, which screenshots and scans then cover.
- **Compare and Gallery views**: the scenario across themes, viewports, cultures, directions, users, forced states, vision simulations or network profiles, side by side; every scenario of a component on one canvas, with two-axis matrices as tables.
- **Scenario matrices**: `set.Matrix(name, m => m.Vary(...).Vary(...).Pairwise(), configure)` generates one scenario per combination (or an all-pairs subset), tagged `matrix` and described by `ScenarioDefinition.Matrix`.
- **Component status**: `set.Status(ComponentStatus.Beta)` (draft, experimental, beta, stable, deprecated) in the explorer, docs and catalog JSON; deprecated components get `Deprecated`.
- **Notes and design references**: `.Notes(markdown)` on scenarios; `.Design(url)` on sets and scenarios, shown in the new **Design** panel (images with a pixel overlay and a difference blend, embedded Figma frames, links).
- **Panels**: **HTML** (formatted rendered markup) and **Storage** (view and edit `localStorage`/`sessionStorage`).
- **Browser storage isolation**: previews clear storage before they start (`options.IsolateBrowserStorage`), and scenarios seed it with `.LocalStorage(key, value)` and `.SessionStorage(key, value)` before Blazor starts.
- **Users and network profiles in the toolbar**: preview any scenario as a user from `options.AddUser(...)` or signed out (`?user=`), or on Fast 4G, Fast 3G, Slow 3G or offline (`?network=`, `options.NetworkProfiles`).
- **Interaction recorder and debugger**: record clicks, typing, selections and keys (with accessible locators) and assertions into C#; step through a test with the target of each step highlighted.
- **Fake clock**: jump to a date and time, and play the clock in real time, from the Environment panel.
- **Documentation pages**: `options.AddPage(title, markdown)` with `{{scenario:id}}`, `{{scenarios:id}}`, `{{parameters:id}}` and `{{tokens}}` embeds (also in component documentation and notes); broken embeds are `WB1025`.
- **Design tokens page** (`/tokens`): every CSS custom property of the preview stylesheets, per theme, as swatches, type samples, spacing bars, radii and shadows.
- **Parameter value coverage** and **relationships** on documentation pages: which `bool`/enum values the scenarios use, and which catalog components a component renders and is rendered by (read from compiled render code; `ComponentEntry.Uses`/`UsedBy`, also in the catalog JSON).
- **Explorer**: filters (`tag:`, `status:`, `is:matrix`, `is:tested`, `is:failing`, ...) with quick-filter chips, documentation pages in the tree, recently opened scenarios, last-run result dots and failure counts (`ITestResultsProvider`, `/_workbench/api/results`), and keyboard shortcuts (Alt+arrows, `?`, G, O, M).
- **Automated runs**: `.Media(m => m.ForcedColors().Print())` emulates `forced-colors: active` and print media; `.Accessibility(a => a.Todo(reason))` reports violations without failing.
- **Demo**: a third tour part with twelve stops for the new tools; button and badge matrices (one pairwise); forced hover and focus scenarios; a design mockup with a deliberate mismatch; `WhatsNewBanner`, a component that remembers dismissal in `localStorage`; print and forced-colors card scenarios; four toolbar users, a brand background, statuses on eleven components and four documentation pages. The catalog has 33 components, 181 scenarios and 80 interaction tests.

### Fixed
- A fixed viewport rendered 2 px narrower than its preset (a 375 px preset gave a 373 px preview), because the frame's border was inside its width. The border is now a shadow.

- **Overview introduction**: `WorkbenchOptions.Introduction` adds Markdown (safe subset) to the top of the overview page, for example a welcome text or a guided tour that links to scenarios (`scenario/{id}?panel=...`) and documentation pages.
- **Scenario facts**: the scenario header and every scenario on a documentation page summarize what the scenario *simulates* (the HTTP rules that can answer, with status, latency, `Times(n)` and gates; the user, roles and claims; the frozen clock; replaced services and a simulated device; cascading values; culture, direction, theme, viewport and seed) and what it *verifies* (interaction tests, screenshot themes and viewports, accessibility exclusions and disabled rules, budgets). Each chip opens the panel behind it, so a scenario's fakes are visible without opening **Environment**.
- **Demo tour**: the demo opens on a guided tour of sixteen stops, each linking to a scenario with the relevant panel open. The catalog is now called *Trailhead UI*; every showcase scenario says what to try, every scenario set documents the Workbench features it shows, and `Display/Card` gains a do/don't pair (*Icon actions, labeled* and *Don't: icon actions without names*) whose deliberate `button-name` violation shows the Accessibility panel at work. A test checks that every tour link resolves. The catalog now has 32 components, 146 scenarios and 77 interaction tests.
- **.NET MAUI Blazor Hybrid**: `BlazorWorkbench.Maui` (`UseBlazorWorkbenchApp`, `UseBlazorWorkbench`, `WorkbenchPage`, `WorkbenchBlazorWebView`) runs scenarios in the platform WebView with a native scenario picker. `BlazorWorkbench.Hybrid` provides the generated host page, the axe-core file and a loopback, read-only automation API. See [docs/hybrid.md](docs/hybrid.md) and [ADR 0009](docs/adr/0009-blazor-hybrid.md).
- **Hybrid automation**: `workbench test`, `list`, `validate`, `approve`, `publish` and `mcp` work with a MAUI app as the host project. On Windows the runner drives WebView2 through the DevTools Protocol and emulates viewport, color scheme, reduced motion, locale and time zone. Baselines go to `{os}-hybrid`.
- **Device simulation**: `BlazorWorkbench.Maui.Essentials` with `.Device(...)` and `AddSimulatedDevice()`: simulated connectivity, device info, geolocation (including permission and settings failures), battery, share, launcher, clipboard, preferences, secure storage, app info, vibration and haptics. Works in Server, WebAssembly, MAUI and bUnit. Calls are reported as `Platform` diagnostics.
- **CLI**: `--framework` and `"framework"` in `workbench.json` for multi-targeted hosts; `workbench dev` passes it to `dotnet watch`.
- **CLI**: `workbench test --no-budgets` skips performance budget checks (timings are still reported; `settings.performance` in the report is false). CI uses it for the WebAssembly runs, because in Interactive WebAssembly the ready time includes starting the .NET runtime, which took 3-5 s on hosted runners and exceeded the demo's 3 s budget. The Interactive Server run still checks budgets.
- **Components**: the `/_workbench/scenarios` page, a minimal scenario list for hosts without the shell.
- **Demo**: device components (`NetworkStatus`, `ShareCard`, `LocationCard`, `DeviceSummary`) with 13 scenarios, and `BlazorWorkbench.Demo.Maui`, a MAUI app that reuses every demo scenario.
- **Demo overhaul**: eleven feature-level components for a fictional outdoor store (`CheckoutForm`, `ProductSearch`, `FlashSaleBanner`, `PriceTag`, `KpiCard`, `RevenueChart`, `SalesDashboard`, `WorkspaceSettings`, `SessionTimeout`, `AppHeader`, `OnboardingWizard`) with 68 scenarios that exercise what demo pages cannot: payment API failures and server-side validation through custom fake HTTP handlers, partial outages, gated loading states, fake-clock timers, named authorization policies for six simulated users, cascading values, feature-flag service overrides (all three override APIs), viewports, a custom theme, calendars and currencies. The catalog grows to 32 components, 144 scenarios and 76 interaction tests. New: [demo/README.md](demo/README.md) with a ten-minute tour and a feature map, `DemoCatalog.Configure` shared by the three hosts, `AddDemoComponents()` registering the library's policies and services, and `demo/BlazorWorkbench.Demo.Components.Tests`, bUnit tests that reuse the scenarios (smoke tests, gates, fake clock, policies).

### Changed
- The samples moved from `samples/` to `demo/` and were renamed: `SampleComponents` is now `BlazorWorkbench.Demo.Components`, `SampleWorkbench` (with `.Wasm`, `.Wasm.Client` and `.Maui`) is now `BlazorWorkbench.Demo`, and `SampleComponents.Tests` is now `BlazorWorkbench.Demo.Components.Tests`. Namespaces follow the project names; `AddSampleComponents()`, `SampleCatalog`, `sample.css` and the other `Sample*` types are now `AddDemoComponents()`, `DemoCatalog`, `demo.css` and `Demo*`.
- CI builds and tests the MAUI projects on Windows (`maui-windows` workload) and runs the demo scenarios in the MAUI app. Linux jobs use `BlazorWorkbench.Web.slnf`. Packing and releases run on Windows with the `maui` workload.
- The runner uses a preset `BLAZOR_WORKBENCH_CDP_PORT` for hybrid apps instead of picking a free port. Elevated processes (GitHub-hosted Windows runners) need it: WebView2 Runtime 150 and later ignore the `WEBVIEW2_*` variables there, so the port comes from the HKLM `AdditionalBrowserArguments` policy. CI sets that policy, and the connection error explains it when the runner is elevated.

- **Mobile viewports**: presets with `IsMobile` (the built-in `mobile`) now emulate a phone in automated runs, the catalog publisher and Blazor Hybrid apps: a mobile viewport and touch input, so `(hover: none)`, `(pointer: coarse)` and touch events apply. Baselines captured with these presets may need re-approval.
- **Interaction checkpoints** are reported as visual checks of their own, so `workbench approve` can approve them. A failed or missing checkpoint fails the scenario but no longer stops the interaction.
- **Change impact**: `--changed-since` also selects scenarios of components that render a changed component (directly or through other components) and scenario files that render it.
- `workbench mcp`'s `run_tests` applies every `workbench.json` setting `workbench test` applies (browser, timeout, retries, filter, anti-aliasing, accessibility tags).
- `workbench publish` refuses a non-empty output folder that is not a previous catalog, and replaces only catalog files when republishing (it used to delete the whole folder).

### Removed
- The demo's committed visual baselines (`demo/BlazorWorkbench.Demo/baselines`, now ignored by Git), the visual regression CI jobs (`visual-windows`, `visual-linux`) and `eng/visual-linux.sh`. CI still runs render, interaction and accessibility checks on every hosting model; approve baselines locally with `workbench test --visual` and `workbench approve`.

### Fixed
- Markdown documentation: an indented line under a list item continues the item. It used to end the list and start a paragraph, which split wrapped bullet points in the demo's documentation.
- Opening a scenario with `?panel=environment` loads the session (services and requests) as soon as the preview is ready, instead of waiting for **Refresh**.
- **Shell layout**: the shell document resets the default body margin (which added gutters and a page scrollbar around the full-height layout, and cut off the explorer footer) and paints the page background in the shell theme. Explorer groups and components use their own disclosure chevrons, aligned with their children; the native markers were drawn outside the tree, against the sidebar edge. The active **Docs** link is readable.
- **Theme**: the Workbench theme follows the operating system's color scheme until the user toggles it. The preview, and the live previews on documentation pages, use the theme that matches the Workbench theme (the first theme with the same color scheme) unless the scenario, the URL or the toolbar chooses one; toggling the Workbench theme switches such previews live. An explicit toolbar choice sticks and is kept in the deep link. Previously a dark Workbench always showed light previews.
- Previews no longer keep a stale pending count when several fake requests complete at once. Activity changes from concurrent requests could reach the preview script out of order (for example 3, 2, 0, 1), so interaction steps waited for their 5 s idle limit and Blazor Hybrid runs could time out waiting for readiness. The preview now receives the count current when the message is sent.
- Parameter and fixture setup failures in a preview render an error panel with `data-workbench-error` and log an error diagnostic, so readiness, render checks and visual runs fail instead of reporting the panel as a ready scenario.
- The test runner reports timeouts, unreadable baselines (for example Git LFS pointers or unsupported PNG formats) and file system errors as scenario errors instead of aborting the run before `report.json` and `junit.xml` are written.
- `workbench approve --prune` refuses reports of `--only-failed` and `--changed-since` runs (recorded as `settings.partial`) and prunes the platform folder the report was captured for (for example `windows-webassembly`).
- Redaction removes quoted secrets that contain spaces, commas, semicolons, ampersands or braces in .NET and in the preview bridge.
- Keyed service registrations in scenarios are rejected (`WB1013`) instead of replacing the unkeyed service; the Environment panel no longer fails on them.
- Scenario sets and providers registered explicitly are no longer also discovered when their type is public in a scanned assembly (duplicate scenarios with `-2` keys and `WB0006`).
- `NaN` and `±Infinity` parameter values serialize as `"NaN"`/`"Infinity"`, and `IntPtr`/`UIntPtr` are no longer treated as editable, so the catalog API no longer fails on them.
- bUnit test contexts dispose the scenario session and catalog accessor (owned services, HTTP clients, held fake requests); the accessor no longer subscribes to hot reload.
- `AddSimulatedDevice()` geolocation timestamps follow the scenario clock.
- `ScenarioContext.Culture` matches the culture the preview renders in, including toolbar and `?culture=` overrides.
- The `Actions_panel_logs_callbacks_from_the_preview` end-to-end test no longer misses the Actions tab when the call count is already shown ("Actions 1").
- `HttpSimulationTests.Latency_and_activity_tracking` allows for the Windows timer resolution, which can end a simulated 80 ms latency up to one tick early.

### Verified
- 320 unit, bUnit and end-to-end tests pass on Windows 11 (.NET 10.0.403, Chromium 156, WebView2 157).
- All 76 demo scenarios pass render, interaction, accessibility and visual checks on Interactive Server, Interactive WebAssembly and in the .NET MAUI app (one scenario is intentionally skipped). Hybrid visual results were reproducible across runs.

## 0.1.0 — 2026-11-03

First release.

### Added
- **Core**: `ScenarioSet<T>` authoring API with parameters, fixtures, setup, templates, cascading values, service overrides, HTTP simulation, users, culture, direction, theme, viewport, clock, seed, layout, readiness selectors, interaction tests, and visual, accessibility and performance options. Also: discovery with stable ids, metadata from reflection, XML docs and PDB source paths, validation with stable codes, redaction, safe Markdown, Razor snippets, C# export, scenario suggestions, and JSON contracts.
- **Components**: the Workbench UI (explorer with search, toolbar, Controls, Environment, Actions, Diagnostics, Interactions, Accessibility, Visual and Code panels, documentation pages, overview with coverage) and the preview runtime (sessions, a fake HTTP backend, gates, simulated authentication, a session-aware `TimeProvider`, callback instrumentation, two-way binding, an error boundary, navigation interception, a diagnostics bridge, readiness detection, `window.__workbench`).
- **Hosting**: `AddBlazorWorkbench` / `MapBlazorWorkbench`, Interactive Server previews, the `/_workbench/api` endpoints, the axe-core script endpoint, the visual results endpoint, development-only and loopback-only defaults, and remote access gated by an authorization policy.
- **Testing**: the Playwright runner (render health, interaction tests, axe-core scans, visual comparison with per-platform baselines, performance budgets, retries, traces), a PNG codec and perceptual diff, the baseline store with explicit approval, JSON, JUnit and HTML reports, and the static catalog publisher.
- **Testing.Bunit**: `RenderScenario`, `ScenarioIds`, `AssertNoScenarioErrors`, `WaitForScenarioIdle`, `CallbackCount`.
- **CLI** (`workbench`): `init`, `list`, `validate`, `test`, `a11y`, `approve`, `publish`, `dev`, `install-browsers`, `mcp`, with documented exit codes.
- **WebAssembly**: the `BlazorWorkbench.WebAssembly` client package and `Interactivity = WebAssembly` hosting. The catalog is built in the browser, XML docs are fetched from the host, and visual baselines are kept per hosting model (`{os}-webassembly`).
- **Interaction steps**: `DragTo`, `Upload` (works with `InputFile`), `ScrollIntoView`, `ExpectEnabled`, `ExpectDisabled`, `ExpectChecked`, `ExpectUnchecked`, in both the Playwright runner and the UI runner.
- **Change impact**: `workbench test --changed-since <ref>` maps changed files (components, scenario sets, baselines) to scenarios, and falls back to all scenarios when unsure.
- **Visual diff**: anti-aliasing detection (ignored by default, drawn yellow, and counted separately); `--include-anti-aliasing`.
- **Diagnostics and performance**: OpenTelemetry `ActivitySource` and `Meter` (`BlazorWorkbench`), session ids on every diagnostic, `diagnostics.jsonl`, server activity metrics per preview, and the bUnit helpers `MeasureScenario` and `ComponentRenderCount`.
- **Large catalogs**: the explorer renders collapsed groups lazily; a 1,000-component benchmark test.
- **Template**: `BlazorWorkbench.Templates` (`dotnet new blazorworkbench`).
- **Release engineering**: deterministic builds, Source Link, symbol packages, an SPDX SBOM, optional package signing, a tag-driven release workflow, Linux visual checks in a pinned container (`eng/visual-linux.sh`), and a template smoke test in CI.
- **Sample**: a component library with Interactive Server and WebAssembly hosts covering ordinary, generic and templated components, child content, DI, HTTP loading, errors, forms, file upload, drag and drop, authorization, RTL and localization, overlays, a custom editor, a scenario provider, and committed Windows baselines for both hosting models.

### Verified
- 271 unit, bUnit and end-to-end tests pass on Windows 11 with .NET 10.0.403 and Chromium 156. CI passes on Ubuntu and Windows.
- All 63 sample scenarios pass render, interaction, accessibility and visual checks on Interactive Server and on Interactive WebAssembly (one scenario is intentionally skipped).
- A clean consumer project built from the produced packages, and a project created from the template, both pass `workbench test`.
- No known vulnerabilities (`dotnet list package --vulnerable --include-transitive`, checked 2026-11-03). `Microsoft.AspNetCore.Components.WebAssembly.Server` is pinned to 10.0.14 because of advisories in earlier 10.0.x releases.

### Known limitations
See [docs/compatibility.md](docs/compatibility.md#known-limitations).
