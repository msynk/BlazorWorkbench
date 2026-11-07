using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;

namespace BlazorWorkbench.Cli;

/// <summary>Exit codes (documented in docs/cli.md).</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failures = 1;
    public const int Usage = 2;
    public const int Infrastructure = 3;
}

/// <summary>Resolves configuration and connects to a Workbench host for a command.</summary>
public sealed class CliContext
{
    public CliContext(string? configPath, string? project, string? url, bool noBuild, bool verbose)
    {
        Config = configPath is not null
            ? WorkbenchConfig.Load(configPath)
            : WorkbenchConfig.Find(Directory.GetCurrentDirectory()) ?? new WorkbenchConfig();
        Project = project is not null ? Path.GetFullPath(project) : Config.Project is { } p ? Config.Resolve(p) : FindProject();
        Url = url is null ? null : new Uri(url.EndsWith('/') ? url : url + "/");
        NoBuild = noBuild;
        Verbose = verbose;
    }

    public WorkbenchConfig Config { get; }
    public string? Project { get; }
    public Uri? Url { get; }
    public bool NoBuild { get; }
    public bool Verbose { get; }
    public string Configuration { get; init; } = "Debug";

    /// <summary>Target framework from <c>--framework</c>; overrides <c>framework</c> in workbench.json.</summary>
    public string? FrameworkOverride { get; init; }

    public string? Framework => FrameworkOverride ?? Config.Framework;

    public string OutputDirectory(string? overridePath) => overridePath is not null ? Path.GetFullPath(overridePath) : Config.Resolve(Config.Output);
    public string BaselineDirectory(string? overridePath) => overridePath is not null ? Path.GetFullPath(overridePath) : Config.Resolve(Config.Baselines);

    /// <summary>
    /// Test run options with every workbench.json setting applied. <c>workbench test</c> and the MCP <c>run_tests</c> tool
    /// both start from these, so agents get the same browser, thresholds and accessibility rules as CI.
    /// </summary>
    public TestRunOptions CreateTestRunOptions(string? outputDirectory = null, string? baselineDirectory = null) => new()
    {
        Filter = Config.Filter,
        Viewports = Config.Viewports,
        Themes = Config.Themes,
        Workers = Config.Workers,
        Retries = Config.Retries,
        OutputDirectory = OutputDirectory(outputDirectory),
        BaselineDirectory = BaselineDirectory(baselineDirectory),
        Browser = Config.Browser,
        AccessibilityFailOn = Config.Accessibility.FailOn,
        AccessibilityTags = Config.Accessibility.Tags,
        VisualThreshold = Config.Visual.Threshold,
        MaxDiffPixelRatio = Config.Visual.MaxDiffPixelRatio,
        IgnoreAntiAliasing = Config.Visual.IgnoreAntiAliasing,
        ReadyTimeout = TimeSpan.FromSeconds(Config.TimeoutSeconds),
    };

    public async Task<WorkbenchHost> ConnectAsync(CancellationToken cancellationToken)
    {
        if (Url is not null)
        {
            Console.Error.WriteLine($"Connecting to {Url}…");
            return await WorkbenchHost.AttachAsync(Url, cancellationToken).ConfigureAwait(false);
        }

        if (Project is null)
            throw new WorkbenchInfrastructureException("No Workbench host project. Pass --project, --url, or add \"project\" to workbench.json.");

        Console.Error.WriteLine($"Starting {Path.GetFileName(Project)}{(NoBuild ? "" : " (building)")}…");
        return await WorkbenchHost.StartAsync(Project, NoBuild, Configuration, onOutput: Verbose ? line => Console.Error.WriteLine($"  host: {line}") : null,
            cancellationToken: cancellationToken, framework: Framework).ConfigureAwait(false);
    }

    private static string? FindProject()
    {
        var candidates = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj").ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
}

/// <summary>Minimal console styling that degrades to plain text when output is redirected or NO_COLOR is set.</summary>
public static class Out
{
    private static readonly bool Color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static string Green(string s) => Color ? $"\u001b[32m{s}\u001b[0m" : s;
    public static string Red(string s) => Color ? $"\u001b[31m{s}\u001b[0m" : s;
    public static string Yellow(string s) => Color ? $"\u001b[33m{s}\u001b[0m" : s;
    public static string Dim(string s) => Color ? $"\u001b[2m{s}\u001b[0m" : s;
    public static string Bold(string s) => Color ? $"\u001b[1m{s}\u001b[0m" : s;

    public static string Status(CheckStatus status) => status switch
    {
        CheckStatus.Passed => Green("PASS"),
        CheckStatus.Failed => Red("FAIL"),
        CheckStatus.Missing => Red("MISS"),
        CheckStatus.Error => Red("ERR "),
        _ => Dim("SKIP"),
    };
}
