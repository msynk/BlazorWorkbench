using BlazorWorkbench.Hosting;
using BlazorWorkbench.Demo.Wasm.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddBlazorWorkbench(options =>
{
    options.Interactivity = WorkbenchInteractivity.WebAssembly;
    DemoSetup.Configure(options);
    options.PreviewStylesheets.Add("_content/BlazorWorkbench.Demo.Components/demo.css");
});

var app = builder.Build();
app.UseWebAssemblyDebugging();
app.MapBlazorWorkbench();
app.Run();
