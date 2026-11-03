# Changelog

All notable changes are documented here. The project follows [Semantic Versioning](https://semver.org/).

## 0.1.0 — 2026-11-03

First release.

### Added
- **Core**: `ScenarioSet<T>` authoring API with parameters, fixtures, setup, templates, cascading values, service overrides, HTTP simulation, users, culture, direction, theme, viewport, clock, seed, layout, readiness selectors, interaction tests, and visual, accessibility and performance options. Also: discovery with stable ids, metadata from reflection, XML docs and PDB source paths, validation with stable codes, redaction, safe Markdown, Razor snippets, C# export, scenario suggestions, and JSON contracts.
- **Components**: the Workbench UI (explorer with search, toolbar, Controls, Environment, Actions, Diagnostics, Interactions, Accessibility, Visual and Code panels, documentation pages, overview with coverage) and the preview runtime (sessions, a fake HTTP backend, gates, simulated authentication, a session-aware `TimeProvider`, callback instrumentation, two-way binding, an error boundary, navigation interception, a diagnostics bridge, readiness detection, `window.__workbench`).
- **Hosting**: `AddBlazorWorkbench` / `MapBlazorWorkbench`, Interactive Server previews, the `/_workbench/api` endpoints, the axe-core script endpoint, the visual results endpoint, development-only and loopback-only defaults, and remote access gated by an authorization policy.
- **Testing**: the Playwright runner (render health, interaction tests, axe-core scans, visual comparison with per-platform baselines, performance budgets, retries, traces), a PNG codec and perceptual diff, the baseline store with explicit approval, JSON, JUnit and HTML reports, and the static catalog publisher.
- **Testing.Bunit**: `RenderScenario`, `ScenarioIds`, `AssertNoScenarioErrors`, `WaitForScenarioIdle`, `CallbackCount`.
- **CLI** (`workbench`): `init`, `list`, `validate`, `test`, `a11y`, `approve`, `publish`, `dev`, `install-browsers`, `mcp`, with documented exit codes.
- **Sample**: a component library and host covering ordinary, generic and templated components, child content, DI, HTTP loading, errors, forms, authorization, RTL and localization, overlays, a custom editor, a scenario provider and committed Windows baselines.

### Verified
- Unit, bUnit and end-to-end suites pass on Windows 11 with .NET 10.0.403 and Chromium 156.
- A clean consumer project built from the produced packages passes `workbench test --a11y`.
- `dotnet list package --vulnerable --include-transitive` reports no known vulnerabilities (checked 2026-11-03).

### Known limitations
See [docs/compatibility.md](docs/compatibility.md#known-limitations).
