using BlazorWorkbench.Maui;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo;

namespace BlazorWorkbench.Demo.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // What the components need in any app: authorization policies and a default feature flag service.
        builder.Services.AddDemoComponents();

        // A dedicated Workbench app: one window with a scenario picker and a preview BlazorWebView. Components that use
        // .NET MAUI Essentials get the real platform services unless a scenario simulates them with .Device(...).
        builder.UseBlazorWorkbenchApp(options =>
        {
            options.Title = "Demo Components (MAUI Blazor Hybrid)";
            DemoCatalog.Configure(options);
            options.PreviewStylesheets.Add("_content/BlazorWorkbench.Demo.Components/demo.css");
        });

        return builder.Build();
    }
}
