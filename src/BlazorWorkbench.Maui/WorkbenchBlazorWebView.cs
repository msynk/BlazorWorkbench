using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Hybrid;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace BlazorWorkbench.Maui;

/// <summary>
/// A BlazorWebView that renders Workbench pages: it serves a generated host page (no <c>index.html</c> needed), the
/// axe-core script, and starts at the scenario list. Each preview is a full document load, so every preview gets a new
/// DI scope and a fresh scenario session.
/// </summary>
public class WorkbenchBlazorWebView : BlazorWebView
{
    public WorkbenchBlazorWebView()
    {
        HostPage = "wwwroot/" + WorkbenchHybridHost.HostPageName;
        StartPath = WorkbenchHybridHost.StartPath;
        RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(WorkbenchRoutes) });
        RootComponents.Add(new RootComponent { Selector = "head::after", ComponentType = typeof(HeadOutlet) });
        BlazorWebViewInitialized += OnInitialized;
    }

    private WorkbenchHybridHost Host =>
        Handler?.MauiContext?.Services.GetService<WorkbenchHybridHost>()
        ?? throw new InvalidOperationException("Blazor Workbench is not registered. Call builder.UseBlazorWorkbench() in MauiProgram.");

    /// <inheritdoc />
    public override IFileProvider CreateFileProvider(string contentRootDir) =>
        new WorkbenchHybridFileProvider(base.CreateFileProvider(contentRootDir), Host);

    private void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs e) => Host.MarkWebViewReady();
}
