# Compatibility and support

## Supported environments

| Area | Supported | Notes |
| --- | --- | --- |
| Target framework | .NET 10 (`net10.0`) | The SDK pins to 10.0.x through `global.json` (`rollForward: latestMajor`, so later stable SDKs work). |
| Preview hosting | **Blazor Web App, Interactive Server** (default), **Interactive WebAssembly** and **.NET MAUI Blazor Hybrid** | `BlazorWorkbench.Hosting`, plus `BlazorWorkbench.WebAssembly` for WebAssembly clients ([guide](webassembly.md)) and `BlazorWorkbench.Maui` for MAUI apps ([guide](hybrid.md)). Prerendering is disabled for Workbench pages. |
| Blazor Hybrid platforms | Windows (WebView2): previews and automated runs. Android, iOS, Mac Catalyst: previews | `workbench test` drives WebView2 through the DevTools Protocol; other platform WebViews have no supported automation API. |
| Components under test | Any Razor component library usable from Interactive Server | Includes generic, templated and `EditContext`-dependent components, components with constructor injection, and `AuthorizeView`. |
| Browsers for automation | Chromium (default), Firefox, WebKit through Playwright | Visual baselines are captured with Chromium in CI and are specific to browser and platform. |
| Workbench UI browsers | Current Chromium, Firefox and Safari | |
| Operating systems | Windows, Linux, macOS | Baselines are stored per platform. |

## Not supported (yet)

| Scenario | Status | Workaround |
| --- | --- | --- |
| Standalone WebAssembly apps without an ASP.NET Core host | Not supported. The CLI, catalog API, axe-core script and baselines are served by the host. | Use the two-project WebAssembly host ([guide](webassembly.md)). |
| Automated runs of Blazor Hybrid apps on Android, iOS or Mac Catalyst | Not supported. Playwright cannot drive Android WebView or WKWebView. | Run the MAUI app there for manual review; automate on Windows, or run the same scenarios with `.Device(...)` in a web host. |
| The full Workbench UI (explorer, panels) inside a MAUI app | Not supported. The shell uses iframes, which a BlazorWebView cannot host. | The MAUI app has a native scenario picker and one preview at a time; use a web host for the explorer. |
| WPF and Windows Forms BlazorWebView | No ready-made integration. | Build one with `BlazorWorkbench.Hybrid` (see [extensibility](extensibility.md)). |
| Static SSR-only components and `@page` routes | Pages are excluded by default (`IncludeRoutableComponents` enables them). Previews are always interactive. | Extract presentational components. |
| Per-scenario `TimeProvider` override through `Services` | Rejected (`WB1022`). | Use `.Clock(...)`. |
| Overriding services consumed by host singletons | Rejected by the container at startup. | Override the consumer. |
| Media-query dark mode inside the UI iframe | Browsers cannot emulate it per iframe. | Theme attributes in the UI; real `prefers-color-scheme` emulation in CLI runs. |
| Component render counts in browsers | Not observable without changing components. | `domUpdates` and server activity metrics; `cut.ComponentRenderCount()` in bUnit. |

## Known limitations

- **Readiness** detects Workbench-tracked work (fake HTTP, gates). Other asynchronous work, such as your own timers, real JavaScript or third-party SDKs, is covered only by the 150 ms DOM quiet window. Use `ReadyWhen` or gates for such components.
- **The in-browser interaction runner** (UI) simulates events and approximates accessible names. The Playwright runner (CLI) is authoritative.
- **Accessibility automation** covers part of WCAG. Manual review is still required.
- **Hot reload** rebuilds the catalog and reloads previews. A service type that becomes overridable only after startup needs a restart.
- **Culture** is applied per circuit activity. Code that runs outside circuit activities (for example a `System.Threading.Timer` created without `InvokeAsync`) does not get the scenario culture.
- **Scenario services** become scoped in the host container (see [environment](environment.md#services)).
- **Package size**: the CLI tool contains the Playwright driver (about 40 MB).
- **WebAssembly clients** must not be trimmed, and each preview boots the runtime (slower readiness).
- **Blazor Hybrid runs** use the app's single WebView page: scenarios run one at a time, without Playwright traces, and hybrid baselines depend on the installed WebView2 version.
- **Change impact** (`--changed-since`) maps files through PDB source paths. Shared code and global assets select every scenario.

## Versioning policy

- Packages follow semantic versioning. All Workbench packages are released together with the same version.
- Public API, the JSON contracts (`schemaVersion`), `workbench.json`, CLI commands and exit codes, and `window.__workbench` change incompatibly only in major versions.
- Deprecations: members are marked `[Obsolete]` with guidance for at least one minor version before removal.
- Scenario ids are derived from titles and names. Changing them changes ids and baseline paths; use `.Key(...)` to keep an id stable.
- Contracts carry `schemaVersion`. Readers ignore unknown properties, so newer minor versions stay readable.

## Upgrading

1. Update all `BlazorWorkbench.*` packages and the CLI tool to the same version.
2. Run `workbench validate` and `workbench test`.
3. If the Playwright or browser version changed, review the visual differences and re-approve them with `workbench approve --all`.
4. Read the [CHANGELOG](../CHANGELOG.md) for migration notes.
