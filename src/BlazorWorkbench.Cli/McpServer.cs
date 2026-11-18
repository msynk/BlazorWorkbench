using System.ComponentModel;
using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Documentation;
using BlazorWorkbench.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ModelContextProtocol.Protocol;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    [McpServerTool(Name = "get_scenario_url", ReadOnly = true), Description("Returns the Workbench URL of a scenario (shell, with optional panel and arguments) and its bare preview URL, for a human to open or a browser tool to load.")]
    public async Task<string> GetScenarioUrl(
        [Description("Scenario id, for example 'inputs-button--primary'")] string id,
        [Description("Optional panel to open: controls, environment, actions, diagnostics, interactions, accessibility, visual, html, storage, code")] string? panel = null,
        [Description("Optional inspector arguments as a JSON object, for example {\"Disabled\":true}")] string? args = null,
        CancellationToken ct = default)
    {
        connection.Audit("get_scenario_url", new { id, panel, args });
        var host = await connection.GetAsync(ct);
        var catalog = await host.GetCatalogAsync(ct);
        var scenario = catalog.Components.SelectMany(c => c.Scenarios).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
            return $"Scenario '{id}' was not found. Use list_components.";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(panel) && panel.All(c => char.IsAsciiLetterLower(c) || c == '-'))
            query.Add("panel=" + panel);
        if (!string.IsNullOrWhiteSpace(args))
            query.Add("args=" + Uri.EscapeDataString(args));
        var shell = host.Info.IsHybrid ? null : new Uri(host.BaseAddress, $"scenario/{Uri.EscapeDataString(scenario.Id)}" + (query.Count > 0 ? "?" + string.Join('&', query) : ""));
        return Json(new { scenarioId = scenario.Id, shell, preview = host.PreviewUrl(scenario.Id, argsJson: string.IsNullOrWhiteSpace(args) ? null : args) });
    }

    [McpServerTool(Name = "screenshot_scenario", ReadOnly = true), Description("Renders a scenario in a headless browser and returns a PNG screenshot of it, so you can see what a component looks like after an edit. Optional viewport and theme names from the catalog, and inspector arguments as JSON.")]
    public async Task<IEnumerable<ContentBlock>> ScreenshotScenario(
        [Description("Scenario id, for example 'inputs-button--primary'")] string id,
        [Description("Viewport preset name, for example 'mobile' (default: the first preset)")] string? viewport = null,
        [Description("Theme name, for example 'dark' (default: the scenario's or the first theme)")] string? theme = null,
        [Description("Optional inspector arguments as a JSON object")] string? args = null,
        CancellationToken ct = default)
    {
        connection.Audit("screenshot_scenario", new { id, viewport, theme, args });
        var host = await connection.GetAsync(ct);
        if (host.Info.IsHybrid)
            return [new TextContentBlock { Text = "Screenshots of a Blazor Hybrid app are taken by run_tests with visual=true; use get_last_report for the files." }];
        var catalog = await host.GetCatalogAsync(ct);
        var scenario = catalog.Components.SelectMany(c => c.Scenarios).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
            return [new TextContentBlock { Text = $"Scenario '{id}' was not found. Use list_components." }];
        var preset = (viewport is null ? null : catalog.Viewports.FirstOrDefault(v => string.Equals(v.Name, viewport, StringComparison.OrdinalIgnoreCase)))
                     ?? CatalogVariants.DefaultViewport(catalog, null);
        var themeName = theme ?? scenario.Environment.Theme ?? CatalogVariants.DefaultTheme(catalog).Name;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = preset.Width, Height = preset.Height },
            DeviceScaleFactor = (float)preset.DeviceScaleFactor,
            IsMobile = preset.IsMobile,
            HasTouch = preset.IsMobile,
            ReducedMotion = ReducedMotion.Reduce,
            Locale = scenario.Environment.Culture ?? "en-US",
            TimezoneId = "UTC",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.PreviewUrl(scenario.Id, themeName, string.IsNullOrWhiteSpace(args) ? null : args).ToString());
        await page.WaitForFunctionAsync("() => ['ready', 'error'].includes(document.documentElement.getAttribute('data-workbench-state'))", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        var state = await page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-workbench-state')");
        var png = scenario.Layout == ScenarioLayout.Fullscreen
            ? await page.ScreenshotAsync(new PageScreenshotOptions { Animations = ScreenshotAnimations.Disabled })
            : await page.Locator("#workbench-preview-root").ScreenshotAsync(new LocatorScreenshotOptions { Animations = ScreenshotAnimations.Disabled });
        return
        [
            new TextContentBlock { Text = $"{scenario.Id} at {preset.Name} ({preset.Width}x{preset.Height}), theme {themeName}: preview {state}." },
            ImageContentBlock.FromBytes(png, "image/png"),
        ];
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

    [McpServerTool(Name = "find_component_usage", ReadOnly = true), Description("Finds where .razor files use a catalog component (file and line) and which of its parameters they set. Use it before changing or removing a parameter, or to see how a component is used in practice. Scans the workbench.json directory, or the given subdirectories of it.")]
    public async Task<string> FindComponentUsage(
        [Description("Component id, for example 'inputs-button'; omit for a summary of every component")] string? id = null,
        [Description("Directories or files to scan, relative to the workbench.json directory (default: all of it)")] string[]? paths = null,
        CancellationToken ct = default)
    {
        connection.Audit("find_component_usage", new { id, paths });
        var root = Path.GetFullPath(settings.Context.Config.RootDirectory);
        var roots = new List<string>();
        foreach (var path in paths is { Length: > 0 } ? paths : ["."])
        {
            // Only inside the Workbench root: the tool reads files, so it does not wander around the machine.
            var full = Path.GetFullPath(Path.Combine(root, path));
            var relative = Path.GetRelativePath(root, full);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                return $"'{path}' is outside the Workbench root ({root}).";
            roots.Add(full);
        }

        var catalog = await (await connection.GetAsync(ct)).GetCatalogAsync(ct);
        if (id is not null && !catalog.Components.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            return $"Component '{id}' was not found.";
        var report = ComponentUsage.Scan(catalog, roots);
        if (id is not null)
            return Json(new { report.ScannedFiles, component = report.Components.First(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)), note = "Markup tags only: components created in C# are not counted." });
        return Json(new
        {
            report.ScannedFiles,
            components = report.Components.Select(c => new { c.Id, c.Uses, c.Files, neverSet = c.Uses > 0 ? c.Parameters.Where(p => p.Value == 0).Select(p => p.Key) : [] }),
            unused = report.Unused.Select(c => c.Id),
            deprecatedInUse = report.Components.Where(c => c.Uses > 0 && c.Status is ComponentStatus.Deprecated).Select(c => c.Id),
        });
    }

    [McpServerTool(Name = "list_changed_scenarios", ReadOnly = true), Description("Lists the scenarios affected by uncommitted git changes (and, with baseRef, by the commits since that ref), including scenarios of components that render a changed component. Use it to decide what to review or test after editing components.")]
    public async Task<string> ListChangedScenarios([Description("Optional git ref to compare with in addition to uncommitted changes, for example 'origin/main'")] string? baseRef = null, CancellationToken ct = default)
    {
        connection.Audit("list_changed_scenarios", new { baseRef });
        var root = settings.Context.Config.RootDirectory;
        var (files, description) = await GitWorkingCopy.ReadAsync(root, baseRef, ct);
        if (files is null)
            return $"Changes are not available: {description}";
        var catalog = await (await connection.GetAsync(ct)).GetCatalogAsync(ct);
        var selection = ChangeImpact.Select(catalog, files.Select(f => f.Path), root, settings.BaselineDirectory);
        return Json(new
        {
            description,
            selection.Reason,
            runsEverything = selection.RunAll,
            scenarios = selection.ScenarioIds?.Order(StringComparer.Ordinal).ToArray(),
            unmappedFiles = selection.UnmappedFiles,
            newFiles = files.Where(f => f.IsNew).Select(f => Path.GetRelativePath(root, f.Path).Replace('\\', '/')).ToArray(),
        });
    }

    [McpServerTool(Name = "run_tests"), Description("Runs scenario tests in a headless browser (render health, interactions; optionally accessibility, visual comparison and a responsive sweep for horizontal overflow) and returns a summary with failures. Never modifies baselines.")]
    public async Task<string> RunTests(
        [Description("Scenario filter (glob on ids/titles, tag:name)")] string? filter = null,
        [Description("Run axe-core accessibility scans")] bool accessibility = false,
        [Description("Compare screenshots against approved baselines")] bool visual = false,
        [Description("Resize each scenario through 320 px and every breakpoint edge, failing on horizontal scrolling")] bool responsive = false,
        CancellationToken ct = default)
    {
        connection.Audit("run_tests", new { filter, accessibility, visual, responsive });
        var host = await connection.GetAsync(ct);
        // Same workbench.json settings as 'workbench test' (browser, timeout, retries, filter, anti-aliasing, axe tags).
        var options = settings.Context.CreateTestRunOptions(settings.OutputDirectory, settings.BaselineDirectory);
        options.Filter = filter ?? options.Filter;
        options.Accessibility = accessibility;
        options.Visual = visual;
        options.Responsive |= responsive;
        var report = await new WorkbenchTestRunner(host, options).RunAsync(ct);
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
        {
            // The summary counts only the scenarios that match, so it agrees with the failures listed below.
            // Infrastructure errors concern the whole run and stay counted; the duration is that of the whole run.
            report.Scenarios.RemoveAll(s => !Glob.IsMatch(s.ScenarioId, filter));
            report.Summarize();
        }

        return Summarize(report, filter);
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

    private static string Summarize(TestRunReport report, string? filter = null) => Json(new
    {
        filter,
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
