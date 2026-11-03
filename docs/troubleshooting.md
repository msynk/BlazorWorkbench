# Troubleshooting

### The preview stays at "Loading…" or tests report "Not ready after 30 s"
- Open **Diagnostics**. A `Resource` error for `_framework/blazor.web.js` means the host lacks the Blazor framework scripts. `BlazorWorkbench.Hosting` adds them through `Microsoft.AspNetCore.App.Internal.Assets`. If you use project references to Workbench sources, add `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` to the host or add one `.razor` file.
- A component that waits for something Workbench cannot observe (real timers, JavaScript, a third-party SDK) may never look idle. Add `.ReadyWhen("selector")`, or hold the work at a gate.
- `pending` > 0 in the timeout message means a fake request never completed. Check for an endless retry loop or a `Hold` gate that was never released.

### "X is not registered by the host and the active scenario does not provide it"
The component injects a service. Provide a fake with `.Service<IX>(ctx => new FakeX())` in the scenario (or in `set.Setup(...)`), or register a development implementation in the host **before** `AddBlazorWorkbench`. `workbench validate` reports these cases as `WB1005`.

### "Cannot consume scoped service … from singleton …" at startup
A host singleton depends on a service that a scenario overrides. Overridden services become scoped. Override the singleton consumer instead, or make it scoped.

### A scenario override does not apply
- The service was resolved before the scenario activated, and the Diagnostics panel shows a warning. This happens when circuit infrastructure resolves the service. Move the dependency into the component.
- You added the override while `dotnet watch` was running and the type was not overridable at startup. Restart the host.

### Visual checks fail on another machine
Baselines depend on the platform, the browser version and the installed fonts. Compare on the same OS and Playwright version, or approve per platform (`baselines/linux`, …). Use fixed clocks and seeded fixtures for dynamic content; `.Visual(v => v.Ignore("..."))` is the last resort.

### `Executable doesn't exist` / browser could not be launched
Run `workbench install-browsers`, and add `--with-deps` on Linux CI agents.

### Accessibility scan shows "axe-core could not be loaded"
The host serves `/_workbench/axe.min.js` from the `Deque.AxeCore.Commons` package. Make sure the package is restored and that no proxy blocks the path.

### `403 Blazor Workbench accepts local connections only`
Workbench accepts loopback clients by default. For shared access, see [security](security.md#remote-access).

### The host refuses to start outside Development
This is intentional. Set `ASPNETCORE_ENVIRONMENT=Development` locally, or set `AllowNonDevelopmentEnvironments = true` for a protected catalog server.

### Culture does not apply
Use predefined culture names (`fa-IR`, not `fa`). Code that runs outside circuit activities does not get the scenario culture; see [compatibility](compatibility.md#known-limitations).

### Interaction passes in the UI but fails in the CLI (or the other way round)
The CLI (Playwright) is authoritative. Typical causes: a locator matches several elements (strict mode), an element is covered or hidden, or keyboard default actions differ. Open the trace from `artifacts/workbench/traces` with `npx playwright show-trace`.

### NuGet cannot find `BlazorWorkbench.*` from a local folder
Check that the folder source is not disabled in your NuGet configuration (`disabledPackageSources`), and use a unique source key.
