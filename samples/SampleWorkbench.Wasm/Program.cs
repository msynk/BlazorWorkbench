using BlazorWorkbench.Hosting;
using SampleWorkbench.Wasm.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddBlazorWorkbench(options =>
{
    options.Interactivity = WorkbenchInteractivity.WebAssembly;
    SampleSetup.Configure(options);
    options.PreviewStylesheets.Add("_content/SampleComponents/sample.css");
});

var app = builder.Build();
app.UseWebAssemblyDebugging();
app.MapBlazorWorkbench();
app.Run();
