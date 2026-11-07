using BlazorWorkbench.Cli;

namespace BlazorWorkbench.Cli.Tests;

[Collection("console")]
public class CliTests
{
    [Fact]
    public void Defines_documented_commands()
    {
        var names = CliApp.Build().Subcommands.Select(c => c.Name).ToHashSet();
        foreach (var expected in new[] { "list", "validate", "test", "a11y", "approve", "dev", "publish", "install-browsers", "init", "mcp" })
            Assert.Contains(expected, names);
    }

    [Theory]
    [InlineData("test --workers abc")]
    [InlineData("test --fail-on catastrophic")]
    [InlineData("no-such-command")]
    [InlineData("validate --unknown-option")]
    public async Task Usage_errors_exit_with_code_2(string args) =>
        Assert.Equal(ExitCodes.Usage, await CliApp.RunAsync(args.Split(' ')));

    [Fact]
    public async Task Help_succeeds() => Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["--help"]));

    [Fact]
    public void Parses_test_options()
    {
        var result = CliApp.Build().Parse("test --filter inputs-* --a11y --visual --viewport mobile laptop --workers 3 --retries 1 --fail-on critical --project x.csproj");
        Assert.Empty(result.Errors);
        Assert.Equal("x.csproj", result.GetValue(CliApp.Project));
    }

    [Fact]
    public async Task Approve_without_report_is_a_usage_error()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Equal(ExitCodes.Usage, await CliApp.RunAsync(["approve", "--output", Path.Combine(dir.FullName, "none")]));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Test_run_options_apply_every_configuration_setting()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "workbench.json");
            File.WriteAllText(path, """
                {
                  "browser": "firefox",
                  "timeoutSeconds": 7,
                  "retries": 2,
                  "filter": "inputs-*",
                  "workers": 3,
                  "visual": { "threshold": 0.2, "maxDiffPixelRatio": 0.01, "ignoreAntiAliasing": false },
                  "accessibility": { "failOn": "critical", "tags": ["wcag2a"] }
                }
                """);
            var options = new CliContext(path, null, "http://127.0.0.1:9", noBuild: true, verbose: false).CreateTestRunOptions();
            Assert.Equal("firefox", options.Browser);
            Assert.Equal(TimeSpan.FromSeconds(7), options.ReadyTimeout);
            Assert.Equal(2, options.Retries);
            Assert.Equal("inputs-*", options.Filter);
            Assert.Equal(3, options.Workers);
            Assert.Equal(0.2, options.VisualThreshold);
            Assert.Equal(0.01, options.MaxDiffPixelRatio);
            Assert.False(options.IgnoreAntiAliasing);
            Assert.Equal("critical", options.AccessibilityFailOn);
            Assert.Equal(["wcag2a"], options.AccessibilityTags);
            Assert.Equal(Path.Combine(dir.FullName, "workbench-baselines"), options.BaselineDirectory);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public async Task Prune_refuses_reports_of_narrowed_runs()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var output = Path.Combine(dir.FullName, "out");
            var baselines = Path.Combine(dir.FullName, "baselines");
            var other = Path.Combine(baselines, BlazorWorkbench.Testing.BaselineStore.CurrentPlatform, "other--scenario");
            Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other, "laptop-light.png"), [1]);

            // --only-failed / --changed-since runs record Partial instead of a filter.
            var report = new BlazorWorkbench.Contracts.TestRunReport { Settings = new BlazorWorkbench.Contracts.RunSettings { Partial = true } };
            report.Scenarios.Add(new BlazorWorkbench.Contracts.ScenarioResult { ScenarioId = "changed--scenario" });
            BlazorWorkbench.Testing.ReportWriter.WriteAll(report, output);

            Assert.Equal(ExitCodes.Usage, await CliApp.RunAsync(["approve", "--output", output, "--baselines", baselines, "--prune"]));
            Assert.True(Directory.Exists(other));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public async Task Unreachable_host_is_an_infrastructure_error() =>
        Assert.Equal(ExitCodes.Infrastructure, await CliApp.RunAsync(["validate", "--url", "http://127.0.0.1:9"]));

    [Fact]
    public void Init_writes_configuration_once()
    {
        var dir = Directory.CreateTempSubdirectory();
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir.FullName);
            Assert.Equal(ExitCodes.Success, Commands.Init(force: false));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "workbench.json")));
            Assert.Equal(ExitCodes.Usage, Commands.Init(force: false));
            var config = BlazorWorkbench.Contracts.WorkbenchConfig.Load(Path.Combine(dir.FullName, "workbench.json"));
            Assert.Equal("serious", config.Accessibility.FailOn);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            dir.Delete(true);
        }
    }
}
