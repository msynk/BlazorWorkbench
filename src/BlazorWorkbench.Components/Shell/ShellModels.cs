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

/// <summary>The outcome of one scenario in the last automated run (<c>workbench test</c>).</summary>
/// <param name="Status"><c>Passed</c>, <c>Failed</c>, <c>Skipped</c> or <c>Error</c>.</param>
/// <param name="Message">The first failure, if any.</param>
/// <param name="Flaky">True when the scenario passed only after a retry.</param>
public sealed record ScenarioRunStatus(string Status, string? Message, bool Flaky);

/// <summary>The last automated run as the explorer shows it: when it ran and the status of every scenario.</summary>
public sealed record TestRunSummary(DateTimeOffset? FinishedAt, IReadOnlyDictionary<string, ScenarioRunStatus> Scenarios)
{
    public static TestRunSummary Empty { get; } = new(null, new Dictionary<string, ScenarioRunStatus>());
}

/// <summary>Supplies the results of the last automated run to the explorer. Implemented by the preview host; optional.</summary>
public interface ITestResultsProvider
{
    Task<TestRunSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
