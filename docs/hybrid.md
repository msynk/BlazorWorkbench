# .NET MAUI Blazor Hybrid

Blazor Workbench can run your scenarios inside a .NET MAUI app, in the platform's own WebView, next to real platform services. You can use it to:

- check components in the WebView your users get (WebView2 on Windows, Android System WebView, WKWebView on iOS and Mac Catalyst), with the app's real fonts, scaling and platform services;
- preview and test components that use .NET MAUI Essentials (`IConnectivity`, `IGeolocation`, `IShare`, ...) with simulated devices. The simulations also work in the web hosts and in bUnit;
- run `workbench test` against the MAUI app on Windows: render health, interaction tests, axe-core scans and visual comparisons, with the same scenarios as your web host.

The full Workbench UI (explorer, Controls, Environment and Diagnostics panels) stays in the web hosts. The MAUI app shows one preview at a time, with a native scenario picker.

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `BlazorWorkbench.Maui` | `net10.0-android`, `-ios`, `-maccatalyst`, `-windows10.0.19041.0` | `UseBlazorWorkbenchApp` / `UseBlazorWorkbench`, `WorkbenchPage`, `WorkbenchBlazorWebView`, registration of MAUI Essentials services. |
| `BlazorWorkbench.Hybrid` | `net10.0` | Platform-neutral BlazorWebView support: catalog and runtime registration, the generated host page, the axe-core script, and the loopback automation endpoint. |
| `BlazorWorkbench.Maui.Essentials` | `net10.0` | Simulated MAUI Essentials services and the `.Device(...)` scenario extension. Works in every hosting model. |

## Create a Workbench app

A Workbench app is a small MAUI project next to your component library. It references the library and the project (or folder) that holds your scenarios.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFrameworks>net10.0-windows10.0.19041.0;net10.0-android</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <EnableDefaultCssItems>false</EnableDefaultCssItems>
    <ApplicationId>com.contoso.workbench</ApplicationId>
    <!-- Unpackaged on Windows, so 'workbench test' can start the app with 'dotnet run'. -->
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.120" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="10.0.120" />
    <PackageReference Include="BlazorWorkbench.Maui" Version="0.1.0" />
    <PackageReference Include="BlazorWorkbench.Maui.Essentials" Version="0.1.0" />
    <ProjectReference Include="..\Contoso.Components\Contoso.Components.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Reuse the scenarios of the web Workbench host. -->
    <Compile Include="..\Contoso.Workbench\Scenarios\*.cs" Link="Scenarios\%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Keep the `Platforms` folder of the `maui-blazor` template (for example `Platforms/Windows/App.xaml`). You do not need `wwwroot/index.html`, `MainPage.xaml` or `Routes.razor`: Workbench generates the host page and provides the page and the router.

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseBlazorWorkbenchApp(options =>
    {
        options.Title = "Contoso components";
        options.AddAssemblyOf<Contoso.Components.Button>();
        options.PreviewStylesheets.Add("_content/Contoso.Components/theme.css");
    });

    // Your own services, as in your app. Scenarios can replace them.
    builder.Services.AddSingleton<IProductService, ProductService>();
    return builder.Build();
}
```

`UseBlazorWorkbenchApp` sets the application (one window with `WorkbenchPage`) and calls `UseBlazorWorkbench`. Use `UseBlazorWorkbench` alone to keep your own `Application` and show `WorkbenchPage` from it, for example on a debug-only route. It also:

- scans the calling assembly for scenario sets and providers;
- registers the BlazorWebView and its developer tools, so you can inspect previews (F12 on Windows);
- registers the platform implementations of common MAUI Essentials interfaces (`IConnectivity`, `IDeviceInfo`, `IGeolocation`, `IShare`, `ILauncher`, `IPreferences`, ...), unless your app already registered them. Pass `registerEssentials: false` to skip this;
- rewrites overridable registrations when the container is built, the same way the web hosts do ([ADR 0003](adr/0003-scenario-service-overrides.md)).

The repository sample is [samples/SampleWorkbench.Maui](../samples/SampleWorkbench.Maui). It reuses every scenario of the Interactive Server sample.

## Simulate the device in scenarios

Components that inject MAUI Essentials interfaces get the real platform services in the MAUI app. Use `.Device(...)` from `BlazorWorkbench.Maui.Essentials` to replace all of them with simulations for a scenario:

```csharp
public sealed class LocationCardScenarios : ScenarioSet<LocationCard>
{
    protected override void Configure(ScenarioSetBuilder<LocationCard> set)
    {
        set.Add("Located", s => s.Device(d => d.Location(48.8584, 2.2945, accuracyMeters: 12)));
        set.Add("Permission denied", s => s.Device(d => d.LocationPermissionDenied()));
        set.Add("Location off", s => s.Device(d => d.LocationDisabled()));
        set.Add("Offline phone", s => s.Device(d => d.AndroidPhone().Offline().Battery(0.12)));
    }
}
```

| Builder method | Simulates |
| --- | --- |
| `AndroidPhone()`, `IPhone()`, `IPad()`, `WindowsDesktop()`, `Mac()`, `Platform(...)`, `Model(...)`, `Physical()` | `IDeviceInfo` |
| `Online(profile)`, `Offline()`, `Network(access, profiles)` | `IConnectivity` (call `FakeConnectivity.Set(...)` to change it later) |
| `Location(lat, lon, accuracy)`, `LocationPermissionDenied()`, `LocationDisabled()`, `Location(LocationAvailability.NotSupported)` | `IGeolocation` (throws `PermissionException`, `FeatureNotEnabledException` or `FeatureNotSupportedException`) |
| `Battery(level, state, source, energySaver)` | `IBattery` |
| `ShareUnsupported()` | `IShare` (otherwise requests succeed and are recorded) |
| `LaunchableSchemes(...)` | `ILauncher` |
| `Clipboard(text)` | `IClipboard` |
| `Preference(key, value)`, `SecureValue(key, value)` | `IPreferences`, `ISecureStorage` |
| `App(name, version, build)`, `RequestedTheme(theme)` | `IAppInfo` |
| `NoVibration()` | `IVibration`, `IHapticFeedback` |

Every request a component makes, such as sharing text, opening a URI or reading the location, is written to the scenario's diagnostics with the source `Platform`. You can see it in the Diagnostics panel and in `diagnostics.jsonl`. Values from secure storage are never logged.

### The same scenarios in web hosts and bUnit

MAUI Essentials has no implementation in a browser or on a server, but its interfaces (the `net10.0` assembly of `Microsoft.Maui.Essentials`) work everywhere. Register a default simulated device in the web host, so components work even in scenarios without `.Device(...)`:

```csharp
builder.Services.AddSimulatedDevice();          // Interactive Server host, or the WebAssembly client
builder.AddBlazorWorkbench(...);
```

In bUnit, `RenderScenario` applies `.Device(...)` like any other scenario service:

```csharp
var cut = context.RenderScenario<ShareCardScenarios>("Sharing unavailable");
cut.Find("button").Click();
Assert.Equal("Sharing is not available on this device.", cut.Find("[role=status]").TextContent);
```

## Run it

```bash
dotnet run --project Contoso.Workbench.Maui -f net10.0-windows10.0.19041.0
dotnet build Contoso.Workbench.Maui -t:Run -f net10.0-android     # emulator or device
```

The app opens at the scenario list (`/_workbench/scenarios`). Pick a scenario in the native picker or in the list, change the theme, or press **Reload**. Every preview is a full document load: the WebView creates a new DI scope, so every preview gets a fresh scenario session.

## Automated tests (Windows)

`workbench test` starts the app and drives its WebView2 through the Chrome DevTools Protocol:

```bash
workbench test -p Contoso.Workbench.Maui/Contoso.Workbench.Maui.csproj --a11y --visual
workbench test -p Contoso.Workbench.Maui/Contoso.Workbench.Maui.csproj --framework net10.0-windows10.0.19041.0   # multi-targeted apps
```

You can also set `"framework"` in `workbench.json`. `list`, `validate`, `approve`, `publish` and the MCP server work too.

How a run works:

1. The CLI picks two free loopback ports and starts the app with `dotnet run`. The environment variables `BLAZOR_WORKBENCH_AUTOMATION_URL`, `BLAZOR_WORKBENCH_CDP_PORT`, `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>` and `WEBVIEW2_USER_DATA_FOLDER` (a private temporary folder) are set for it. When `BLAZOR_WORKBENCH_CDP_PORT` is already set, the CLI uses that port instead of a free one (see [Elevated processes and CI runners](#elevated-processes-and-ci-runners)).
2. The app serves the read-only automation API (`/_workbench/api/health`, `catalog`, `validate`, `scenarios/{id}`) on the first port. Health reports `hostingModel: "Hybrid"`, the app origin (`https://0.0.0.1/`) and the DevTools endpoint, and returns 503 until the WebView exists.
3. The runner connects Playwright to the WebView (`ConnectOverCDP`) and loads each preview at `https://0.0.0.1/_workbench/preview/{id}`. Before each load it sets the viewport, device scale factor, color scheme, reduced motion, locale and time zone of the variant through a DevTools session.
4. When the run ends, the CLI stops the app and deletes the temporary WebView data.

Differences from web hosts:

| | Web hosts | Blazor Hybrid |
| --- | --- | --- |
| Parallelism | `--workers` browser contexts | One page; scenarios run one after another (`workers` is reported as 1). |
| Browser | Playwright's Chromium, Firefox or WebKit | The app's WebView2 (`browser: webview` and the WebView2 version in reports). `--browser` and `--headed` are ignored; the app window is visible. |
| Traces | Recorded for failing interactions | Not recorded. Failure screenshots and diagnostics are. |
| Server metrics | Interactive Server only | Not applicable. |
| Baselines | `{os}`, `{os}-webassembly` | `{os}-hybrid` (for example `windows-hybrid`) |

Android, iOS and Mac Catalyst run the same app for manual review. Automated runs need Windows: Playwright cannot drive Android WebView or WKWebView through a supported API.

### Elevated processes and CI runners

WebView2 Runtime 150 and later ignore the `WEBVIEW2_*` environment variables (and per-user policies) when the app runs elevated ([WebView2Feedback#5640](https://github.com/MicrosoftEdge/WebView2Feedback/issues/5640)). GitHub-hosted Windows runners run everything as administrator, so the WebView opens no DevTools port there and runs fail with `Could not connect to the WebView ... ECONNREFUSED`. .NET MAUI does not pass `BlazorWebViewInitializing` environment options to the WebView (MAUI 10), so the app cannot pass the port through the WebView2 API either.

Elevated apps still honor the machine-wide policy. Set it for the app's executable with a fixed port, and set `BLAZOR_WORKBENCH_CDP_PORT` to the same port so the CLI and the app use it:

```powershell
reg add "HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments" /v Contoso.Workbench.Maui.exe /t REG_SZ /d "--remote-debugging-port=9333" /f
"BLAZOR_WORKBENCH_CDP_PORT=9333" >> $env:GITHUB_ENV   # GitHub Actions; elsewhere set the variable for the test process
```

The port is fixed, so run one hybrid app at a time on such a machine. The repository's CI does this (`.github/workflows/ci.yml`). `WEBVIEW2_USER_DATA_FOLDER` is still applied.

## Visual baselines

Hybrid screenshots are stored under `{baselines}/windows-hybrid/`. Approve them like other baselines (`workbench approve`).

WebView2 updates itself (the Evergreen runtime), and a new version can change text rendering. Web host baselines do not have this problem, because Playwright pins its Chromium. To keep hybrid baselines stable:

- use a [Fixed Version WebView2 runtime](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution#the-fixed-version-runtime-distribution-mode) on the machines that run visual checks, and set `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` to it; or
- treat hybrid visual checks as informational in CI (the repository does this) and run the gating visual checks on the web hosts.

Reports record the WebView2 version (`environment.browserVersion`) of every run.

## How it works

- **Host page.** `WorkbenchBlazorWebView` serves a generated host page (`wwwroot/workbench.html`). The page loads the preview bridge first, then the app's scoped CSS bundle (when it exists), `PreviewStylesheets`, `_framework/blazor.webview.js` and `PreviewScripts`. The same file provider serves the unmodified axe-core script at `/_workbench/axe.min.js`. All other files come from the app's content (`wwwroot` and `_content/*`).
- **Sessions.** A BlazorWebView renders one document, and every document load creates a new DI scope. Workbench therefore treats the active scenario session as process-wide, as in WebAssembly: the session clock drives `TimeProvider`, and the scenario culture is applied to the process and restored when the next scenario has none.
- **Overrides.** The container rewrite is the same as in the web hosts ([ADR 0003](adr/0003-scenario-service-overrides.md)), so scenarios can replace app services and platform services alike.

See [ADR 0009](adr/0009-blazor-hybrid.md) for the design decisions.

## Security

- The automation API is read-only, answers GET requests only, listens on loopback, rejects requests whose `Host` header is not a loopback name (DNS rebinding), and sends no CORS headers. It starts only when `BLAZOR_WORKBENCH_AUTOMATION_URL` (or a `--urls` argument) is present.
- The WebView2 DevTools port is opened only when `BLAZOR_WORKBENCH_CDP_PORT` is set. The CLI sets it for the app process it starts, and the port listens on loopback. While a run is in progress, local processes can connect to it, as with any Chromium DevTools port.
- A Workbench app is a development tool. Do not ship `UseBlazorWorkbench` in a production app. If you add `WorkbenchPage` to your own app, add it in debug builds only.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `The Blazor Hybrid host exposes no WebView DevTools endpoint` | Start the app with `workbench test` (it sets the variables), or set `BLAZOR_WORKBENCH_CDP_PORT` and `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` yourself. Automated runs need Windows. |
| `Could not connect to the WebView` | If the run is elevated (for example on a GitHub-hosted runner), WebView2 ignored the `WEBVIEW2_*` variables: see [Elevated processes and CI runners](#elevated-processes-and-ci-runners). Otherwise another process already uses the port, or the WebView joined another instance's browser process. Close other instances of the app and use a private `WEBVIEW2_USER_DATA_FOLDER`. |
| `dotnet run` asks for a target framework | Pass `--framework` or set `"framework"` in `workbench.json`. |
| A component name clashes with a MAUI type (`Button`, `Size`, `Toolbar`) | Remove the MAUI global usings in the app project: `<Using Remove="Microsoft.Maui.Controls" />`. |
| `Conflicting assets with the same target path '_framework/blazor.modules.json'` | A class library references `Microsoft.AspNetCore.Components.WebView.Maui` with its build assets. Reference it with `ExcludeAssets="build;buildTransitive"` in libraries; only the app needs them. |
| Hybrid visual differences after a Windows update | WebView2 was updated. Review the differences and re-approve them, or pin a Fixed Version runtime. |
