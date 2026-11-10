using System.Text.Json;

namespace BlazorWorkbench.Contracts;

/// <summary>
/// Repository-level configuration (<c>workbench.json</c>) shared by the CLI and the preview host. All paths are relative
/// to the directory containing the file.
/// </summary>
public sealed class WorkbenchConfig
{
    public const string FileName = "workbench.json";

    /// <summary>The Workbench host project (<c>.csproj</c>).</summary>
    public string? Project { get; init; }

    /// <summary>Target framework passed to <c>dotnet run -f</c> for multi-targeted hosts (for example a .NET MAUI app).</summary>
    public string? Framework { get; init; }

    /// <summary>Directory of approved visual baselines (commit it).</summary>
    public string Baselines { get; init; } = "workbench-baselines";

    /// <summary>Directory for reports and artifacts (do not commit it).</summary>
    public string Output { get; init; } = "artifacts/workbench";

    public string Browser { get; init; } = "chromium";
    public IReadOnlyList<string> Viewports { get; init; } = [];
    public IReadOnlyList<string> Themes { get; init; } = [];
    public int Workers { get; init; } = 2;
    public int TimeoutSeconds { get; init; } = 30;
    public int Retries { get; init; }
    public string? Filter { get; init; }
    public VisualConfig Visual { get; init; } = new();
    public AccessibilityConfig Accessibility { get; init; } = new();
    public ResponsiveConfig Responsive { get; init; } = new();

    /// <summary>Full path of the loaded file (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourcePath { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string RootDirectory => SourcePath is null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(SourcePath)!;

    public string Resolve(string relative) => Path.GetFullPath(Path.Combine(RootDirectory, relative));

    public static WorkbenchConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<WorkbenchConfig>(json, new JsonSerializerOptions(WorkbenchJson.Options) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                     ?? new WorkbenchConfig();
        config.SourcePath = Path.GetFullPath(path);
        return config;
    }

    /// <summary>Searches <paramref name="startDirectory"/> and its parents for <c>workbench.json</c>.</summary>
    public static WorkbenchConfig? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return Load(candidate);
        }

        return null;
    }
}

public sealed class VisualConfig
{
    /// <summary>Per pixel color distance (0..1) tolerated before a pixel counts as different.</summary>
    public double Threshold { get; init; } = 0.1;

    /// <summary>Ratio (0..1) of differing pixels tolerated before the comparison fails.</summary>
    public double MaxDiffPixelRatio { get; init; }

    /// <summary>Ignore pixels that differ only because of anti-aliasing.</summary>
    public bool IgnoreAntiAliasing { get; init; } = true;
}

public sealed class ResponsiveConfig
{
    /// <summary>Run the responsive check in every <c>workbench test</c> (the same as <c>--responsive</c>).</summary>
    public bool Enabled { get; init; }

    /// <summary>Widths, in CSS pixels, to check. Empty checks 320 px and both edges of every breakpoint.</summary>
    public IReadOnlyList<int> Widths { get; init; } = [];
}

public sealed class AccessibilityConfig
{
    /// <summary>Lowest axe-core impact that fails a run: minor, moderate, serious or critical.</summary>
    public string FailOn { get; init; } = "serious";

    /// <summary>axe-core tags to run (WCAG levels). Empty runs every rule.</summary>
    public IReadOnlyList<string> Tags { get; init; } = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"];
}
