# ADR 0009: .NET MAUI Blazor Hybrid hosting

- **Status**: Accepted
- **Date**: 2026-11-07

## Context
Teams that ship Blazor Hybrid apps need to see their components in the platform WebView, with the app's platform services, and to test components that use device APIs. The web hosts cannot show this. Three things differ from the web hosts:

- A BlazorWebView has one document, and no ASP.NET Core server exposes the catalog API that the CLI uses.
- The WebView cannot host the Workbench shell: the shell embeds previews in iframes, and an iframe that loads `blazor.webview.js` attaches as a new page and disposes the shell's scope.
- Playwright cannot launch a WebView. On Windows it can connect to WebView2 through the Chrome DevTools Protocol. It has no supported way to drive Android WebView or WKWebView.

## Decision
- **Packages.** `BlazorWorkbench.Hybrid` (`net10.0`, no MAUI dependency) holds the platform-neutral parts: catalog and runtime registration, the generated host page, the axe-core file, and the automation endpoint. `BlazorWorkbench.Maui` adds the MAUI integration (`UseBlazorWorkbenchApp`, `WorkbenchPage`, `WorkbenchBlazorWebView`). `BlazorWorkbench.Maui.Essentials` (`net10.0`) provides simulated Essentials services, so they also work in web hosts and bUnit.
- **One preview per document.** The MAUI app shows a native scenario picker and one full-document preview. Navigation between previews uses a forced reload, so every preview gets a new DI scope and session. The session and culture are process-wide (`ScenarioSessionContext.UseProcessWideSession`), as in WebAssembly. The culture is restored when the next scenario declares none, because the process keeps running.
- **Generated host page.** `WorkbenchBlazorWebView` overrides `CreateFileProvider` to serve `workbench.html` and `/_workbench/axe.min.js`. Apps need no `index.html`, and the bridge loads before Blazor exactly as in web previews.
- **Automation.** A minimal HTTP/1.1 server on `TcpListener` serves the read-only `/_workbench/api` endpoints. It needs no URL reservation (unlike `HttpListener`), works on every MAUI platform, binds loopback only and checks the `Host` header. The CLI enables it, and the WebView2 DevTools port, through environment variables. The runner connects with `ConnectOverCDP` and emulates each variant (metrics, media features, locale, time zone) through one DevTools session kept for the whole run. It reuses the app's single page, so loads are serialized.
- **Baselines.** Hybrid screenshots go to `{os}-hybrid`, because WebView2 renders text differently from Playwright's Chromium.

## Alternatives considered
- **Run the shell in the WebView with iframes.** Rejected: a second `blazor.webview.js` instance would replace the shell's page context.
- **Pass WebView2 options in `BlazorWebViewInitializing`.** The MAUI handler did not apply them in our tests (MAUI 10.0.120). Environment variables, which WebView2 reads when it creates its environment, worked reliably. The app sets them in-process when only `BLAZOR_WORKBENCH_CDP_PORT` is present.
- **Appium or WinAppDriver** for native automation. Rejected: too heavy, and they cannot inspect web content as precisely as the DevTools Protocol.

## Consequences
- All sample scenarios (76) run in the MAUI app on Windows: render, interactions and accessibility pass, and the visual results are reproducible on the same WebView2 version.
- WebView2 updates itself (Evergreen), so hybrid baselines can drift. CI treats hybrid visual checks as informational. A Fixed Version runtime makes them stable.
- No Playwright traces and no parallel workers for hybrid runs.
- The MAUI library is built for Android, iOS, Mac Catalyst and Windows, so packing requires the `maui` workload (CI packs on Windows). Linux CI builds the web projects through `BlazorWorkbench.Web.slnf`.
