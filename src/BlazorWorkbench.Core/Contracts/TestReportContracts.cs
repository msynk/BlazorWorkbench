using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorWorkbench.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<CheckKind>))]
public enum CheckKind
{
    Render,
    Interaction,
    Accessibility,
    Visual,
    Performance,

    /// <summary>The page does not scroll horizontally at any width of the responsive sweep (WCAG 1.4.10 Reflow).</summary>
    Responsive,
}

[JsonConverter(typeof(JsonStringEnumConverter<CheckStatus>))]
public enum CheckStatus
{
    Passed,
    Failed,
    Skipped,

    /// <summary>Infrastructure problem (browser crash, host unavailable); not a component defect.</summary>
    Error,

    /// <summary>Visual check without an approved baseline.</summary>
    Missing,
}

/// <summary>Machine-readable result of <c>workbench test</c> (written to <c>report.json</c>). Schema changes follow <see cref="SchemaVersion"/>.</summary>
public sealed class TestRunReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; set; }
    public string? WorkbenchVersion { get; init; }
    public RunEnvironment Environment { get; init; } = new();
    public RunSettings Settings { get; init; } = new();
    public RunSummary Summary { get; set; } = new();
    public List<ScenarioResult> Scenarios { get; init; } = [];

    /// <summary>Failures of the test infrastructure itself (exit code 3), as opposed to scenario failures (exit code 1).</summary>
    public List<string> InfrastructureErrors { get; init; } = [];

    public void Summarize()
    {
        Summary = new RunSummary
        {
            Total = Scenarios.Count,
            Passed = Scenarios.Count(s => s.Status == CheckStatus.Passed),
            Failed = Scenarios.Count(s => s.Status is CheckStatus.Failed or CheckStatus.Missing),
            Skipped = Scenarios.Count(s => s.Status == CheckStatus.Skipped),
            Errors = Scenarios.Count(s => s.Status == CheckStatus.Error) + InfrastructureErrors.Count,
            DurationMs = Math.Round((FinishedAt - StartedAt).TotalMilliseconds),
        };
    }

    public static TestRunReport? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TestRunReport>(stream, WorkbenchJson.Options);
    }
}

public sealed class RunEnvironment
{
    public string? Os { get; init; }

    /// <summary>Baseline platform folder: the OS (windows, linux, macos), plus <c>-webassembly</c> for WebAssembly hosts.</summary>
    public string? Platform { get; init; }

    /// <summary>Hosting model of the previews (Server or WebAssembly).</summary>
    public string? HostingModel { get; init; }
    public string? Runtime { get; init; }
    public string? Browser { get; init; }
    public string? BrowserVersion { get; init; }
    public bool Ci { get; init; }
    public string? HostUrl { get; init; }
}

public sealed class RunSettings
{
    public string? Filter { get; init; }

    /// <summary>
    /// True when the run was narrowed to an explicit scenario subset (<c>--only-failed</c>, <c>--changed-since</c> or
    /// <c>TestRunOptions.OnlyScenarios</c>), so the report does not list every scenario of the catalog.
    /// </summary>
    public bool Partial { get; init; }

    /// <summary>True when <see cref="Filter"/> or <see cref="Partial"/> narrowed the run.</summary>
    [JsonIgnore]
    public bool IsSubset => !string.IsNullOrEmpty(Filter) || Partial;

    public bool Interactions { get; init; } = true;
    public bool Accessibility { get; init; }
    public bool Visual { get; init; }
    public bool Performance { get; init; } = true;
    public bool Responsive { get; init; }

    /// <summary>Widths of the responsive sweep set for the run; empty uses 320 px and the edges of every breakpoint.</summary>
    public IReadOnlyList<int> ResponsiveWidths { get; init; } = [];

    public IReadOnlyList<string> Viewports { get; init; } = [];
    public IReadOnlyList<string> Themes { get; init; } = [];
    public int Workers { get; init; } = 1;
    public double VisualThreshold { get; init; } = 0.1;
    public double MaxDiffPixelRatio { get; init; }
    public string AccessibilityFailOn { get; init; } = "serious";
    public int Retries { get; init; }
}

public sealed class RunSummary
{
    public int Total { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int Errors { get; init; }
    public double DurationMs { get; init; }
}

public sealed class ScenarioResult
{
    public required string ScenarioId { get; init; }
    public string? ComponentId { get; init; }
    public string? Name { get; init; }
    public CheckStatus Status { get; set; }
    public string? Message { get; set; }
    public double DurationMs { get; set; }
    public double? ReadyMs { get; set; }
    public int? DomUpdates { get; set; }
    public int Attempts { get; set; } = 1;
    public List<CheckResult> Checks { get; init; } = [];
    public List<DiagnosticDto> Diagnostics { get; init; } = [];

    /// <summary>Artifact paths relative to the report directory (traces, screenshots, logs).</summary>
    public List<string> Artifacts { get; init; } = [];

    public void Conclude()
    {
        if (Status is CheckStatus.Skipped or CheckStatus.Error && Checks.Count == 0)
            return;
        Status = Checks.Any(c => c.Status == CheckStatus.Error) ? CheckStatus.Error
            : Checks.Any(c => c.Status is CheckStatus.Failed or CheckStatus.Missing) ? CheckStatus.Failed
            : Checks.Count > 0 && Checks.All(c => c.Status == CheckStatus.Skipped) ? CheckStatus.Skipped
            : CheckStatus.Passed;
        Message ??= Checks.FirstOrDefault(c => c.Status is CheckStatus.Failed or CheckStatus.Missing or CheckStatus.Error)?.Message;
    }
}

public sealed class CheckResult
{
    public required CheckKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Variant { get; init; }
    public CheckStatus Status { get; set; }
    public string? Message { get; set; }
    public double DurationMs { get; set; }
    public List<StepOutcome>? Steps { get; set; }
    public List<AccessibilityFinding>? Findings { get; set; }
    public VisualComparison? Visual { get; set; }
    public Dictionary<string, double>? Metrics { get; set; }
}

public sealed record StepOutcome(int Index, string Description, CheckStatus Status, string? Error, double DurationMs);

public sealed record AccessibilityFinding(
    string RuleId,
    string? Impact,
    string Help,
    string? HelpUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Targets,
    bool Blocking,
    bool NeedsReview = false);

public sealed class VisualComparison
{
    public required string Variant { get; init; }
    public string? Viewport { get; init; }
    public string? Theme { get; init; }

    /// <summary>Path relative to the baseline directory.</summary>
    public string? BaselinePath { get; init; }

    /// <summary>Path relative to the report directory.</summary>
    public string? ActualPath { get; init; }

    /// <summary>Path relative to the report directory.</summary>
    public string? DiffPath { get; set; }

    public double DiffRatio { get; set; }
    public long DiffPixels { get; set; }

    /// <summary>Pixels that differ only by anti-aliasing (ignored unless anti-aliasing is included).</summary>
    public long AntiAliasedPixels { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public bool SizeMismatch { get; set; }
}

/// <summary>Metadata of approved visual baselines (<c>manifest.json</c> in the baseline directory). Committed to source control.</summary>
public sealed class BaselineManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public SortedDictionary<string, BaselineEntry> Entries { get; init; } = new(StringComparer.Ordinal);
}

public sealed class BaselineEntry
{
    public required string ScenarioId { get; init; }
    public required string Variant { get; init; }
    public required string File { get; init; }
    public required string Sha256 { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Browser { get; init; }
    public string? BrowserVersion { get; init; }
    public string? Viewport { get; init; }
    public double DeviceScaleFactor { get; init; } = 1;
    public string? Theme { get; init; }
    public string? Culture { get; init; }
    public string? Os { get; init; }
    public DateTimeOffset ApprovedAt { get; init; }
}
