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
    options.Title = "Demo Components";
    DemoCatalog.Configure(options);
    options.PreviewStylesheets.Add("_content/BlazorWorkbench.Demo.Components/demo.css");
});

var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
