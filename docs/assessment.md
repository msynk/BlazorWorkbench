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
| Interactive WebAssembly / Auto | The UI library has no server dependencies, but a WebAssembly preview host, catalog transport and fake HTTP over fetch would be needed. **Not implemented.** |
| .NET MAUI Blazor Hybrid | Previews of platform features need device fakes; components are previewed in a web host. **Not supported.** |

## Phase status

| Phase | Status | Evidence |
| --- | --- | --- |
| 0 Assessment | Done | This document, [ADRs](adr/) |
| 1 Vertical slice | Done | Sample host; discovery, scenarios, preview, inspector |
| 2 Scenario and environment engine | Done | Fixtures, setup, overrides, cascading values, gates, clock, auth, culture, isolation tests |
| 3 Developer experience and diagnostics | Done | Search and keyboard, viewport/theme/culture/direction, diagnostics capture, hot reload, error recovery |
| 4 Automated testing and CLI | Done | Playwright runner, bUnit package, CLI, JSON/JUnit/HTML, exit codes, CI workflow |
| 5 Visual regression and accessibility | Done | Per-platform baselines, explicit approval, diff views, axe in UI and CLI, keyboard and focus steps |
| 6 Documentation and publishing | Done | Docs pages, static catalog, coverage reports, privacy checks |
| 7 Advanced diagnostics and performance | Partial | Budgets, ready time, DOM update metrics, traces. No render counts in browsers and no large-catalog benchmark suite. |
| 8 Extensibility and AI | Done | Providers, editors, collectors, MCP with approval gating and audit log |
| 9 Production readiness | Partial | Packages and tool built and validated through a clean install. Release automation (signing, publishing) is not set up. |

## Measured baseline (sample, Windows 11, Chromium 156)

| Measurement | Value |
| --- | --- |
| Catalog build at startup (15 components, 60 scenarios) | < 1 s, part of host start-up |
| Full `workbench test --a11y --visual` (60 scenarios, 4 workers) | about 13–16 s after the host has started |
| Median preview ready time (`readyMs`) | about 150–400 ms, including circuit start |
| Unit and E2E tests | Core 91 · Components 94 · Testing 21 · CLI 10 · E2E 28, all passing |

These numbers come from one developer workstation. Results on other hardware differ; the report records the environment of each run.

## Backlog (prioritized remaining gaps)

1. WebAssembly preview host (catalog over HTTP, fake HTTP through `HttpMessageHandler` in the browser).
2. Large-catalog benchmarks: discovery of 1,000+ components and explorer virtualization.
3. Affected-scenario selection from changed files (git diff to components to scenarios).
4. Visual diff anti-aliasing detection, which lets the default threshold be lower.
5. Release automation: package signing, NuGet publishing, SBOM.
6. A `dotnet new` template for Workbench hosts.
7. More interaction steps: drag and drop, file upload, clipboard.
