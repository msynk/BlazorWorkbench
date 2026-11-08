# Interactive WebAssembly hosting

By default, Workbench previews run with Interactive Server: one circuit per preview. You can also run the Workbench UI and the previews **in the browser** with Interactive WebAssembly. Use it when your components depend on the WebAssembly runtime (`IJSInProcessRuntime`, `OperatingSystem.IsBrowser()`, WebAssembly-only libraries), or to verify that they behave the same in both hosting models.

## Setup

A WebAssembly host has two projects, like a Blazor Web App with WebAssembly interactivity:

```
MyComponents.Workbench/          Sdk.Web: serves the app, catalog API, axe-core, baselines
MyComponents.Workbench.Client/   Sdk.BlazorWebAssembly: Workbench UI, previews and scenarios
```

**Client** (`MyComponents.Workbench.Client.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PublishTrimmed>false</PublishTrimmed>   <!-- discovery uses reflection -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BlazorWorkbench.WebAssembly" Version="0.1.0" />
    <ProjectReference Include="..\MyComponents\MyComponents.csproj" />
  </ItemGroup>
</Project>
```

```csharp
// Program.cs
var builder = WebAssemblyHostBuilder.CreateDefault(args);
// Register client services first.
await builder.AddBlazorWorkbenchAsync(WorkbenchSetup.Configure);
await builder.Build().RunAsync();

// WorkbenchSetup.cs: shared by client and host
public static class WorkbenchSetup
{
    public static void Configure(WorkbenchOptions options)
    {
        options.AddAssemblyOf<MyComponents.Button>();
        options.ScenarioAssemblies.Add(typeof(WorkbenchSetup).Assembly);   // scenarios live in the client
    }
}
```

**Host** (`MyComponents.Workbench.csproj`) references `BlazorWorkbench.Hosting` and the client project:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddBlazorWorkbench(options =>
{
    options.Interactivity = WorkbenchInteractivity.WebAssembly;
    WorkbenchSetup.Configure(options);
    options.PreviewStylesheets.Add("_content/MyComponents/theme.css");
});
var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
```

To debug the WebAssembly code, reference `Microsoft.AspNetCore.Components.WebAssembly.Server` in the host and call `app.UseWebAssemblyDebugging()`.

The repository contains a complete example: [BlazorWorkbench.Demo.Wasm](../demo/BlazorWorkbench.Demo.Wasm) and [BlazorWorkbench.Demo.Wasm.Client](../demo/BlazorWorkbench.Demo.Wasm.Client). It reuses every scenario of the Interactive Server demo.

## What runs where

| Concern | Interactive WebAssembly |
| --- | --- |
| Catalog for the UI | Built in the browser at startup. XML documentation is fetched from the host (`/_workbench/api/xmldoc/{assembly}`, limited to the configured assemblies). |
| Catalog for the CLI and MCP | Built by the host from the same assemblies (the host references the client), and reports `hostingModel: "WebAssembly"`. |
| Scenario services | Rewritten in the client container (`ConfigureContainer`). One preview document is one session. |
| Fake HTTP, gates, auth, clock | In the browser; same behavior as Server. |
| Culture | Applied process-wide in the preview document (WebAssembly is single-threaded). |
| Visual results panel | Read from the host through `/_workbench/api/visual/{id}`. |
| Dependency validation (`WB1005`) | Runs in the client against the client's registrations. The host-side `validate` command does not see client-only registrations and skips service checks it cannot make. |

## Differences to expect

Running the same 63 demo scenarios on both hosting models found three visual differences. All three are real platform behavior:

- **Exception messages.** The WebAssembly runtime can use resource keys instead of full messages, so error texts that show `ex.Message` differ.
- **Globalization data.** WebAssembly ships reduced ICU data, so some cultures (for example `fa-IR` dates) format differently.
- **Start-up.** Every preview document boots the .NET WebAssembly runtime. Readiness takes longer (about 1–2 s per preview after the first load), so CI runs are slower.

For this reason, visual baselines are stored per hosting model: `baselines/windows` and `baselines/windows-webassembly`, and likewise for Linux and macOS. See [visual regression](visual-regression.md).

## Limitations

- Publishing a trimmed (Release) WebAssembly client removes types that discovery needs. Workbench clients are development tools; keep `PublishTrimmed=false`.
- Hot reload follows WebAssembly hot reload support in `dotnet watch`.
- Server metrics (`serverActivities`, `serverMs`) are Server-only. WebAssembly runs have `readyMs` and `domUpdates`.
