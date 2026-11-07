# Changelog

All notable changes are documented here. The project follows [Semantic Versioning](https://semver.org/).

## Unreleased

### Added
- **.NET MAUI Blazor Hybrid**: `BlazorWorkbench.Maui` (`UseBlazorWorkbenchApp`, `UseBlazorWorkbench`, `WorkbenchPage`, `WorkbenchBlazorWebView`) runs scenarios in the platform WebView with a native scenario picker. `BlazorWorkbench.Hybrid` provides the generated host page, the axe-core file and a loopback, read-only automation API. See [docs/hybrid.md](docs/hybrid.md) and [ADR 0009](docs/adr/0009-blazor-hybrid.md).
- **Hybrid automation**: `workbench test`, `list`, `validate`, `approve`, `publish` and `mcp` work with a MAUI app as the host project. On Windows the runner drives WebView2 through the DevTools Protocol and emulates viewport, color scheme, reduced motion, locale and time zone. Baselines go to `{os}-hybrid`.
- **Device simulation**: `BlazorWorkbench.Maui.Essentials` with `.Device(...)` and `AddSimulatedDevice()`: simulated connectivity, device info, geolocation (including permission and settings failures), battery, share, launcher, clipboard, preferences, secure storage, app info, vibration and haptics. Works in Server, WebAssembly, MAUI and bUnit. Calls are reported as `Platform` diagnostics.
- **CLI**: `--framework` and `"framework"` in `workbench.json` for multi-targeted hosts; `workbench dev` passes it to `dotnet watch`.
- **CLI**: `workbench test --no-budgets` skips performance budget checks (timings are still reported; `settings.performance` in the report is false). The visual CI jobs use it, because in Interactive WebAssembly the ready time includes starting the .NET runtime, which on busy CI machines exceeded the sample's 3 s budget.
- **Components**: the `/_workbench/scenarios` page, a minimal scenario list for hosts without the shell.
- **Sample**: device components (`NetworkStatus`, `ShareCard`, `LocationCard`, `DeviceSummary`) with 13 scenarios, and `SampleWorkbench.Maui`, a MAUI app that reuses every sample scenario. Baselines for `windows`, `windows-webassembly` and `windows-hybrid`.

### Changed
- CI builds and tests the MAUI projects on Windows (`maui-windows` workload) and runs the sample scenarios in the MAUI app. Linux jobs use `BlazorWorkbench.Web.slnf`. Packing and releases run on Windows with the `maui` workload.
- The runner uses a preset `BLAZOR_WORKBENCH_CDP_PORT` for hybrid apps instead of picking a free port. Elevated processes (GitHub-hosted Windows runners) need it: WebView2 Runtime 150 and later ignore the `WEBVIEW2_*` variables there, so the port comes from the HKLM `AdditionalBrowserArguments` policy. CI sets that policy, and the connection error explains it when the runner is elevated.

### Fixed
- The `Actions_panel_logs_callbacks_from_the_preview` end-to-end test no longer misses the Actions tab when the call count is already shown ("Actions 1").
- `HttpSimulationTests.Latency_and_activity_tracking` allows for the Windows timer resolution, which can end a simulated 80 ms latency up to one tick early.

### Verified
- 320 unit, bUnit and end-to-end tests pass on Windows 11 (.NET 10.0.403, Chromium 156, WebView2 157).
- All 76 sample scenarios pass render, interaction, accessibility and visual checks on Interactive Server, Interactive WebAssembly and in the .NET MAUI app (one scenario is intentionally skipped). Hybrid visual results were reproducible across runs.

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
