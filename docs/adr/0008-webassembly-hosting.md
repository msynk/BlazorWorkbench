# ADR 0008: Interactive WebAssembly hosting and baselines per hosting model

- **Status**: Accepted
- **Date**: 2026-11-03

## Context
Some components depend on the WebAssembly runtime, and teams want to know whether components behave the same in both hosting models. The UI library (`BlazorWorkbench.Components`) already avoided server APIs, but three things were bound to Interactive Server: the culture, the session lookup used by `TimeProvider`, and the routes and documentation.

## Decision
- Add a `BlazorWorkbench.WebAssembly` client package with `AddBlazorWorkbenchAsync`. It builds the catalog in the browser, fetches XML documentation from the host, applies the same container rewrite (`WebAssemblyHostBuilder.ConfigureContainer`), and reads visual results over HTTP.
- `WorkbenchHostOptions.Interactivity = WebAssembly` switches the root render mode and adds the WebAssembly endpoints. Routes move into the RCL (`WorkbenchRoutes`) so that both models can render them.
- A preview document hosts exactly one session, so WebAssembly uses a process-wide session fallback (`ScenarioSessionContext.Browser`) and process-wide culture.
- The host catalog reports `hostingModel`. Baseline folders become `{os}` for Server and `{os}-webassembly` for WebAssembly.

## Consequences
- All 63 sample scenarios pass on both models. Three screenshots differ between them because of exception message resources and ICU data, which confirms that separate baselines are needed.
- `BlazorWorkbench.Hosting` depends on `Microsoft.AspNetCore.Components.WebAssembly.Server` (10.0.14 or later; earlier 10.0.x servicing releases have advisories). Its build and content assets (the debug proxy) are excluded.
- WebAssembly clients must not be trimmed.
