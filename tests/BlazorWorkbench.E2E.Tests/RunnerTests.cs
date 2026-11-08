using System.Diagnostics;
using System.Text.Json;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Exercises the automation engine used by <c>workbench test</c> against the demo host.</summary>
public class RunnerTests(WorkbenchFixture fixture) : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("wb-e2e");

    public void Dispose() => _temp.Delete(true);

    private TestRunOptions Options(string filter) => new()
    {
        Filter = filter,
        OutputDirectory = Path.Combine(_temp.FullName, "out"),
        BaselineDirectory = Path.Combine(_temp.FullName, "baselines"),
        Workers = 2,
        Viewports = ["laptop"],
        Themes = ["light"],
    };

    [Fact]
    public async Task Render_and_interaction_checks_pass_for_healthy_scenarios()
    {
        var report = await new WorkbenchTestRunner(fixture.Host, Options("inputs-counter-*,forms-*,data-product-list--loading-gated")).RunAsync();
        Assert.Equal(0, report.Summary.Failed);
        Assert.Equal(0, report.Summary.Errors);
        Assert.Contains(report.Scenarios.SelectMany(s => s.Checks), c => c.Kind == CheckKind.Interaction && c.Name == "keyboard arrows" && c.Status == CheckStatus.Passed);
        Assert.All(report.Scenarios, s => Assert.True(s.ReadyMs > 0));
        Assert.True(File.Exists(Path.Combine(_temp.FullName, "out", "junit.xml")));
    }

    [Fact]
    public async Task Skipped_scenarios_are_reported_not_run()
    {
        var report = await new WorkbenchTestRunner(fixture.Host, Options("feedback-flaky-widget--*")).RunAsync();
        Assert.Equal(CheckStatus.Skipped, report.Scenarios.Single(s => s.ScenarioId.EndsWith("throws-while-rendering", StringComparison.Ordinal)).Status);
        Assert.Equal(CheckStatus.Passed, report.Scenarios.Single(s => s.ScenarioId.EndsWith("healthy", StringComparison.Ordinal)).Status);
    }

    [Fact]
    public async Task Accessibility_scans_report_findings()
    {
        var options = Options("inputs-button--*,data-product-list--in-stock");
        options.Accessibility = true;
        options.Interactions = false;
        var report = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        Assert.Equal(0, report.Summary.Failed);
        Assert.All(report.Scenarios, s => Assert.Contains(s.Checks, c => c.Kind == CheckKind.Accessibility && c.Status == CheckStatus.Passed));
    }

    [Fact]
    public async Task Visual_workflow_detects_missing_then_passes_after_approval()
    {
        var options = Options("display-badge--*");
        options.Visual = true;
        options.Interactions = false;

        var first = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        Assert.All(first.Scenarios, s => Assert.Contains(s.Checks, c => c.Kind == CheckKind.Visual && c.Status == CheckStatus.Missing));
        Assert.Equal(first.Scenarios.Count, first.Summary.Failed);

        var store = new BaselineStore(options.BaselineDirectory!);
        Assert.Equal(first.Scenarios.Count, store.Approve(first, options.OutputDirectory, _ => true).Count);

        var second = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        Assert.Equal(second.Scenarios.Count, second.Summary.Passed);
        Assert.All(second.Scenarios.SelectMany(s => s.Checks).Where(c => c.Kind == CheckKind.Visual), c => Assert.Equal(0, c.Visual!.DiffPixels));

        // A different viewport has no baseline yet.
        options.Viewports = ["mobile"];
        var third = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        Assert.All(third.Scenarios, s => Assert.Equal(CheckStatus.Failed, s.Status));
    }

    [Fact]
    public async Task Publishes_a_static_catalog_without_sensitive_details()
    {
        var output = Path.Combine(_temp.FullName, "site");
        await CatalogPublisher.PublishAsync(fixture.Host, new PublishOptions { OutputDirectory = output, Screenshots = true, Workers = 4 });
        Assert.True(File.Exists(Path.Combine(output, "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "components", "inputs-button.html")));
        Assert.True(File.Exists(Path.Combine(output, "screenshots", "inputs-button--primary.png")));
        var everything = string.Concat(Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain(fixture.Host.BaseAddress.ToString(), everything);
        Assert.DoesNotContain(WorkbenchFixture.RepositoryRoot, everything);
        Assert.DoesNotContain(WorkbenchFixture.RepositoryRoot.Replace('\\', '/'), everything);
        Assert.Contains("Visual emphasis of the button.", File.ReadAllText(Path.Combine(output, "components", "inputs-button.html")));
    }
}

/// <summary>Smoke-tests the MCP server over stdio with raw JSON-RPC.</summary>
public class McpTests(WorkbenchFixture fixture)
{
    [Fact]
    public async Task Lists_tools_and_answers_calls()
    {
        var cli = Path.Combine(WorkbenchFixture.RepositoryRoot, "src", "BlazorWorkbench.Cli", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "net10.0", "workbench.dll");
        var output = Directory.CreateTempSubdirectory("wb-mcp");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { cli, "mcp", "--url", fixture.Host.BaseAddress.ToString(), "--output", output.FullName })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        try
        {
            async Task<JsonElement> Send(int id, string method, object parameters)
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
                await process.StandardInput.FlushAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cts.Token) ?? throw new InvalidOperationException("MCP server closed stdout.");
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                        return doc.RootElement.Clone();
                }
            }

            var init = await Send(1, "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "e2e", version = "1.0" } });
            Assert.Equal("blazor-workbench", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

            var tools = await Send(2, "tools/list", new { });
            var names = tools.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
            Assert.Contains("list_components", names);
            Assert.Contains("run_tests", names);
            Assert.Contains("approve_baselines", names);

            var list = await Send(3, "tools/call", new { name = "list_components", arguments = new { filter = "inputs-*" } });
            var text = list.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
            Assert.True(text!.Contains("inputs-button", StringComparison.Ordinal), text);
            Assert.DoesNotContain("data-weather", text);

            var suggest = await Send(4, "tools/call", new { name = "suggest_scenarios", arguments = new { id = "display-badge" } });
            Assert.Contains("ScenarioSet<Badge>", suggest.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

            var approve = await Send(5, "tools/call", new { name = "approve_baselines", arguments = new { filter = "*", confirm = true } });
            Assert.Contains("Refused", approve.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

            var audit = await File.ReadAllLinesAsync(Path.Combine(output.FullName, "mcp-audit.log"));
            Assert.Contains(audit, l => l.Contains("approve_baselines", StringComparison.Ordinal));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            output.Delete(true);
        }
    }
}
