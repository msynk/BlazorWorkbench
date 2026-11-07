namespace BlazorWorkbench.Hybrid;

/// <summary>Options of a Blazor Hybrid (BlazorWebView) Workbench host.</summary>
public sealed class WorkbenchHybridOptions : WorkbenchOptions
{
    /// <summary>The environment variable that enables the automation endpoint (set by <c>workbench test</c>).</summary>
    public const string AutomationUrlVariable = "BLAZOR_WORKBENCH_AUTOMATION_URL";

    /// <summary>The environment variable carrying the WebView's Chrome DevTools Protocol port (set by <c>workbench test</c>).</summary>
    public const string DevToolsPortVariable = "BLAZOR_WORKBENCH_CDP_PORT";

    /// <summary>Stylesheets loaded into every preview document (for example <c>_content/MyLibrary/theme.css</c>). The app's scoped CSS bundle is added automatically when it exists.</summary>
    public IList<string> PreviewStylesheets { get; } = [];

    /// <summary>Scripts loaded into every preview document after Blazor (for example a component library's JS).</summary>
    public IList<string> PreviewScripts { get; } = [];

    /// <summary>
    /// The origin the BlazorWebView serves the app from. .NET MAUI uses <c>https://0.0.0.1/</c> on Windows and Android and
    /// <c>app://0.0.0.1/</c> on iOS and Mac Catalyst; the MAUI integration sets it for you.
    /// </summary>
    public Uri AppOrigin { get; set; } = new("https://0.0.0.1/");

    /// <summary>
    /// Loopback URL of the read-only automation endpoint (health, catalog, validation) used by <c>workbench test</c>.
    /// Defaults to the <see cref="AutomationUrlVariable"/> environment variable or a <c>--urls</c> command-line argument;
    /// when neither is present the endpoint is not started.
    /// </summary>
    public Uri? AutomationUrl { get; set; }

    /// <summary>Chrome DevTools Protocol port of the WebView (Windows/WebView2 only). Defaults to <see cref="DevToolsPortVariable"/>.</summary>
    public int? DevToolsPort { get; set; }

    /// <summary>Root used to turn absolute source paths into relative ones. Defaults to the directory of <c>workbench.json</c>.</summary>
    public string? SourceRoot { get; set; }

    /// <summary>Reads <see cref="AutomationUrl"/> and <see cref="DevToolsPort"/> from the environment and command line when they are not set.</summary>
    public void ApplyEnvironment(IReadOnlyList<string>? commandLine = null)
    {
        if (AutomationUrl is null)
        {
            var value = Environment.GetEnvironmentVariable(AutomationUrlVariable);
            var args = commandLine ?? Environment.GetCommandLineArgs();
            for (var i = 0; value is null && i < args.Count - 1; i++)
            {
                if (args[i] is "--urls" or "--automation-url")
                    value = args[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var url))
                AutomationUrl = url;
        }

        if (DevToolsPort is null && int.TryParse(Environment.GetEnvironmentVariable(DevToolsPortVariable), out var port) && port is > 0 and < 65536)
            DevToolsPort = port;
    }
}
