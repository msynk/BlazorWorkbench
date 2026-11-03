using System.ComponentModel;
using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Documentation;
using BlazorWorkbench.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace BlazorWorkbench.Cli;

/// <summary>Settings of the MCP server (from <c>workbench mcp</c> options).</summary>
public sealed record McpSettings(CliContext Context, string OutputDirectory, string BaselineDirectory, bool AllowApprove);

/// <summary>
/// Model Context Protocol server over stdio. Exposes the same catalog, validation and test APIs as the UI and CLI to
/// coding agents. Read-only by default: approving baselines requires <c>--allow-approve</c> plus an explicit confirmation
/// argument. Every tool call is appended to <c>mcp-audit.log</c> in the output directory.
/// </summary>
public static class McpServerHost
{
    public static async Task<int> RunAsync(McpSettings settings, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace); // stdout is reserved for the protocol
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<WorkbenchConnection>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = "blazor-workbench", Version = typeof(McpServerHost).Assembly.GetName().Version?.ToString() ?? "0.1.0" })
            .WithStdioServerTransport()
            .WithTools<WorkbenchTools>();
        await builder.Build().RunAsync(cancellationToken);
        return ExitCodes.Success;
    }
}

/// <summary>Lazily starts (or attaches to) one Workbench host shared by all tool calls.</summary>
public sealed class WorkbenchConnection(McpSettings settings) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WorkbenchHost? _host;

    public async Task<WorkbenchHost> GetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _host ??= await settings.Context.ConnectAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Audit(string tool, object? arguments)
    {
        try
        {
            Directory.CreateDirectory(settings.OutputDirectory);
            var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, tool, arguments }, WorkbenchJson.Options);
            File.AppendAllText(Path.Combine(settings.OutputDirectory, "mcp-audit.log"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Auditing must not break the tool call; failures surface in the server log.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        _lock.Dispose();
    }
}

[McpServerToolType]
public sealed class WorkbenchTools(WorkbenchConnection connection, McpSettings settings)
{
    private static string Json(object value) => JsonSerializer.Serialize(value, WorkbenchJson.Indented);

    [McpServerTool(Name = "list_components", ReadOnly = true), Description("Lists Razor components in the Workbench catalog with their scenarios. Optional glob filter on component ids or titles.")]
    public async Task<string> ListComponents([Description("Glob filter, for example 'inputs-*'")] string? filter = null, CancellationToken ct = default)
    {
        connection.Audit("list_components", new { filter });
        var catalog = await (await connection.GetAsync(ct)).GetCatalogAsync(ct);
        return Json(catalog.Components
            .Where(c => filter is null || Glob.IsMatch(c.Id, filter) || Glob.IsMatch(c.Title, filter))
            .Select(c => new { c.Id, c.Title, c.Name, c.Summary, c.Tags, scenarios = c.Scenarios.Select(s => new { s.Id, s.Name, s.IsGenerated }) }));
    }

    [McpServerTool(Name = "get_component", ReadOnly = true), Description("Returns parameters, documentation, dependencies and scenarios (with Razor usage) of one component.")]
    public async Task<string> GetComponent([Description("Component id, for example 'inputs-button'")] string id, CancellationToken ct = default)
    {
        connection.Audit("get_component", new { id });
        var catalog = await (await connection.GetAsync(ct)).GetCatalogAsync(ct);
        var component = catalog.Components.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        return component is null ? $"Component '{id}' was not found. Use list_components." : Json(component);
    }

    [McpServerTool(Name = "validate_scenarios", ReadOnly = true), Description("Validates every scenario without rendering and returns errors and warnings with remedies.")]
    public async Task<string> Validate(CancellationToken ct = default)
    {
        connection.Audit("validate_scenarios", null);
        var report = await (await connection.GetAsync(ct)).ValidateAsync(ct);
        return Json(new { report.Errors, report.Warnings, issues = report.Issues.Where(i => i.Severity >= IssueSeverity.Warning) });
    }

    [McpServerTool(Name = "suggest_scenarios", ReadOnly = true), Description("Suggests missing boundary-case scenarios for a component and returns a C# ScenarioSet skeleton to review and adapt.")]
    public async Task<string> SuggestScenarios([Description("Component id")] string id, CancellationToken ct = default)
    {
        connection.Audit("suggest_scenarios", new { id });
        var catalog = await (await connection.GetAsync(ct)).GetCatalogAsync(ct);
        var component = catalog.Components.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (component is null)
            return $"Component '{id}' was not found.";
        var suggestions = ScenarioSkeletonGenerator.Suggest(component);
        return Json(new { suggestions, code = ScenarioSkeletonGenerator.GenerateSet(component, suggestions), note = "Generated code is a starting point; run validate_scenarios after adding it." });
    }

    [McpServerTool(Name = "run_tests"), Description("Runs scenario tests in a headless browser (render health, interactions; optionally accessibility and visual comparison) and returns a summary with failures. Never modifies baselines.")]
    public async Task<string> RunTests(
        [Description("Scenario filter (glob on ids/titles, tag:name)")] string? filter = null,
        [Description("Run axe-core accessibility scans")] bool accessibility = false,
        [Description("Compare screenshots against approved baselines")] bool visual = false,
        CancellationToken ct = default)
    {
        connection.Audit("run_tests", new { filter, accessibility, visual });
        var host = await connection.GetAsync(ct);
        var config = settings.Context.Config;
        var report = await new WorkbenchTestRunner(host, new TestRunOptions
        {
            Filter = filter,
            Accessibility = accessibility,
            Visual = visual,
            OutputDirectory = settings.OutputDirectory,
            BaselineDirectory = settings.BaselineDirectory,
            Viewports = config.Viewports,
            Themes = config.Themes,
            Workers = config.Workers,
            VisualThreshold = config.Visual.Threshold,
            MaxDiffPixelRatio = config.Visual.MaxDiffPixelRatio,
            AccessibilityFailOn = config.Accessibility.FailOn,
        }).RunAsync(ct);
        return Summarize(report);
    }

    [McpServerTool(Name = "get_last_report", ReadOnly = true), Description("Returns failures and diagnostics from the most recent test report.")]
    public string GetLastReport([Description("Optional scenario filter")] string? filter = null)
    {
        connection.Audit("get_last_report", new { filter });
        var report = TestRunReport.Load(Path.Combine(settings.OutputDirectory, "report.json"));
        if (report is null)
            return "No report yet. Call run_tests first.";
        if (filter is not null)
            report.Scenarios.RemoveAll(s => !Glob.IsMatch(s.ScenarioId, filter));
        return Summarize(report);
    }

    [McpServerTool(Name = "approve_baselines", Destructive = true), Description("Approves current screenshots of failed or missing visual checks as new baselines. Requires the server to run with --allow-approve and confirm=true.")]
    public string ApproveBaselines([Description("Scenario filter")] string filter, [Description("Must be true to confirm the change")] bool confirm = false)
    {
        connection.Audit("approve_baselines", new { filter, confirm, allowed = settings.AllowApprove });
        if (!settings.AllowApprove)
            return "Refused: start the MCP server with 'workbench mcp --allow-approve' to permit baseline changes.";
        if (!confirm)
            return "Refused: pass confirm=true after a human has reviewed the visual differences.";
        var report = TestRunReport.Load(Path.Combine(settings.OutputDirectory, "report.json"));
        if (report is null)
            return "No report yet. Call run_tests with visual=true first.";
        var approved = new BaselineStore(settings.BaselineDirectory).Approve(report, settings.OutputDirectory, id => Glob.IsMatch(id, filter));
        return Json(new { approved = approved.Select(a => a.File), count = approved.Count });
    }

    private static string Summarize(TestRunReport report) => Json(new
    {
        report.Summary,
        failures = report.Scenarios.Where(s => s.Status is CheckStatus.Failed or CheckStatus.Error or CheckStatus.Missing).Select(s => new
        {
            s.ScenarioId,
            s.Status,
            s.Message,
            checks = s.Checks.Where(c => c.Status is CheckStatus.Failed or CheckStatus.Error or CheckStatus.Missing).Select(c => new
            {
                c.Kind,
                c.Name,
                c.Variant,
                c.Message,
                failedSteps = c.Steps?.Where(st => st.Status == CheckStatus.Failed),
                blockingFindings = c.Findings?.Where(f => f.Blocking),
                visual = c.Visual is null ? null : new { c.Visual.DiffRatio, c.Visual.SizeMismatch, c.Visual.ActualPath, c.Visual.DiffPath },
            }),
            errors = s.Diagnostics.Where(d => d.Severity == Diagnostics.DiagnosticSeverity.Error).Take(5).Select(d => $"[{d.Source}] {d.Message}"),
        }),
    });
}
