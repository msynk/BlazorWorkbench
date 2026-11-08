using BlazorWorkbench.Maui;
using SampleComponents;
using SampleWorkbench;

namespace SampleWorkbench.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // What the components need in any app: authorization policies and a default feature flag service.
        builder.Services.AddSampleComponents();

        // A dedicated Workbench app: one window with a scenario picker and a preview BlazorWebView. Components that use
        // .NET MAUI Essentials get the real platform services unless a scenario simulates them with .Device(...).
        builder.UseBlazorWorkbenchApp(options =>
        {
            options.Title = "Sample Components (MAUI Blazor Hybrid)";
            SampleCatalog.Configure(options);
            options.PreviewStylesheets.Add("_content/SampleComponents/sample.css");
        });

        return builder.Build();
    }
}
