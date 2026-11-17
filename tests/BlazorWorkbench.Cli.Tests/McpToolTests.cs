using System.Text.Json;
using BlazorWorkbench.Cli;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Cli.Tests;

public class McpToolTests
{
    private static ScenarioResult Result(string id, CheckStatus status) => new() { ScenarioId = id, Status = status };

    [Fact]
    public async Task Filtered_report_summary_counts_only_matching_scenarios()
    {
        var dir = Directory.CreateTempSubdirectory("wb-mcp");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "workbench.json"), "{}");
            var report = new TestRunReport
            {
                StartedAt = DateTimeOffset.UnixEpoch,
                FinishedAt = DateTimeOffset.UnixEpoch.AddSeconds(2),
                Scenarios =
                {
                    Result("inputs-button--primary", CheckStatus.Passed),
                    Result("inputs-button--danger", CheckStatus.Failed),
                    Result("data-table--empty", CheckStatus.Failed),
                    Result("data-table--error", CheckStatus.Error),
                    Result("data-table--slow", CheckStatus.Skipped),
                },
            };
            report.Summarize();
            File.WriteAllText(Path.Combine(dir.FullName, "report.json"), JsonSerializer.Serialize(report, WorkbenchJson.Options));

            var settings = new McpSettings(new CliContext(Path.Combine(dir.FullName, "workbench.json"), null, "http://127.0.0.1:9", noBuild: true, verbose: false),
                dir.FullName, Path.Combine(dir.FullName, "baselines"), AllowApprove: false);
            await using var connection = new WorkbenchConnection(settings);
            var tools = new WorkbenchTools(connection, settings);

            using var filtered = JsonDocument.Parse(tools.GetLastReport("inputs-*"));
            var summary = filtered.RootElement.GetProperty("summary");
            Assert.Equal("inputs-*", filtered.RootElement.GetProperty("filter").GetString());
            Assert.Equal(2, summary.GetProperty("total").GetInt32());
            Assert.Equal(1, summary.GetProperty("passed").GetInt32());
            Assert.Equal(1, summary.GetProperty("failed").GetInt32());
            Assert.Equal(0, summary.GetProperty("errors").GetInt32());
            Assert.Equal(0, summary.GetProperty("skipped").GetInt32());
            Assert.Single(filtered.RootElement.GetProperty("failures").EnumerateArray());

            using var all = JsonDocument.Parse(tools.GetLastReport());
            Assert.Equal(5, all.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
            Assert.False(all.RootElement.TryGetProperty("filter", out _));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
