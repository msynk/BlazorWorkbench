using System.Text.Json;

namespace BlazorWorkbench.Components.Shell;

/// <summary>The result of one check of a test run started from the Workbench UI.</summary>
/// <param name="ScenarioId">The scenario.</param>
/// <param name="Kind"><c>render</c> (render health and, when asked, accessibility) or <c>interaction</c>.</param>
/// <param name="Name">The interaction test, for interaction checks.</param>
/// <param name="Status"><c>passed</c>, <c>warning</c>, <c>failed</c>, <c>error</c>, <c>skipped</c> or <c>canceled</c>.</param>
/// <param name="Message">The first failure or a note.</param>
/// <param name="DurationMs">Time from loading the preview to the end of the check.</param>
/// <param name="Details">The interaction outcome or the axe-core results, as the preview reported them.</param>
public sealed record UiTestResult(string ScenarioId, string Kind, string? Name, string Status, string? Message, double DurationMs, JsonElement? Details)
{
    /// <summary>The run that produced the result; results of an earlier, canceled run are ignored.</summary>
    public int RunId { get; init; }

    public bool IsFailure => Status is "failed" or "error";
}

/// <summary>What to run: the scenarios, whether to scan accessibility, and a label for the summary.</summary>
public sealed record UiTestRequest(IReadOnlyList<string> ScenarioIds, bool Accessibility, string Label);

/// <summary>
/// A test run started from the Workbench UI (the sidebar's test runner, like Storybook's test widget). It runs in the
/// browser, so results are best effort; <c>workbench test</c> with Playwright stays authoritative.
/// </summary>
public sealed class UiTestRun
{
    private readonly List<UiTestResult> _results = [];
    private readonly Dictionary<string, int> _expected = new(StringComparer.OrdinalIgnoreCase);

    public UiTestRequest? Request { get; private set; }
    public bool Running { get; private set; }
    public int Total { get; private set; }
    public int Done => _results.Count;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Re-run the last request when the catalog changes (hot reload).</summary>
    public bool Watch { get; set; }

    public IReadOnlyList<UiTestResult> Results => _results;
    public int Failed => _results.Count(r => r.IsFailure);
    public int Warnings => _results.Count(r => r.Status == "warning");
    public int Passed => _results.Count(r => r.Status == "passed");

    /// <summary>Results per scenario, in the explorer's terms (the scenarios run so far).</summary>
    public TestRunSummary Summary { get; private set; } = TestRunSummary.Empty;

    /// <summary>Starts a run of <paramref name="jobCounts"/> checks per scenario.</summary>
    public void Start(UiTestRequest request, IReadOnlyDictionary<string, int> jobCounts)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(jobCounts);
        Request = request;
        Running = true;
        _results.Clear();
        _expected.Clear();
        foreach (var (id, count) in jobCounts)
            _expected[id] = count;
        Total = jobCounts.Values.Sum();
        StartedAt = DateTimeOffset.UtcNow;
        FinishedAt = null;
        Summary = TestRunSummary.Empty;
    }

    public void Add(UiTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _results.Add(result);
        Summary = new TestRunSummary(StartedAt, Aggregate());
    }

    /// <summary>Records scenarios that were not run (for example <c>SkipTests</c>).</summary>
    public void Skip(string scenarioId, string reason) => Add(new UiTestResult(scenarioId, "render", null, "skipped", reason, 0, null));

    public void Finish()
    {
        Running = false;
        FinishedAt = DateTimeOffset.UtcNow;
        Summary = new TestRunSummary(FinishedAt, Aggregate());
    }

    /// <summary>The results of one scenario.</summary>
    public IEnumerable<UiTestResult> For(string scenarioId) => _results.Where(r => string.Equals(r.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, ScenarioRunStatus> Aggregate()
    {
        var statuses = new Dictionary<string, ScenarioRunStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in _results.GroupBy(r => r.ScenarioId, StringComparer.OrdinalIgnoreCase))
        {
            var failure = group.FirstOrDefault(r => r.IsFailure);
            // A scenario is complete when every check came back; until then it shows only failures.
            var complete = group.Count() >= _expected.GetValueOrDefault(group.Key, 1);
            if (failure is not null)
                statuses[group.Key] = new ScenarioRunStatus(failure.Status == "error" ? "Error" : "Failed", Describe(failure), false);
            else if (group.All(r => r.Status == "skipped"))
                statuses[group.Key] = new ScenarioRunStatus("Skipped", group.First().Message, false);
            else if (complete)
                statuses[group.Key] = new ScenarioRunStatus("Passed", group.FirstOrDefault(r => r.Status == "warning")?.Message, false);
        }

        return statuses;
    }

    private static string Describe(UiTestResult result) =>
        (result.Kind == "interaction" && result.Name is not null ? $"{result.Name}: " : "") + (result.Message ?? result.Status);
}
