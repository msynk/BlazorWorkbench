using System.Diagnostics;
using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;

namespace BlazorWorkbench.Cli;

/// <summary>Implementations of the CLI commands. Machine output goes to stdout; progress goes to stderr.</summary>
public static class Commands
{
    public static async Task<int> ListAsync(CliContext context, string? filter, bool json, CancellationToken ct)
    {
        await using var host = await context.ConnectAsync(ct);
        var catalog = await host.GetCatalogAsync(ct);
        var items = catalog.Components.SelectMany(c => c.Scenarios.Select(s => (c, s)))
            .Where(x => filter is null || filter.Split(',').Any(t => Glob.IsMatch(x.s.Id, t.Trim()) || Glob.IsMatch(x.c.Id, t.Trim()) || Glob.IsMatch($"{x.c.Title}/{x.s.Name}", t.Trim())))
            .ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(items.Select(x => new
            {
                id = x.s.Id,
                component = x.c.Id,
                title = x.c.Title,
                name = x.s.Name,
                tags = x.s.Tags.Concat(x.c.Tags).Distinct(),
                interactions = x.s.Interactions.Select(i => i.Name),
                generated = x.s.IsGenerated,
                skip = x.s.SkipReason,
            }), WorkbenchJson.Indented));
            return ExitCodes.Success;
        }

        foreach (var group in items.GroupBy(x => x.c))
        {
            Console.WriteLine(Out.Bold(group.Key.Title) + Out.Dim($"  ({group.Key.FullName})"));
            foreach (var (_, s) in group)
                Console.WriteLine($"  {s.Id}{(s.Interactions.Count > 0 ? Out.Dim($"  [{s.Interactions.Count} interaction(s)]") : "")}{(s.IsGenerated ? Out.Dim("  [generated]") : "")}{(s.SkipReason is not null ? Out.Yellow("  [skipped in tests]") : "")}");
        }

        Console.WriteLine(Out.Dim($"{items.Select(x => x.c.Id).Distinct().Count()} components, {items.Count} scenarios"));
        return ExitCodes.Success;
    }

    public static async Task<int> ValidateAsync(CliContext context, bool json, bool warningsAsErrors, CancellationToken ct)
    {
        await using var host = await context.ConnectAsync(ct);
        var report = await host.ValidateAsync(ct);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, WorkbenchJson.Indented));
        }
        else
        {
            foreach (var issue in report.Issues.Where(i => i.Severity >= IssueSeverity.Warning).OrderByDescending(i => i.Severity))
            {
                var label = issue.Severity == IssueSeverity.Error ? Out.Red("error") : Out.Yellow("warning");
                Console.WriteLine($"{label} {issue.Code}: {issue.Message}{((issue.ScenarioId ?? issue.ComponentId) is { } where ? Out.Dim($" [{where}]") : "")}");
                if (issue.Remedy is { } remedy)
                    Console.WriteLine(Out.Dim($"    {remedy}"));
            }

            Console.WriteLine($"{report.Components} components, {report.Scenarios} scenarios: {report.Errors} error(s), {report.Warnings} warning(s).");
        }

        return report.Errors > 0 || (warningsAsErrors && report.Warnings > 0) ? ExitCodes.Failures : ExitCodes.Success;
    }

    public static async Task<int> TestAsync(CliContext context, TestRunOptions options, bool onlyFailed, CancellationToken ct)
    {
        if (onlyFailed)
        {
            var previous = TestRunReport.Load(Path.Combine(options.OutputDirectory, "report.json"));
            if (previous is null)
            {
                Console.Error.WriteLine("No previous report.json; running every selected scenario.");
            }
            else
            {
                options.OnlyScenarios = previous.Scenarios.Where(s => s.Status is CheckStatus.Failed or CheckStatus.Error or CheckStatus.Missing).Select(s => s.ScenarioId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Console.Error.WriteLine($"Re-running {options.OnlyScenarios.Count} failed scenario(s) from the previous report.");
                if (options.OnlyScenarios.Count == 0)
                    return ExitCodes.Success;
            }
        }

        var watch = Stopwatch.StartNew();
        await using var host = await context.ConnectAsync(ct);
        var runner = new WorkbenchTestRunner(host, options, new ConsoleObserver());
        var report = await runner.RunAsync(ct);
        if (context.Verbose || report.Summary.Errors > 0)
            File.WriteAllText(Path.Combine(options.OutputDirectory, "host.log"), host.Log);

        var s = report.Summary;
        Console.WriteLine();
        Console.WriteLine($"{Out.Bold("Result:")} {Out.Green($"{s.Passed} passed")}, {(s.Failed > 0 ? Out.Red($"{s.Failed} failed") : "0 failed")}, {s.Skipped} skipped, {(s.Errors > 0 ? Out.Red($"{s.Errors} infrastructure error(s)") : "0 errors")} in {watch.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine(Out.Dim($"Reports: {Path.Combine(options.OutputDirectory, "index.html")} · report.json · junit.xml"));

        var missing = report.Scenarios.SelectMany(x => x.Checks).Count(c => c.Status == CheckStatus.Missing);
        if (missing > 0)
            Console.WriteLine(Out.Yellow($"{missing} screenshot(s) have no approved baseline. Review them, then run 'workbench approve'."));

        if (s.Errors > 0 && s.Failed == 0)
            return ExitCodes.Infrastructure;
        return s.Failed > 0 || s.Errors > 0 ? ExitCodes.Failures : ExitCodes.Success;
    }

    public static int Approve(CliContext context, string? output, string? baselines, string? filter, bool all, bool prune)
    {
        var outputDirectory = context.OutputDirectory(output);
        var report = TestRunReport.Load(Path.Combine(outputDirectory, "report.json"));
        if (report is null)
        {
            Console.Error.WriteLine($"No report.json in {outputDirectory}. Run 'workbench test --visual' first.");
            return ExitCodes.Usage;
        }

        var store = new BaselineStore(context.BaselineDirectory(baselines));
        bool Matches(string id) => filter is null || filter.Split(',').Any(t => Glob.IsMatch(id, t.Trim()));
        var approved = store.Approve(report, outputDirectory, Matches, includePassed: all);
        foreach (var entry in approved)
            Console.WriteLine($"{Out.Green("approved")} {entry.File} ({entry.Width}×{entry.Height}, {entry.Browser} {entry.BrowserVersion})");
        Console.WriteLine(approved.Count == 0
            ? "Nothing to approve: no failed or missing visual checks matched."
            : $"{approved.Count} baseline(s) updated in {store.Directory}. Review and commit them with the change that caused them.");

        if (prune)
        {
            var ids = report.Scenarios.Select(x => x.ScenarioId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(report.Settings.Filter))
            {
                Console.Error.WriteLine("Refusing to prune: the last run was filtered, so it does not list every scenario.");
                return ExitCodes.Usage;
            }

            foreach (var removed in store.Prune(ids))
                Console.WriteLine($"{Out.Yellow("removed")} {removed}");
        }

        return ExitCodes.Success;
    }

    public static async Task<int> PublishAsync(CliContext context, string output, bool screenshots, string[] excludeTags, string? version, CancellationToken ct)
    {
        await using var host = await context.ConnectAsync(ct);
        await CatalogPublisher.PublishAsync(host, new PublishOptions
        {
            OutputDirectory = output,
            Screenshots = screenshots,
            ExcludeTags = excludeTags.Length > 0 ? excludeTags : ["internal"],
            Version = version,
        }, Console.Error.WriteLine, ct);
        return ExitCodes.Success;
    }

    public static async Task<int> DevAsync(CliContext context, CancellationToken ct)
    {
        if (context.Project is null)
        {
            Console.Error.WriteLine("No Workbench host project. Pass --project or add \"project\" to workbench.json.");
            return ExitCodes.Usage;
        }

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var arg in new[] { "watch", "--project", context.Project })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new WorkbenchInfrastructureException("dotnet watch could not be started.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }

    public static int InstallBrowsers(string browser, bool withDeps)
    {
        var args = new List<string> { "install" };
        if (withDeps)
            args.Add("--with-deps");
        args.Add(browser);
        return Microsoft.Playwright.Program.Main([.. args]);
    }

    public static int Init(bool force)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), WorkbenchConfig.FileName);
        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"{WorkbenchConfig.FileName} already exists. Use --force to overwrite.");
            return ExitCodes.Usage;
        }

        var project = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .FirstOrDefault(p => File.ReadAllText(p).Contains("BlazorWorkbench.Hosting", StringComparison.Ordinal));
        var relative = project is null ? "path/to/YourWorkbench.csproj" : Path.GetRelativePath(Directory.GetCurrentDirectory(), project).Replace('\\', '/');
        File.WriteAllText(path, $$"""
            {
              "project": "{{relative}}",
              "baselines": "workbench-baselines",
              "output": "artifacts/workbench",
              "browser": "chromium",
              "viewports": ["laptop"],
              "themes": ["light"],
              "workers": 2,
              "timeoutSeconds": 30,
              "visual": { "threshold": 0.1, "maxDiffPixelRatio": 0 },
              "accessibility": { "failOn": "serious" }
            }

            """);
        Console.WriteLine($"Created {path}");
        return ExitCodes.Success;
    }

    private sealed class ConsoleObserver : ITestRunObserver
    {
        private static readonly Lock Sync = new();

        public void OnScenarioCompleted(ScenarioResult result)
        {
            // Build the block first so parallel workers never interleave lines.
            var text = new System.Text.StringBuilder();
            text.AppendLine($"{Out.Status(result.Status)} {result.ScenarioId} {Out.Dim($"{result.DurationMs:0} ms")}{(result.Attempts > 1 ? Out.Yellow($" (attempt {result.Attempts})") : "")}");
            foreach (var check in result.Checks.Where(c => c.Status is CheckStatus.Failed or CheckStatus.Missing or CheckStatus.Error))
            {
                text.AppendLine($"     {Out.Red("✖")} {check.Kind}: {check.Variant ?? check.Name} — {check.Message}");
                foreach (var step in check.Steps?.Where(s => s.Status == CheckStatus.Failed) ?? [])
                    text.AppendLine(Out.Dim($"         at step {step.Index + 1}: {step.Description}"));
            }

            lock (Sync)
                Console.Write(text.ToString());
        }
    }
}
