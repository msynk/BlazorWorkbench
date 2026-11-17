namespace BlazorWorkbench.Hosting;

/// <summary>How the Workbench UI and previews run in the browser.</summary>
public enum WorkbenchInteractivity
{
    /// <summary>Interactive Server: one circuit per preview (default).</summary>
    Server,

    /// <summary>Interactive WebAssembly: the UI and previews run in the browser from a client project that calls <c>AddBlazorWorkbenchAsync</c>.</summary>
    WebAssembly,
}

/// <summary>Options of the ASP.NET Core preview host. Secure by default: development environment and loopback clients only.</summary>
public sealed class WorkbenchHostOptions : WorkbenchOptions
{
    /// <summary>
    /// Accept connections from other machines. Requires <see cref="RemoteAccessPolicy"/> (an authorization policy applied to
    /// every Workbench endpoint) unless <see cref="AllowAnonymousRemoteAccess"/> is explicitly enabled.
    /// </summary>
    public bool AllowRemoteAccess { get; set; }

    /// <summary>Render mode of the UI and previews. WebAssembly needs a client project; see docs/webassembly.md.</summary>
    public WorkbenchInteractivity Interactivity { get; set; } = WorkbenchInteractivity.Server;

    /// <summary>Authorization policy required for every Workbench endpoint when remote access is enabled.</summary>
    public string? RemoteAccessPolicy { get; set; }

    /// <summary>Allows unauthenticated remote access. Only for isolated networks; previews may expose fixture data.</summary>
    public bool AllowAnonymousRemoteAccess { get; set; }

    /// <summary>Run outside the Development environment (for example a shared, protected catalog server).</summary>
    public bool AllowNonDevelopmentEnvironments { get; set; }

    /// <summary>Stylesheets loaded into every preview document (for example <c>css/app.css</c>). The host's scoped CSS bundle is added automatically when it exists.</summary>
    public IList<string> PreviewStylesheets { get; } = [];

    /// <summary>Scripts loaded into every preview document after Blazor (for example a component library's JS).</summary>
    public IList<string> PreviewScripts { get; } = [];

    /// <summary>Visual baseline directory. Defaults to the value in <c>workbench.json</c>.</summary>
    public string? BaselineDirectory { get; set; }

    /// <summary>
    /// The impact at which the explorer's test runner fails an accessibility scan (<c>minor</c>, <c>moderate</c>,
    /// <c>serious</c>, <c>critical</c>); defaults to <c>accessibility.failOn</c> in <c>workbench.json</c>, else <c>serious</c>.
    /// </summary>
    public string? AccessibilityFailOn { get; set; }

    /// <summary>axe-core rule tags the explorer's test runner uses; defaults to <c>accessibility.tags</c> in <c>workbench.json</c>.</summary>
    public IList<string> AccessibilityTags { get; } = [];

    /// <summary>Directory containing <c>report.json</c> from the last <c>workbench test</c> run. Defaults to <c>workbench.json</c>.</summary>
    public string? ResultsDirectory { get; set; }

    /// <summary>Root used to turn absolute source paths into relative ones. Defaults to the <c>workbench.json</c> directory or the content root.</summary>
    public string? SourceRoot { get; set; }

    /// <summary>
    /// Marks scenarios affected by changes in the git working copy in the explorer (<c>is:changed</c>, <c>is:new</c>,
    /// <c>is:related</c>). On by default; the host runs <c>git status</c> in <see cref="SourceRoot"/>.
    /// </summary>
    public bool ShowChanges { get; set; } = true;

    /// <summary>
    /// A git ref to compare with in addition to uncommitted changes, such as <c>origin/main</c>, so the explorer also marks
    /// what the current branch changed (like a pull request). Null compares with the last commit only.
    /// </summary>
    public string? ChangesBase { get; set; }

    /// <summary>
    /// Lets the Visual panel approve screenshots from the last run as baselines (like <c>workbench approve</c>). On by
    /// default for local hosts; a host with <see cref="AllowRemoteAccess"/> needs it set explicitly.
    /// </summary>
    public bool? AllowApprovalFromUi { get; set; }

    /// <summary>True when the Visual panel may approve baselines.</summary>
    public bool CanApproveFromUi => AllowApprovalFromUi ?? !AllowRemoteAccess;
}
