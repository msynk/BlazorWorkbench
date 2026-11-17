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
| Responsive design | viewport addon, resizable canvas | – | – | viewport per test | ✔ device mode with breakpoints read from the stylesheets, touch emulation, overflow detection, reflow sweep in CI |
| Visual regression | Chromatic (commercial SaaS) | – | – | screenshots only (.NET) | ✔ built-in diff, per-platform baselines, explicit approval |
| Diagnostics | console/actions | – | – | traces | ✔ correlated, redacted, bounded; traces on failure |
| CLI/CI reports | test runner, JUnit | – | test frameworks | ✔ | ✔ JSON/JUnit/HTML, exit codes, retries/flaky |
| AI/MCP | Storybook MCP (preview) | MCP (Server variant) | – | Playwright MCP | ✔ read-only by default, audited, approval gated |
| Canvas tools (zoom, viewport rotate/custom, backgrounds, grid, measure, outline) | ✔ core toolbar | ✔ (no rotate/zoom levels) | – | – | ✔ in the URL, plus DOM-update flashing and a design overlay |
| Forced pseudo-states | ✔ pseudo-states addon | – | – | – | ✔ toolbar and per scenario (`.ForceState`), covered by screenshots and scans |
| Vision simulation, text scale, tab order | vision simulator | color-vision filter | – | – | ✔ vision, 200% text, WCAG 1.4.12 spacing, tab-order overlay, highlight |
| Combinatorial variants | community addons | – | – | – | ✔ `set.Matrix(...)` with pairwise reduction, gallery table |
| Multi-environment comparison | Chromatic modes (CI) | – | – | – | ✔ Compare view (themes, viewports, cultures, users, states, vision, network) |
| Interaction recorder / step debugger | debugger; recorder experimental | – | – | codegen | ✔ both, with accessible locators and C# output |
| Docs pages with live embeds, design tokens | MDX, doc blocks (manual tokens) | Markdown pages | – | – | ✔ pages with `{{scenario}}` embeds; tokens read from stylesheets |
| Component status, relationships, value coverage | tags; change detection | – | – | – | ✔ status, "renders / rendered by" from IL, value coverage |
| Sidebar test status | ✔ test widget | – | – | – | ✔ last-run dots, `is:failing`, test runner with watch mode in the explorer |
| Decorators | ✔ decorators | ✔ | manual | – | ✔ `.Wrap(...)` per scenario, set and catalog |
| License | MIT (Chromatic commercial) | MPL-2.0 | MIT | Apache-2.0/MIT | MIT |

Sources: the Storybook 9 release announcement (storybook.js.org) and community guides on Storybook 10 testing; the Blazing Story repository README (github.com/jsakamoto/BlazingStory); official bUnit and Playwright documentation.

Differentiation: Workbench treats scenarios as executable, environment-complete definitions. One C# definition drives preview, docs, bUnit, Playwright, accessibility and visual checks, with dependency, network, identity, clock and async simulation built in. Comparable JS tools need several addons and a paid visual service to reach a similar scope.

## Round 2: workbench feature audit (November 2026)

A second study compared Workbench with Storybook 9–11 and Chromatic, and with Widgetbook, React Cosmos, Histoire, Ladle,
Playroom, Lookbook, Showcase, Pattern Lab, Fractal, Compose and SwiftUI previews, Showkase, Paparazzi, Blazing Story and
PlayBlazor, and design-system documentation tools (zeroheight, Supernova). Sources were the official documentation,
repositories and release notes. The features most worth having in a developer-facing workbench, and their state:

| Feature (inspired by) | State |
| --- | --- |
| Zoom, rotate, custom viewport, backgrounds, grid, measure, outline (Storybook core, Blazing Story) | Done ([tools](tools.md)) |
| Vision simulator, text scale, text spacing, tab order (Storybook a11y, Widgetbook, Compose UI Check) | Done |
| Forced pseudo-states (storybook-addon-pseudo-states) | Done, also per scenario |
| Props matrix with pairwise reduction (Histoire, Widgetbook v4, Compose `@PreviewParameter`) | Done |
| Side-by-side modes (Chromatic modes, Playroom, Showkase permutations) | Done (Compare view) |
| Device mode: drag-to-resize viewport, media query ruler, touch emulation, breakpoint edges (browser developer tools, Storybook resizable viewport, Polypane) | Done ([device mode](tools.md#device-mode-and-responsive-design)), with breakpoints read from the stylesheets |
| Viewport sweeps in CI (Pattern Lab, Polypane breakpoint edges) | Done (`workbench test --responsive`, WCAG 1.4.10 reflow at 320 px and every breakpoint edge) |
| Interaction step-through debugger and recorder (Storybook interactions) | Done |
| HTML output panel (Lookbook, storybook-addon-html) | Done |
| Design references and overlay (storybook-addon-designs, Widgetbook `designLink`) | Done |
| Lifecycle status (Pattern Lab, Fractal, zeroheight) | Done |
| Lineage, "uses / used by" (Pattern Lab, Fractal) | Done (from compiled render code) |
| Docs pages with live embeds, design tokens (MDX doc blocks, Lookbook embeds) | Done |
| Sidebar test status and failure filter (Storybook test widget) | Done for the last CLI run, and for UI runs since round 3 |
| Accessibility `todo` mode (Storybook `a11y.test`) | Done |
| Media features in visual runs (Chromatic media features) | Done (forced colors, print; reduced motion always) |
| Browser storage per scenario (storybook-addon-cookie, Cosmos fixtures) | Done, with isolation |
| User and network switching for any scenario (Storybook globals) | Done |
| Running tests from the UI with watch mode (Storybook test widget) | Done in round 3: the explorer's test runner (render, interactions, accessibility, watch) |
| Approving visual changes in the UI (Chromatic UI Review) | Done in round 3: **Approve** in the Visual panel |
| Change detection in the sidebar (Storybook 10.4) | Done in round 3: N/M/R marks and `is:changed` from git |
| Live Razor playground across themes and widths (Playroom) | Open: needs Razor compilation at run time |
| JS interop fakes per scenario (bUnit `JSInterop`) | Done in round 3: `.JSInterop(...)`, see below for how the circuit binding was solved |
| Composition of several catalogs (Storybook refs) | Open |
| Canvas comments for agents (Histoire) | Open |

## Round 3: ecosystem refresh (November 2026)

A third study looked at what changed since round 2: Storybook 9.0 to 10.6 (test widget with coverage and watch mode,
module automocking with `sb.mock`, CSF factories, change detection in 10.4, MCP and manifests in 10.3, agentic review in
10.5, skills in 10.6), Chromatic's Visual Tests addon, Ladle 3, Histoire, React Cosmos 7 (bookmarks, fuzzy search, remote
renderers), Playroom, Widgetbook 4 (scenarios, modes, matrix), Lookbook (display options, embeds), Phoenix Storybook,
Showkase, Jetpack Compose previews (resizable preview, animation preview, UI Check), SwiftUI previews (`@Previewable`,
variants, selectable mode), Kitbook, Blazing Story (MCP, VRT) and PlayBlazor. Sources were release notes, documentation and
package pages. Gaps found in Workbench and what was done:

| Gap (inspired by) | State |
| --- | --- |
| Per-scenario fakes for JavaScript the component calls (Storybook `sb.mock`, bUnit `JSInterop`) | Done: `.JSInterop(...)` with returns, errors, delays, gates, argument conditions, module exports and strict mode; every call recorded, `ExpectJSCall`, bUnit `JSCallCount`. `IJSRuntime` is bound to the circuit when it starts, so the registration is rewritten to a transient that returns the real runtime until the scenario is active and a recording decorator afterwards. |
| Run tests from the UI, with watch mode (Storybook test widget) | Done: test runner in the explorer |
| Accept baselines in the UI (Chromatic Visual Tests addon) | Done: Visual panel **Approve**, shared `BaselineApproval` |
| Changed/new/related stories in the sidebar (Storybook 10.4) | Done: git-based marks and filters, `ChangesBase` for branches |
| Bookmarks (React Cosmos) | Done: pinned scenarios |
| Animation timeline and media settings (Compose Animation Preview, Xcode Variants contrast, browser developer tools) | Done: reduced motion, more contrast and print emulation; animations slowed or paused at a position |
| ⌘K search (Cosmos, Storybook) | Done: Ctrl/⌘+K focuses search |
| Change-aware agent tooling (Storybook agentic review) | Done: MCP `list_changed_scenarios` |
| Save edited controls back to the scenario file (Storybook 8.1+, Kitbook) | Not done: C# has no stable round-trip from a running object graph to source. The Code panel shows the scenario as C# to paste; writing source files from a browser also widens the attack surface. |
| Selectable mode: click an element to jump to its component source (Xcode, Kitbook) | Not done: Blazor does not map DOM nodes to component instances. Needs renderer instrumentation; the HTML panel and PDB source links cover part of it. |
| Live Razor playground (Playroom) | Still open: needs Razor compilation in the browser or on the server. |
| Composition of several catalogs (Storybook refs) | Still open: previews of another host are cross-origin, so the bridge would need an origin allowlist. |
| Coverage in the UI (Storybook 9) | Not planned: .NET coverage needs instrumented builds, which the in-browser runner cannot produce. |

Problems found while doing it, both fixed:

- With newer SDKs, every scenario failed its render check in Development, because the injected hot reload client fetches
  `_framework/dotnet-browser-tools/…` files that exist only under `dotnet watch`. Development tooling requests are now ignored.
- The MAUI demo failed to build with newer Windows App SDK analyzers (`CsWinRT1032` on collection expressions in the
  shared scenarios). The warning concerns trimming and AOT, which the app does not use; it is suppressed and documented.
- The explorer's screen-reader-only labels are absolutely positioned; deep in a long tree they stretched the page and made
  it scroll. The tree's scroll areas now contain them.

## Round 4: ecosystem refresh and code audit (November 2026)

A fourth study looked at Storybook 10.4 to 10.6 (change review, agentic review, agent skills; 11 is in alpha with CSF
factories as the default), Chromatic accessibility tests, Ladle, Histoire, React Cosmos, Playroom, Widgetbook 4, Lookbook,
Phoenix Storybook, Kitbook, Jetpack Compose previews (`@PreviewWrapper`, screenshot testing), SwiftUI `PreviewModifier`,
Paparazzi, Polypane (outline panel, synced panes), Figma Code Connect, Supernova and zeroheight, Blazing Story and PlayBlazor.
Backlight shut down in June 2025 and was dropped. Sources were release notes, official documentation and package pages.
A code audit of the shell, the preview bridge, hosting, the test runner and the CLI ran alongside it.

Features added:

| Feature (inspired by) | State |
| --- | --- |
| Decorators: wrap a scenario in markup or a component, per scenario, set or catalog (Storybook decorators, SwiftUI `PreviewModifier`, Compose `@PreviewWrapper`) | Done: `.Wrap(...)`, `.Wrap<T>()`, `.WrapStyle(...)`, `options.AddDecorator(...)` ([scenarios](scenarios.md#decorators)) |
| Controls from standard attributes: sliders, labels, sections, order, hidden parameters (Storybook argTypes, Lookbook `@param`) | Done: `[Range]`, `[Display]`, `[DisplayName]`, `[Category]`, `[Description]`, `[DataType]`, `[StringLength]`, `[EditorBrowsable]` ([editors](parameter-editors.md)) |
| Explorer order (Storybook `storySort`) | Done: `options.ExplorerOrder`; Alt+arrows follow the tree |
| Open in editor (storybook-addon-source-link, Kitbook) | Done: **Edit scenario** (compiler-recorded line of `set.Add`) and **Edit component**, `options.EditorUrlTemplate` |
| Markup snapshots (Paparazzi, Verify, Jest) | Done: `cut.MatchMarkupSnapshot()` in bUnit ([bUnit](bunit.md#markup-snapshots)) |
| `llms.txt` in published catalogs (Storybook manifests, llmstxt.org) | Done |
| Shell branding (Storybook manager theming) | Done: logo, accent colors, shell stylesheets |
| Tag exclusion in search (Storybook `!tag`), expand/collapse all | Done: `-tag:x`, `-is:x` |
| Configurable single-key shortcuts, ARIA tabs, skip link, error boundaries in the shell (Storybook 10.3 accessibility work) | Done |
| Toolbar globals: your own menus such as density, brand or a feature flag (Storybook `globalTypes`) | Done: `options.AddGlobal(...)`, `.Global(...)`; cascaded by id, `data-{id}` on the preview, `ctx.Global(id)`, `?g.{id}=` |
| Accessibility outline: headings and landmarks with structural warnings (Polypane outline panel) | Done: **Outline** in the Accessibility panel |
| Hosting under a path base (reverse proxies) | Done and covered by an end-to-end test |
| Timestamps in the viewer's time zone with Interactive Server | Done |
| Accessibility baselines: fail only on new violations (Chromatic accessibility tests) | Done: `workbench approve --accessibility`, `baselines/accessibility/{scenario}.json` |
| Panels and toolbar items of your own (Storybook addon API, Lookbook panels) | Done: `options.AddPanel<T>(title)`, `options.AddToolbarItem<T>()`, `WorkbenchExtensionContext` with `EvaluateAsync` in the preview ([extensibility](extensibility.md#panels-and-toolbar-items-of-your-own)) |
| JSON editor for complex parameters (Storybook object control) | Done: records, classes and collections of plain data, deserialized into the declared type only |
| Synchronized interaction across Compare frames (Polypane synced panes) | Done: **Sync interactions** in the Compare view |
| Catalog composition (Storybook refs) | Done: `options.AddCatalogReference(title, url)` lists published catalogs in the explorer and search, with their pages framed without scripts ([publishing](publishing.md#composing-catalogs)) |

Still open, with the reason:

| Gap | Why |
| --- | --- |
| Live Razor playground (Playroom) | Needs Razor and Roslyn compilation at run time. In the browser that means shipping the compiler (tens of MB) to WebAssembly hosts only; on the server it means compiling and running code typed into a web page, which would turn every reachable Workbench into a remote code execution endpoint. Decorators, the JSON editor and the Code panel cover the composition use cases without either. |

Problems found by the audit and fixed:

- **Security.** A loopback-only host answered any `Host` header, so DNS rebinding could read the catalog and approve
  baselines; the host now also checks the `Host` header. A base ref given to `list_changed_scenarios` could inject git
  options; refs are validated and passed after `--end-of-options`. The security headers no longer overwrite a host's CSP.
  Path containment checks are case-sensitive on case-sensitive file systems. The preview accepts commands only from the
  window that embeds it.
- **Browser storage.** Clearing the shared storage for each preview raced when the Gallery, Compare view, docs pages or the
  test runner loaded several frames at once, so seeds were lost. Each preview now gets its own in-memory storage.
- **Test runner.** A canceled run that finished late ended the next run; results now carry a run id. A frame that never
  loaded or a paused interaction could block a worker forever; loads, interactions and scans are bounded by the job timeout
  and canceled with the run.
- **Readiness and state.** Callbacks raised in `OnInitialized` were not counted by `ExpectCallback` (in Playwright and the
  UI runner) or listed in Actions. The preview stayed in the error state after **Recover** or after fixing arguments, and
  scans or interactions started in Compare or Gallery mode, or in the error state, stayed "Scanning…" forever. Custom HTTP
  handler responses ended activity before the body was read, and handlers got no cancellation token.
- **Shell.** No error boundary: a failing panel or custom editor ended the circuit. Links to the current scenario with
  another `panel` or `mode` were ignored. The Visual panel could show the previous scenario's results. The shell's callback
  list grew without bound. Alt+arrows did not follow the tree. The DateTimeOffset editor dropped the offset. The dark theme
  error badges failed contrast. Unknown routes rendered a blank page. A dark shell flashed light and loaded its preview twice.
- **CLI and publishing.** `--changed-since` and the host used different git commands (and non-ASCII paths were quoted);
  both now share one implementation based on the merge base. `get_last_report` with a filter returned the unfiltered
  summary. Publishing excluded-tag scenarios when every scenario was generated, and empty viewport or theme lists crashed
  the publisher and the runner. A JS module disposed during an import could throw or leak.

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
| Interactive WebAssembly | Implemented: `BlazorWorkbench.WebAssembly` plus `Interactivity = WebAssembly`. All demo scenarios pass on both models; baselines are stored per hosting model ([ADR 0008](adr/0008-webassembly-hosting.md)). |
| Interactive Auto | Not offered as a Workbench mode: previews must be deterministic, and Auto switches models between loads. Use Server or WebAssembly explicitly. |
| .NET MAUI Blazor Hybrid | Implemented: `BlazorWorkbench.Maui` runs scenarios in the platform WebView with a native picker; `workbench test` drives WebView2 on Windows over the DevTools Protocol; `BlazorWorkbench.Maui.Essentials` simulates device services in every hosting model ([ADR 0009](adr/0009-blazor-hybrid.md)). |

## Phase status

| Phase | Status | Evidence |
| --- | --- | --- |
| 0 Assessment | Done | This document, [ADRs](adr/) |
| 1 Vertical slice | Done | Demo hosts; discovery, scenarios, preview, inspector, template |
| 2 Scenario and environment engine | Done | Fixtures, setup, overrides, cascading values, gates, clock, auth, culture, isolation tests |
| 3 Developer experience and diagnostics | Done | Search and keyboard, viewport/theme/culture/direction, diagnostics capture, hot reload, error recovery, lazy explorer for large catalogs |
| 4 Automated testing and CLI | Done | Playwright runner, bUnit package, CLI, JSON/JUnit/HTML, exit codes, change impact, CI on Windows and Linux |
| 5 Visual regression and accessibility | Done | Baselines per platform and hosting model, explicit approval, anti-aliasing-aware diff, axe in UI and CLI, keyboard, focus, drag and upload steps |
| 6 Documentation and publishing | Done | Docs, static catalog, coverage reports, privacy checks |
| 7 Advanced diagnostics and performance | Done | Budgets, ready time, DOM updates, server activity metrics, bUnit render counts and allocations, OpenTelemetry activities and metrics, `diagnostics.jsonl` with session correlation, 1,000-component benchmark |
| 8 Extensibility and AI | Done | Providers, editors, collectors, telemetry, MCP with approval gating and audit log |
| 9 Production readiness | Done (release pending) | Deterministic packages with Source Link and symbols, SBOM, optional signing, release workflow, template, clean-install validation. Publishing needs a tag and the `NUGET_API_KEY` secret. |
| 10 Workbench tools (round 2) | Done | Canvas toolbar, accessibility simulations, forced states, Compare and Gallery, matrices, status, notes, designs, HTML and Storage panels, recorder and debugger, pages, tokens, relationships, coverage, last-run status; 14 new end-to-end tests ([tools](tools.md)) |

## Measured baseline (Windows 11, Chromium 156, .NET 10.0.403)

| Measurement | Value |
| --- | --- |
| Demo catalog (32 components, 146 scenarios, 77 interaction tests) | built at startup in well under 1 s |
| Synthetic catalog, 1,000 components × 10 parameters | build ≈ 0.75 s, filter ≈ 1 ms, catalog JSON ≈ 3 MB serialized in ≈ 0.25 s |
| Explorer with 1,000 components (bUnit) | 25 collapsed groups; children render only when opened |
| `workbench test --a11y --visual`, Interactive Server (144 scenarios, 4 workers) | ≈ 36 s after host start |
| Same, Interactive WebAssembly | ≈ 105 s (each preview boots the runtime) |
| Same, .NET MAUI Blazor Hybrid (WebView2 157, one page) | ≈ 98 s after the app starts |
| Automated tests | Core 101 · Components 186 · Hybrid 28 · Testing 45 · CLI 12 · E2E 34 (incl. WebAssembly and MAUI) · Demo 156, all passing |
| CI (GitHub Actions, first push) | Build/test on Ubuntu and Windows, Windows visual job and pack: all green |

These numbers come from one workstation, and results on other hardware differ. Each report records the environment of its run.

## Findings from running the suite

Workbench found real defects in the demo library, which shows it works as intended:

- `Counter` and `SortableList` discarded user state when the parent re-rendered (render check and interaction tests).
- `ProductList` had an accessible name that did not contain the visible text (axe `label-content-name-mismatch`, WCAG 2.5.3).
- Server and WebAssembly render three scenarios differently (exception message resources, ICU data). This led to baselines per hosting model.
- Blazor Hybrid: a BlazorWebView cannot host the iframe-based shell, MAUI did not apply WebView2 options from `BlazorWebViewInitializing`, and MAUI global usings clash with common component names (`Button`, `Size`). These led to the native picker, environment-variable activation of the DevTools port, and the documented `<Using Remove>` fix.
- Readiness race: a fake response was counted as done before the component had consumed it. Fixed by tracking the response body.
- Packaging: hosts without `.razor` files lacked `blazor.web.js` ([ADR 0004](adr/0004-blazor-framework-assets.md)), and the WebAssembly server package had vulnerable 10.0.x versions (pinned to 10.0.14).
- Demo overhaul (feature-level components): `AppHeader` named its cart "Cart, 128 items" while the badge showed "99+" (axe `label-content-name-mismatch` in the *Busy cart* scenario).
- Pending-count race: when several fake requests finished at once (a dashboard releasing three requests from one gate), the preview could receive the activity counts out of order and keep a stale non-zero value. Interaction steps then waited for their 5 s idle limit, and a Blazor Hybrid run timed out waiting for readiness. Fixed by sending the current count when the message goes out.

- Round 4, Blazor Hybrid: the flash sale banner's "Shop the sale" button lost its light background on hover (the generic
  `.btn-primary:hover` rule is more specific), leaving dark text on a dark background. It surfaced in the MAUI app, whose
  WebView sat under the real mouse pointer, once a runner bug was fixed: axe-core shares a rule's `tags` array between
  violations and incomplete results, and Playwright serialized the repeated reference as an object, which ended the scan
  with an infrastructure error. Hybrid runs now also move the synthetic pointer to a corner, so `:hover` never depends on
  where the mouse happens to be.

## Remaining gaps

| Gap | Why it remains | Next step |
| --- | --- | --- |
| Visual regression in CI | The demo's baselines are not committed, so CI runs no visual comparison. | Commit per-platform baselines (pinned Chromium; a pinned container image for Linux) and add a visual job. |
| Package publishing | Needs a version tag and the `NUGET_API_KEY` secret (and optionally a signing certificate). | Push tag `v0.1.0` after review. The release workflow has not run yet. |
| Automated Blazor Hybrid runs on Android, iOS and Mac Catalyst | Playwright has no supported API for Android WebView or WKWebView. | Manual review in the MAUI app; automated runs on Windows. |
| Gating hybrid visual checks in CI | WebView2 updates itself, so `windows-hybrid` baselines can drift on hosted runners. | Run them as an informational step; pin a Fixed Version WebView2 runtime to gate. |
| Clipboard and drag between frames | Browser permission models differ across engines. | Add to the DSL when Playwright support is uniform. |
| Screen reader verification | No automation can replace it. | Manual checklist in [accessibility](accessibility.md). |
