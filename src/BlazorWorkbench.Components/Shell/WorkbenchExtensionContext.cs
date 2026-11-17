using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Components.Shell;

/// <summary>
/// What a panel or toolbar item of your own (<see cref="WorkbenchOptions.AddPanel{TPanel}"/>,
/// <see cref="WorkbenchOptions.AddToolbarItem{TItem}"/>) can see and do: the open scenario, the preview's state, its arguments
/// and diagnostics, and a few actions on the preview. Declare a parameter <c>[Parameter] public WorkbenchExtensionContext Context { get; set; }</c>.
/// The shell passes the same instance on every render and re-renders the extension when anything here changes.
/// </summary>
public sealed class WorkbenchExtensionContext
{
    internal Func<IReadOnlyDictionary<string, ArgOverride>, Task> SetArgsHandler { get; set; } = _ => Task.CompletedTask;
    internal Func<Task> ReloadHandler { get; set; } = () => Task.CompletedTask;
    internal Func<string, string?, Task> HighlightHandler { get; set; } = (_, _) => Task.CompletedTask;
    internal Func<string, TimeSpan, CancellationToken, Task<JsonElement>> EvaluateHandler { get; set; } =
        (_, _, _) => Task.FromException<JsonElement>(new InvalidOperationException("The preview is not open."));

    /// <summary>The open scenario.</summary>
    public ScenarioDefinition Scenario { get; internal set; } = default!;

    /// <summary>The scenario's component, with its parameters and documentation.</summary>
    public ComponentEntry Component { get; internal set; } = default!;

    /// <summary>The Workbench options (themes, viewports, globals, your own settings in derived options).</summary>
    public WorkbenchOptions Options { get; internal set; } = default!;

    /// <summary>The Controls panel's current arguments (the overrides of the scenario's values), by parameter name.</summary>
    public IReadOnlyDictionary<string, ArgOverride> Args { get; internal set; } = new Dictionary<string, ArgOverride>();

    /// <summary>The preview's diagnostics so far, oldest first.</summary>
    public IReadOnlyList<DiagnosticDto> Diagnostics { get; internal set; } = [];

    /// <summary><c>loading</c>, <c>ready</c> or <c>error</c>.</summary>
    public string PreviewState { get; internal set; } = "loading";

    /// <summary>True in Canvas mode, where one live preview runs; false in Compare and Gallery modes.</summary>
    public bool HasPreview { get; internal set; }

    /// <summary>
    /// Increases every time the preview becomes ready (a new scenario, a reload, a change of user or culture). Compare it in
    /// <c>OnParametersSetAsync</c> to refresh what your panel read from the preview.
    /// </summary>
    public int PreviewVersion { get; internal set; }

    /// <summary>Replaces the Controls panel's arguments; the preview applies them without reloading.</summary>
    public Task SetArgsAsync(IReadOnlyDictionary<string, ArgOverride> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return SetArgsHandler(args);
    }

    /// <summary>Reloads the preview with a new session, keeping the arguments.</summary>
    public Task ReloadAsync() => ReloadHandler();

    /// <summary>Outlines the elements matching <paramref name="cssSelector"/> in the preview for a few seconds.</summary>
    public Task HighlightAsync(string cssSelector, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cssSelector);
        return HighlightHandler(cssSelector, label);
    }

    /// <summary>
    /// Runs JavaScript in the preview document and returns its result as JSON. <paramref name="script"/> is the body of an
    /// async function with two variables: <c>root</c> (the preview root element) and <c>workbench</c> (the preview automation
    /// API). For example <c>return getComputedStyle(root.firstElementChild).borderRadius;</c>. The result must be
    /// JSON-serializable; DOM elements are not.
    /// </summary>
    /// <exception cref="InvalidOperationException">The preview is not open (Compare or Gallery mode), or the script threw.</exception>
    /// <exception cref="TimeoutException">No answer within <paramref name="timeout"/> (default 10 seconds).</exception>
    public Task<JsonElement> EvaluateAsync(string script, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return EvaluateHandler(script, timeout ?? TimeSpan.FromSeconds(10), cancellationToken);
    }
}
