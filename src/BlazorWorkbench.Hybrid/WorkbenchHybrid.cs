using System.Net;
using System.Reflection;
using System.Text;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Hybrid;

/// <summary>Registers Blazor Workbench in a Blazor Hybrid (BlazorWebView) app.</summary>
public static class WorkbenchHybridServiceCollectionExtensions
{
    /// <summary>
    /// Builds the catalog and registers the preview runtime for a BlazorWebView host. One preview document runs at a time,
    /// so the scenario session (clock, culture) applies process-wide. Returns the host, whose
    /// <see cref="WorkbenchHybridHost.OverridableTypes"/> must be passed to <see cref="ServiceOverrides.Apply"/> right before
    /// the container is built (the MAUI integration does this for you).
    /// </summary>
    public static WorkbenchHybridHost AddBlazorWorkbenchHybrid(this IServiceCollection services, WorkbenchHybridOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        ScenarioSessionContext.UseProcessWideSession = true;
        options.ApplyEnvironment();
        options.SourceRoot ??= WorkbenchConfig.Find(AppContext.BaseDirectory)?.RootDirectory;

        var catalog = CatalogBuilder.Build(options);
        var overridable = ServiceOverrides.CollectTypes(catalog).ToHashSet();
        var host = new WorkbenchHybridHost(options, overridable);
        services.AddSingleton(host);
        services.AddSingleton(options);
        services.AddBlazorWorkbenchRuntime(options, catalog, overridable);
        return host;
    }
}

/// <summary>State of a Blazor Hybrid Workbench host: options, overridable services, the host page and WebView readiness.</summary>
public sealed class WorkbenchHybridHost
{
    /// <summary>Path of the generated host page relative to the BlazorWebView content root (<c>wwwroot</c>).</summary>
    public const string HostPageName = "workbench.html";

    /// <summary>Route of the scenario list shown when the app starts.</summary>
    public const string StartPath = "/_workbench/scenarios";

    private readonly TaskCompletionSource _webViewReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal WorkbenchHybridHost(WorkbenchHybridOptions options, IReadOnlySet<Type> overridableTypes)
    {
        Options = options;
        OverridableTypes = overridableTypes;
    }

    public WorkbenchHybridOptions Options { get; }

    /// <summary>Service types rewritten by <see cref="ServiceOverrides.Apply"/>.</summary>
    public IReadOnlySet<Type> OverridableTypes { get; }

    /// <summary>True once the platform WebView exists (its DevTools endpoint accepts connections from then on).</summary>
    public bool IsWebViewReady => _webViewReady.Task.IsCompleted;

    /// <summary>Completes when the platform WebView has been created.</summary>
    public Task WebViewReady => _webViewReady.Task;

    /// <summary>Called by the platform integration when the WebView has been created.</summary>
    public void MarkWebViewReady() => _webViewReady.TrySetResult();

    /// <summary>The DevTools endpoint the runner connects to, when automation is enabled on a platform that supports it.</summary>
    public Uri? DevToolsEndpoint => Options.DevToolsPort is { } port ? new Uri($"http://127.0.0.1:{port}") : null;

    /// <summary>Workbench version reported to the runner.</summary>
    public static string Version { get; } =
        typeof(WorkbenchHybridHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// Builds the preview host page: the preview bridge loads first so console, error and network capture starts before Blazor,
    /// then the configured stylesheets, the BlazorWebView script and the configured scripts.
    /// </summary>
    /// <param name="stylesheetExists">Checks whether a file exists in the app's content root (used for the scoped CSS bundle).</param>
    public string BuildHostPage(Func<string, bool>? stylesheetExists = null)
    {
        var html = new StringBuilder();
        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no, viewport-fit=cover" />
                <base href="/" />
                <script src="_content/BlazorWorkbench.Components/workbench-preview.js"></script>
                <link rel="stylesheet" href="_content/BlazorWorkbench.Components/workbench-preview.css" />

            """);
        var entry = Assembly.GetEntryAssembly()?.GetName().Name;
        var bundle = entry is null ? null : $"{entry}.styles.css";
        if (bundle is not null && stylesheetExists?.Invoke(bundle) == true)
            html.Append("    <link rel=\"stylesheet\" href=\"").Append(Encode(bundle)).Append("\" />\n");
        foreach (var href in Options.PreviewStylesheets)
            html.Append("    <link rel=\"stylesheet\" href=\"").Append(Encode(href)).Append("\" />\n");
        html.Append("""
            </head>
            <body>
                <div id="app"></div>
                <div id="blazor-error-ui" hidden>
                    An unhandled error has occurred. <a href="" class="reload">Reload</a>
                </div>
                <script src="_framework/blazor.webview.js" autostart="false"></script>

            """);
        foreach (var src in Options.PreviewScripts)
            html.Append("    <script src=\"").Append(Encode(src)).Append("\"></script>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>The URL of a scenario preview inside the WebView.</summary>
    public Uri PreviewUrl(string scenarioId, string? theme = null) =>
        new(Options.AppOrigin, $"_workbench/preview/{Uri.EscapeDataString(scenarioId)}" + (theme is null ? "" : $"?theme={Uri.EscapeDataString(theme)}"));

    internal string? RelativeSource(string path)
    {
        if (Options.SourceRoot is not { } root)
            return Path.GetFileName(path);
        var relative = Path.GetRelativePath(root, path);
        // Never publish absolute or out-of-tree paths.
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : relative.Replace('\\', '/');
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
