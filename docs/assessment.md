# Assessment, competitor baseline and roadmap

This document holds the Phase 0 deliverables: repository assessment, ecosystem audit, dependency review, architecture boundaries, backlog and the baseline, and it tracks the status of every phase.

## Repository assessment (start of work)

The repository contained only `LICENSE` (MIT, 2026) and a one-line `README.md`. There were no projects, CI, conventions or consumers, so there were no compatibility constraints. Decisions made as a result:

- **Target framework**: `net10.0`, the current LTS (10.0.403 SDK installed; 8.0 and an 11.0 RC were also present). `global.json` pins 10.0.x with `rollForward: latestMajor` and no prereleases.
- **Conventions**: central package management, nullable reference types, warnings as errors, file-scoped namespaces, `.slnx` solution, xUnit v3.
- **Baseline**: nothing to build or test.

## Competitor capability matrix

| Capability | Storybook 9/10 (JS) | Blazing Story | bUnit | Playwright | **Blazor Workbench** |
| --- | --- | --- | --- | --- | --- |
| Native Blazor components | via web components only | ✔ (`.stories.razor`) | ✔ | n/a | ✔ (C# `ScenarioSet<T>`) |
| Interactive controls | ✔ args/controls | ✔ | – | – | ✔ (null/unset/fixtures/custom editors, deep links, C# export) |
| Auto-generated docs | ✔ autodocs/MDX | ✔ | – | – | ✔ (XML docs, Markdown, snippets, static publishing, coverage) |
| Per-story DI/service mocks | module mocking (JS) | host-level only | manual | – | ✔ per-scenario overrides, isolated sessions |
| Network mocking | addons (MSW) | – | `MockHttp` manual | route interception | ✔ built-in fake backend (latency, faults, offline, retries) |
| Deterministic async control | – | – | manual | – | ✔ **gates** (UI, tests, code) |
| Auth/culture/RTL/clock simulation | partial (addons/globals) | – | manual | locale/timezone | ✔ per scenario + toolbar |
| Interaction tests | ✔ play functions (Vitest) | – | ✔ (C#) | ✔ | ✔ serializable steps; Playwright runner + in-UI runner |
| Accessibility | ✔ addon (axe) | – | – | via axe packages | ✔ axe in UI and CLI, severity gates, justifications |
| Visual regression | Chromatic (commercial SaaS) | – | – | screenshots only (.NET) | ✔ built-in diff, per-platform baselines, explicit approval |
| Diagnostics | console/actions | – | – | traces | ✔ correlated, redacted, bounded; traces on failure |
| CLI/CI reports | test runner, JUnit | – | test frameworks | ✔ | ✔ JSON/JUnit/HTML, exit codes, retries/flaky |
| AI/MCP | Storybook MCP (preview) | MCP (Server variant) | – | Playwright MCP | ✔ read-only by default, audited, approval gated |
| License | MIT (Chromatic commercial) | MPL-2.0 | MIT | Apache-2.0/MIT | MIT |

Sources: the Storybook 9 release announcement (storybook.js.org) and community guides on Storybook 10 testing; the Blazing Story repository README (github.com/jsakamoto/BlazingStory); official bUnit and Playwright documentation.

Differentiation: Workbench treats scenarios as executable, environment-complete definitions. One C# definition drives preview, docs, bUnit, Playwright, accessibility and visual checks, with dependency, network, identity, clock and async simulation built in. Comparable JS tools need several addons and a paid visual service to reach a similar scope.

## Dependency and licensing review

| Dependency | Version | License | Why | Alternatives considered |
| --- | --- | --- | --- | --- |
| Microsoft.Playwright | 1.64.1 | MIT (Apache-2.0 upstream) | Mature cross-browser automation | Selenium (weaker locator and assertion model) |
| Deque.AxeCore.Commons | 4.14.0 | MPL-2.0 (unmodified axe-core) | Industry-standard accessibility engine | Writing rules ourselves (not credible) |
| bunit | 2.13.3 | MIT | Standard Blazor component testing | – |
| System.CommandLine | 2.0.13 | MIT | Stable CLI parsing | Spectre.Console.Cli |
| ModelContextProtocol | 2.3.0 | Apache-2.0 | Official C# MCP SDK | A hand-written JSON-RPC server |
| Microsoft.Extensions.TimeProvider.Testing | 10.0.0 | MIT | Battle-tested fake clock | A custom implementation |
| Microsoft.AspNetCore.App.Internal.Assets | 10.0.0 | MIT | Blazor framework scripts for hosts without `.razor` files ([ADR 0004](adr/0004-blazor-framework-assets.md)) | Embedding a copy of `blazor.web.js` |
| *(not used)* SixLabors.ImageSharp | – | Six Labors Split License | – | Rejected for licensing: an own PNG codec and diff ([ADR 0005](adr/0005-image-comparison.md)) |
| *(not used)* Markdig | – | BSD-2 | – | A small safe subset renderer avoids an HTML sanitizer |

MPL-2.0 for axe-core is a file-level copyleft. Workbench redistributes the unmodified file through Deque's package and does not modify it. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Hosting models

| Model | Assessment |
| --- | --- |
| Blazor Web App, Interactive Server | Primary: one circuit per preview gives natural session isolation, and server-side diagnostics are simple. **Implemented.** |
| Interactive WebAssembly | Implemented: `BlazorWorkbench.WebAssembly` plus `Interactivity = WebAssembly`. All sample scenarios pass on both models; baselines are stored per hosting model ([ADR 0008](adr/0008-webassembly-hosting.md)). |
| Interactive Auto | Not offered as a Workbench mode: previews must be deterministic, and Auto switches models between loads. Use Server or WebAssembly explicitly. |
| .NET MAUI Blazor Hybrid | Implemented: `BlazorWorkbench.Maui` runs scenarios in the platform WebView with a native picker; `workbench test` drives WebView2 on Windows over the DevTools Protocol; `BlazorWorkbench.Maui.Essentials` simulates device services in every hosting model ([ADR 0009](adr/0009-blazor-hybrid.md)). |

## Phase status

| Phase | Status | Evidence |
| --- | --- | --- |
| 0 Assessment | Done | This document, [ADRs](adr/) |
| 1 Vertical slice | Done | Sample hosts; discovery, scenarios, preview, inspector, template |
| 2 Scenario and environment engine | Done | Fixtures, setup, overrides, cascading values, gates, clock, auth, culture, isolation tests |
| 3 Developer experience and diagnostics | Done | Search and keyboard, viewport/theme/culture/direction, diagnostics capture, hot reload, error recovery, lazy explorer for large catalogs |
| 4 Automated testing and CLI | Done | Playwright runner, bUnit package, CLI, JSON/JUnit/HTML, exit codes, change impact, CI on Windows and Linux |
| 5 Visual regression and accessibility | Done | Baselines per platform and hosting model, explicit approval, anti-aliasing-aware diff, axe in UI and CLI, keyboard, focus, drag and upload steps |
| 6 Documentation and publishing | Done | Docs, static catalog, coverage reports, privacy checks |
| 7 Advanced diagnostics and performance | Done | Budgets, ready time, DOM updates, server activity metrics, bUnit render counts and allocations, OpenTelemetry activities and metrics, `diagnostics.jsonl` with session correlation, 1,000-component benchmark |
| 8 Extensibility and AI | Done | Providers, editors, collectors, telemetry, MCP with approval gating and audit log |
| 9 Production readiness | Done (release pending) | Deterministic packages with Source Link and symbols, SBOM, optional signing, release workflow, template, clean-install validation. Publishing needs a tag and the `NUGET_API_KEY` secret. |

## Measured baseline (Windows 11, Chromium 156, .NET 10.0.403)

| Measurement | Value |
| --- | --- |
| Sample catalog (32 components, 144 scenarios, 76 interaction tests) | built at startup in well under 1 s |
| Synthetic catalog, 1,000 components × 10 parameters | build ≈ 0.75 s, filter ≈ 1 ms, catalog JSON ≈ 3 MB serialized in ≈ 0.25 s |
| Explorer with 1,000 components (bUnit) | 25 collapsed groups; children render only when opened |
| `workbench test --a11y --visual`, Interactive Server (144 scenarios, 4 workers) | ≈ 36 s after host start |
| Same, Interactive WebAssembly | ≈ 105 s (each preview boots the runtime) |
| Same, .NET MAUI Blazor Hybrid (WebView2 157, one page) | ≈ 98 s after the app starts |
| Automated tests | Core 101 · Components 186 · Hybrid 28 · Testing 45 · CLI 12 · E2E 34 (incl. WebAssembly and MAUI) · Samples 156, all passing |
| CI (GitHub Actions, first push) | Build/test on Ubuntu and Windows, Windows visual job and pack: all green |

These numbers come from one workstation, and results on other hardware differ. Each report records the environment of its run.

## Findings from running the suite

Workbench found real defects in the sample library, which shows it works as intended:

- `Counter` and `SortableList` discarded user state when the parent re-rendered (render check and interaction tests).
- `ProductList` had an accessible name that did not contain the visible text (axe `label-content-name-mismatch`, WCAG 2.5.3).
- Server and WebAssembly render three scenarios differently (exception message resources, ICU data). This led to baselines per hosting model.
- Blazor Hybrid: a BlazorWebView cannot host the iframe-based shell, MAUI did not apply WebView2 options from `BlazorWebViewInitializing`, and MAUI global usings clash with common component names (`Button`, `Size`). These led to the native picker, environment-variable activation of the DevTools port, and the documented `<Using Remove>` fix.
- Readiness race: a fake response was counted as done before the component had consumed it. Fixed by tracking the response body.
- Packaging: hosts without `.razor` files lacked `blazor.web.js` ([ADR 0004](adr/0004-blazor-framework-assets.md)), and the WebAssembly server package had vulnerable 10.0.x versions (pinned to 10.0.14).
- Sample overhaul (feature-level components): `AppHeader` named its cart "Cart, 128 items" while the badge showed "99+" (axe `label-content-name-mismatch` in the *Busy cart* scenario).
- Pending-count race: when several fake requests finished at once (a dashboard releasing three requests from one gate), the preview could receive the activity counts out of order and keep a stale non-zero value. Interaction steps then waited for their 5 s idle limit, and a Blazor Hybrid run timed out waiting for readiness. Fixed by sending the current count when the message goes out.

## Remaining gaps

| Gap | Why it remains | Next step |
| --- | --- | --- |
| Linux visual baselines | This machine has no network inside Docker or WSL, so the CI container image cannot be built here. | The `visual-linux` CI job runs in bootstrap mode until baselines exist. Approve the first run's artifact (`workbench approve -o <artifact>`) and commit. |
| Package publishing | Needs a version tag and the `NUGET_API_KEY` secret (and optionally a signing certificate). | Push tag `v0.1.0` after review. The release workflow has not run yet. |
| Automated Blazor Hybrid runs on Android, iOS and Mac Catalyst | Playwright has no supported API for Android WebView or WKWebView. | Manual review in the MAUI app; automated runs on Windows. |
| Gating hybrid visual checks in CI | WebView2 updates itself, so `windows-hybrid` baselines can drift on hosted runners. | Informational CI step; pin a Fixed Version WebView2 runtime to gate. |
| Clipboard and drag between frames | Browser permission models differ across engines. | Add to the DSL when Playwright support is uniform. |
| Screen reader verification | No automation can replace it. | Manual checklist in [accessibility](accessibility.md). |
