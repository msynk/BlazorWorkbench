# Environment simulation

Scenarios can simulate the conditions a component depends on, without changing production code or production service registrations. Every simulation is scoped to one preview session: a browser tab, a CLI test page or a bUnit context.

## What is real and what is simulated

| Dependency | In previews | Notes |
| --- | --- | --- |
| Services registered by the host | Real | Overridable per scenario with `.Service(...)` / `.Services(...)`. |
| `HttpClient`, `IHttpClientFactory` | **Simulated** | Fake backend. Requests never leave the process. Unmatched requests return 404 and are reported. |
| `AuthenticationStateProvider` | **Simulated** | Anonymous unless the scenario declares a user. |
| `TimeProvider` | Real, or **simulated** with `.Clock(...)` | A singleton that follows the active session. |
| `NavigationManager` | Real, with interception | Navigations are prevented and reported as diagnostics. |
| `IJSRuntime` | Real | The browser runs your component's JavaScript. |
| Culture | Simulated per session | Applied to every circuit activity (events, rendering, JS interop callbacks). |
| Theme, direction, viewport | Simulated | Attributes on the preview document and iframe size. CLI runs also emulate `prefers-color-scheme`. |
| .NET MAUI Essentials (`IConnectivity`, `IGeolocation`, `IShare`, ...) | Real in a MAUI app, or **simulated** with `.Device(...)` | `BlazorWorkbench.Maui.Essentials`; works in every hosting model and in bUnit. See [device simulation](#device-simulation-net-maui-essentials). |

The **Environment** panel lists the source of each service for the current session (*Scenario override*, *Simulated*, *Host registration*).

## Fake HTTP backend

```csharp
s.Http(h =>
{
    h.Get("/api/weather?city=Tehran").RespondJson(forecast).Delay(TimeSpan.FromMilliseconds(150));
    h.Get("/api/weather").Times(1).Fail(HttpStatusCode.ServiceUnavailable);  // first call fails …
    h.Get("/api/weather").RespondJson(forecast);                             // … the retry succeeds
    h.Post("/api/orders").Respond(HttpStatusCode.Created, """{"id":7}""", "application/json");
    h.Delete("/api/orders/*").NetworkError();
    h.Get("/api/slow").Timeout();
    h.Any("/api/**").Hold("api");                      // waits for the gate, then responds
    h.Get("https://cdn.example.com/x.json").RespondText("{}", "application/json");
    h.Latency(TimeSpan.FromMilliseconds(50));           // default for rules without Delay
    h.Unmatched(HttpStatusCode.NotImplemented);         // default 404
});
```

Pattern syntax:

- Exact paths, `*` for one segment and `**` for any suffix. Paths are case-insensitive and a trailing slash is ignored.
- An optional query is matched as a subset: `?city=Tehran` matches `?city=Tehran&units=c`, and `?city=*` matches any value.
- An absolute URL matches its host and port as well.

Matching and responses:

- Rules are evaluated in order. Rules of the scenario come before rules from `set.Setup(...)`.
- `Times(n)` limits a rule to `n` matches. After that, matching continues with the next rules.
- Faults look like real failures: `NetworkError` and `Offline()` throw `HttpRequestException` with `HttpRequestError.ConnectionError`. `Timeout()` throws `TaskCanceledException` with an inner `TimeoutException`, as `HttpClient.Timeout` does.
- `.Handle((request, ctx) => ...)` produces a custom response in code. The sample's [FakeBackends](../samples/SampleWorkbench/Fakes/FakeBackends.cs) filters a product catalog by query string and validates a posted order.
- The injected `HttpClient` has the base address `https://workbench.invalid/`. The `.invalid` domain is reserved, so accidental real requests cannot resolve.
- Every request is recorded with its redacted URL, status, duration and matched rule.

## Gates: deterministic async

A gate is a named latch. Work that waits at a gate does **not** count as pending, so the scenario becomes *ready* while it is still loading. You can inspect it, take a screenshot of it and test it.

```csharp
// HTTP
s.Http(h => h.Get("/api/weather").Hold("weather").RespondJson(forecast));

// Fake services
public sealed class FakeProductService(ScenarioGates gates) : IProductService
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct)
    {
        await gates.WaitAsync("products", ct);
        return Products;
    }
}
s.Service<IProductService>(ctx => new FakeProductService(ctx.Gates));
```

You can release or fail a gate from the Environment panel, from an interaction step (`.ReleaseGate("weather")`, `.FailGate("weather")`) or from code (`ScenarioGates.Release/Fail`). Gates are latched: after they are released, later waits complete immediately. A failed gate on an HTTP rule becomes a network error.

## Readiness

A preview is **ready** when all of these are true:

1. The scenario host is attached and nothing is pending. Tracked work includes fake HTTP requests until the response body has been read; work held at a gate is not pending.
2. The DOM has been quiet for 150 ms.
3. Fonts are loaded and the images inside the preview root are complete.
4. The `ReadyWhen` selector matches, if one is declared.

The state appears on `<html data-workbench-state="loading|ready|error">` and on `window.__workbench`. Automation waits for it instead of using fixed delays. Work that Workbench cannot observe, such as your own timers or JavaScript, is covered only by the quiet window. Use `ReadyWhen` or gates for such components.

## Services

```csharp
s.Service<IProductService>(ctx => new FakeProductService(SampleData.Products))
 .Services((services, ctx) => services.AddScoped<ICart, MemoryCart>());
```

How it works (see [ADR 0003](adr/0003-scenario-service-overrides.md)):

- At startup, Workbench collects every service type that a scenario overrides and replaces its registration with a scoped factory. The factory asks the current `ScenarioSession`, and the host's original registration stays available as a keyed fallback.
- The service callbacks run on **every activation**, so each preview session gets fresh instances, and Workbench disposes them with the session.

Limitations:

- An overridden service becomes scoped. If a host *singleton* depends on it, the container rejects this at startup in Development. Override the consumer instead.
- A service that is resolved in a preview circuit before the scenario activates cannot be swapped. Workbench reports this case in Diagnostics.
- Keyed services and open generic types cannot be overridden (`WB1013`).

## Device simulation (.NET MAUI Essentials)

Components that use .NET MAUI Essentials interfaces can be previewed with a simulated device:

```csharp
s.Device(d => d.IPhone().Offline().Battery(0.12).LocationPermissionDenied());
```

`.Device(...)` replaces every supported Essentials service for the scenario. Each call a component makes (share, open a URI, read the location, ...) appears in Diagnostics with the source `Platform`. Web hosts register a default device with `builder.Services.AddSimulatedDevice()`. The full list of options is in the [Blazor Hybrid guide](hybrid.md#simulate-the-device-in-scenarios).

## Users and authorization

```csharp
s.Anonymous();
s.User("Ada Lovelace");
s.User("Grace", "Admin", "Auditor");
s.User(u => u.Name("Grace").Role("Admin").Claim("department", "Engineering").AuthenticationType("Cookies"));
```

`AuthorizeView`, `[CascadingParameter] Task<AuthenticationState>` and `IAuthorizationService` policies use the simulated principal. Register the policies your components use in the host with `builder.Services.AddAuthorizationCore(o => ...)`; the sample library ships them in `AddSampleComponents()`, which every sample host calls (see [WorkspaceSettingsScenarios](../samples/SampleWorkbench/Scenarios/AccountScenarios.cs)). The simulated principal exists only in the preview session and never authenticates real requests.

## Clock, randomness and culture

- `.Clock(instant)` freezes `TimeProvider` (backed by `FakeTimeProvider`). The Environment panel can advance it by 1 s, 1 min, 1 h or 1 day. Timers created through `TimeProvider` fire when the clock advances.
- `.Seed(n)` seeds `ScenarioContext.Random`. Without it, the seed is a stable hash of the scenario id.
- `.Culture("de-DE")` sets `CurrentCulture` and `CurrentUICulture` for the session. The toolbar can override the culture without changing the scenario. Direction follows the culture (`fa-IR` and `ar-SA` give `rtl`) unless `.Direction(...)` sets it.

## Themes and viewports

Themes are configured in the host:

```csharp
options.Themes.Clear();
options.Themes.Add(new ThemeDefinition("light"));
options.Themes.Add(new ThemeDefinition("dark", ColorScheme.Dark, CssClass: "theme-dark") { Stylesheets = ["css/dark.css"] });
```

A theme sets `data-theme` and `color-scheme` on the preview `<html>`, adds its optional class and loads its stylesheets. Switching themes in the UI does not reload the preview. Unless the scenario, the URL or the toolbar picks a theme, the preview uses the theme matching the Workbench theme (the first theme with the same color scheme), and the Workbench theme follows the operating system until you toggle it. Media-query based dark mode (`prefers-color-scheme`) is emulated only in automated runs; browsers cannot emulate it inside an iframe.

The viewport presets are `mobile` (375×667), `tablet` (768×1024), `laptop` (1280×800) and `desktop` (1440×900). You can add your own with `options.Viewports.Add(new ViewportPreset("wide", 1920, 1080))`. In automated runs, presets with `IsMobile` (such as `mobile`) emulate a phone: a mobile viewport and touch input, so `(hover: none)`, `(pointer: coarse)` and touch events apply. Firefox gets touch input only, because Playwright cannot emulate a mobile viewport there.

## Cleanup and isolation

- Each preview tab, test page or bUnit context is one session, and a session is activated once. **Reset** in the UI loads a new session.
- When the session ends, Workbench cancels held and in-flight fake requests and disposes the services the scenario created. Instances that you supply with `AddSingleton(instance)` belong to you, as with Microsoft DI.
- Simulations never change the host's own registrations for other sessions.
