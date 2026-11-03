namespace BlazorWorkbench.Testing;

/// <summary>Options of a <see cref="WorkbenchTestRunner"/> run (mirrors <c>workbench test</c> options).</summary>
public sealed class TestRunOptions
{
    /// <summary>Scenario filter (glob on ids/titles, <c>tag:name</c>, comma separated).</summary>
    public string? Filter { get; set; }

    /// <summary>Scenario ids to run (for example failures of a previous run). Applied after <see cref="Filter"/>.</summary>
    public IReadOnlySet<string>? OnlyScenarios { get; set; }

    public bool Interactions { get; set; } = true;
    public bool Accessibility { get; set; }
    public bool Visual { get; set; }

    /// <summary>Viewport preset names for visual and default runs. Empty uses the first viewport or the scenario's viewport.</summary>
    public IReadOnlyList<string> Viewports { get; set; } = [];

    /// <summary>Theme names for visual runs. Empty uses the first theme or the scenario's theme.</summary>
    public IReadOnlyList<string> Themes { get; set; } = [];

    public int Workers { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public int Retries { get; set; }
    public double VisualThreshold { get; set; } = 0.1;
    public double MaxDiffPixelRatio { get; set; }

    /// <summary>Ignore pixels that differ only because of anti-aliasing (default true; they are drawn yellow in diffs).</summary>
    public bool IgnoreAntiAliasing { get; set; } = true;

    /// <summary>Lowest axe-core impact that fails: minor, moderate, serious, critical.</summary>
    public string AccessibilityFailOn { get; set; } = "serious";

    public IReadOnlyList<string> AccessibilityTags { get; set; } = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"];

    /// <summary>Record Playwright traces for failing interaction tests (they contain DOM snapshots of fixture data).</summary>
    public bool TraceOnFailure { get; set; } = true;

    public string Browser { get; set; } = "chromium";
    public bool Headed { get; set; }

    /// <summary>Directory for report.json, junit.xml, index.html and artifacts.</summary>
    public string OutputDirectory { get; set; } = Path.Combine("artifacts", "workbench");

    /// <summary>Directory of approved baselines (required for visual checks).</summary>
    public string? BaselineDirectory { get; set; }

    /// <summary>Treat missing baselines as failures (default) instead of reporting them only.</summary>
    public bool FailOnMissingBaseline { get; set; } = true;
}

/// <summary>Progress notifications for console output.</summary>
public interface ITestRunObserver
{
    void OnScenarioStarted(string scenarioId) { }
    void OnScenarioCompleted(BlazorWorkbench.Contracts.ScenarioResult result) { }
    void OnMessage(string message) { }
}
