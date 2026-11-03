using BlazorWorkbench.Hosting;
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

var app = builder.Build();
app.MapBlazorWorkbench();
app.Run();
