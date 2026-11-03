using BlazorWorkbench.Catalog;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Components.Shell;

public sealed record A11yNode(string Target, string Html, string? FailureSummary);

public sealed record A11yRule(string Id, string? Impact, string Help, string? Description, string? HelpUrl, string[]? Tags, A11yNode[]? Nodes);

public sealed record A11yOutcome(string? Engine, A11yRule[]? Violations, A11yRule[]? Incomplete, int Passes, string? Error);

public sealed record StepResult(int Index, string Description, string Status, string? Error, double? DurationMs);

public sealed record InteractionOutcome(string Name, bool Passed, StepResult[] Results);

public sealed record PreviewMetrics(double? ReadyMs, int DomUpdates, int Pending);

public sealed record GateInfo(string Name, GateStatus Status, int Waiters, string? FailureMessage);

/// <summary>Visual comparison data shown in the Visual panel (provided by the host from the last test run).</summary>
public sealed record VisualComparisonView(
    string Variant,
    string Status,
    string? BaselineUrl,
    string? ActualUrl,
    string? DiffUrl,
    double? DiffRatio,
    string? Message,
    DateTimeOffset? RunAt);

/// <summary>Supplies stored visual test results to the UI. Implemented by the preview host; optional.</summary>
public interface IVisualResultsProvider
{
    Task<IReadOnlyList<VisualComparisonView>> GetAsync(string scenarioId, CancellationToken cancellationToken = default);

    /// <summary>Human readable description of where baselines and results are read from.</summary>
    string Describe();
}

/// <summary>
/// Base class for custom parameter editors registered with <see cref="WorkbenchOptions.AddParameterEditor{TValue, TEditor}"/>.
/// Values must be JSON serializable because they travel to the preview document.
/// </summary>
public abstract class ParameterEditorBase<TValue> : ComponentBase
{
    [Parameter] public ParameterDescriptor Parameter { get; set; } = default!;
    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }

    protected Task SetValueAsync(TValue? value) => ValueChanged.InvokeAsync(value);
}
