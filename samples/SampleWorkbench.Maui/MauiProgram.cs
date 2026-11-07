using BlazorWorkbench.Maui;
using SampleComponents;
using SampleComponents.Inputs;
using SampleWorkbench.Editors;

namespace SampleWorkbench.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // A dedicated Workbench app: one window with a scenario picker and a preview BlazorWebView. Components that use
        // .NET MAUI Essentials get the real platform services unless a scenario simulates them with .Device(...).
        builder.UseBlazorWorkbenchApp(options =>
        {
            options.Title = "Sample Components (MAUI Blazor Hybrid)";
            options.AddAssemblyOf<Button>();
            options.PreviewStylesheets.Add("_content/SampleComponents/sample.css");
            options.AddParameterEditor<HexColor, HexColorEditor>();
        });

        return builder.Build();
    }
}
