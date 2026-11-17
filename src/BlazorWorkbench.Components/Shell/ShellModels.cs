using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Components.Shell;

public sealed record A11yNode(string Target, string Html, string? FailureSummary);

public sealed record A11yRule(string Id, string? Impact, string Help, string? Description, string? HelpUrl, string[]? Tags, A11yNode[]? Nodes);

public sealed record A11yOutcome(string? Engine, A11yRule[]? Violations, A11yRule[]? Incomplete, int Passes, string? Error);

/// <summary>A heading of the preview, as assistive technology lists it.</summary>
/// <param name="Level">1 to 6 (<c>aria-level</c> wins over the tag).</param>
/// <param name="Text">The accessible name.</param>
/// <param name="Issue">A structural problem (skipped level, empty heading), or null.</param>
/// <param name="Index">Identifies the element for highlighting.</param>
public sealed record OutlineHeading(int Level, string Text, string? Issue, int Index);

/// <summary>A landmark of the preview (banner, navigation, main, region and so on).</summary>
public sealed record OutlineLandmark(string Role, string Name, int Depth, string? Issue, int Index);

/// <summary>The preview's headings and landmarks, in document order (the Accessibility panel's Outline).</summary>
public sealed record OutlineReport(OutlineHeading[] Headings, OutlineLandmark[] Landmarks)
{
    public int IssueCount => Headings.Count(h => h.Issue is not null) + Landmarks.Count(l => l.Issue is not null);
}

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

    /// <summary>True when this host lets the Visual panel approve screenshots as baselines.</summary>
    ValueTask<bool> CanApproveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

    /// <summary>
    /// Approves the screenshots of the scenario's failed or missing visual checks from the last run as baselines (only
    /// <paramref name="variant"/> when given), like <c>workbench approve --filter id</c>.
    /// </summary>
    Task<VisualApprovalResult> ApproveAsync(string scenarioId, string? variant = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VisualApprovalResult([], "Approving baselines is not available in this host; run workbench approve."));
}

/// <summary>The outcome of approving baselines from the Visual panel.</summary>
/// <param name="Approved">The approved baseline files, relative to the baseline directory.</param>
/// <param name="Message">What happened, for the panel.</param>
public sealed record VisualApprovalResult(IReadOnlyList<string> Approved, string Message);

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

/// <summary>
/// How the explorer's test runner judges accessibility scans, so it agrees with <c>workbench test --a11y</c>: the impact that
/// fails a scan, the axe-core rule tags, and each scenario's known violations (accessibility baselines).
/// </summary>
/// <param name="FailOn"><c>minor</c>, <c>moderate</c>, <c>serious</c> or <c>critical</c>.</param>
/// <param name="Tags">axe-core rule tags to run; empty runs axe-core's defaults.</param>
/// <param name="Known">Known violations by scenario id.</param>
public sealed record AccessibilityPolicy(string FailOn, IReadOnlyList<string> Tags, IReadOnlyDictionary<string, IReadOnlyList<AccessibilityBaseline.KnownViolation>> Known)
{
    public static AccessibilityPolicy Default { get; } = new("serious", [], new Dictionary<string, IReadOnlyList<AccessibilityBaseline.KnownViolation>>());
}

/// <summary>Supplies the <see cref="AccessibilityPolicy"/> for a set of scenarios. Implemented by the preview host from <c>workbench.json</c> and the baselines; optional.</summary>
public interface IAccessibilityPolicyProvider
{
    Task<AccessibilityPolicy> GetAsync(IReadOnlyList<string> scenarioIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Supplies the scenarios affected by the changes in the working copy to the explorer (<c>is:changed</c>, <c>is:new</c>,
/// <c>is:related</c>). Implemented by the preview host from git; optional.
/// </summary>
public interface IChangesProvider
{
    Task<BlazorWorkbench.Catalog.CatalogChangeSummary> GetChangesAsync(CancellationToken cancellationToken = default);
}
