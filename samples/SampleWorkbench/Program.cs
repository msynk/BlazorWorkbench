using BlazorWorkbench.Hosting;
using BlazorWorkbench.Maui.Essentials;
using SampleComponents;
using SampleComponents.Inputs;
using SampleWorkbench.Editors;

var builder = WebApplication.CreateBuilder(args);

builder.AddBlazorWorkbench(options =>
{
    options.Title = "Sample Components";
    options.AddAssemblyOf<Button>();
    options.PreviewStylesheets.Add("_content/SampleComponents/sample.css");
    options.AddParameterEditor<HexColor, HexColorEditor>();
});

// Components that use .NET MAUI Essentials get a simulated device in this web host; scenarios refine it with .Device(...).
builder.Services.AddSimulatedDevice();

var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
