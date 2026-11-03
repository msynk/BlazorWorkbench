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
