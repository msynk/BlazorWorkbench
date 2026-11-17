using BlazorWorkbench.Hosting;
using BlazorWorkbench.Maui.Essentials;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo;

var builder = WebApplication.CreateBuilder(args);

// What the components need in any app: authorization policies and a default feature flag service.
builder.Services.AddDemoComponents();

// Components that use .NET MAUI Essentials get a simulated device in this web host; scenarios refine it with .Device(...).
builder.Services.AddSimulatedDevice();

builder.AddBlazorWorkbench(options =>
{
    DemoCatalog.Configure(options);
    options.PreviewStylesheets.Add("_content/BlazorWorkbench.Demo.Components/demo.css");
    // Another team's published catalog, listed in the explorer next to this one (wwwroot/catalogs/trail-icons is a stand-in).
    options.AddCatalogReference("Trail icons", "catalogs/trail-icons/");
});

var app = builder.Build();
// Hosting behind a reverse proxy under a path, for example Workbench__PathBase=/workbench (used by the end-to-end tests).
// UseRouting must follow UsePathBase, or endpoints are matched against the full path.
if (app.Configuration["Workbench:PathBase"] is { Length: > 1 } pathBase)
{
    app.UsePathBase(pathBase);
    app.UseRouting();
}
app.MapBlazorWorkbench();
app.Run();
