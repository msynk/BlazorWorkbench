using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Preview;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Hosting.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorWorkbench.Hosting;

/// <summary>Registers and maps the Workbench preview host.</summary>
public static class WorkbenchHostingExtensions
{
    /// <summary>
    /// Adds Blazor Workbench: discovers components and scenarios, registers Interactive Server rendering and makes
    /// scenario service overrides possible. Call it after your own service registrations.
    /// </summary>
    public static IHostApplicationBuilder AddBlazorWorkbench(this IHostApplicationBuilder builder, Action<WorkbenchHostOptions>? configure = null)
    {
        var options = new WorkbenchHostOptions();
        if (Assembly.GetEntryAssembly() is { } entry)
        {
            options.ScenarioAssemblies.Add(entry);
            options.Title = entry.GetName().Name is { } name ? $"{name}" : options.Title;
        }

        configure?.Invoke(options);

        var config = WorkbenchConfig.Find(builder.Environment.ContentRootPath);
        options.BaselineDirectory ??= config?.Resolve(config.Baselines);
        options.AccessibilityFailOn ??= config?.Accessibility.FailOn;
        if (options.AccessibilityTags.Count == 0 && config is not null)
        {
            foreach (var tag in config.Accessibility.Tags)
                options.AccessibilityTags.Add(tag);
        }
        options.ResultsDirectory ??= config?.Resolve(config.Output);
        options.SourceRoot ??= config?.RootDirectory ?? builder.Environment.ContentRootPath;
        // Editor links reveal absolute paths: offer them by default only to the developer on this machine.
        if (options.EditorUrlTemplate is null && builder.Environment.IsDevelopment() && !options.AllowRemoteAccess)
            options.EditorUrlTemplate = "vscode://file/{path}:{line}";

        if (options.AllowRemoteAccess && options.RemoteAccessPolicy is null && !options.AllowAnonymousRemoteAccess)
            throw new InvalidOperationException(
                "Blazor Workbench remote access requires an authorization policy. Set RemoteAccessPolicy (and configure authentication), " +
                "or set AllowAnonymousRemoteAccess = true for an isolated network.");

        // Discovery happens once at startup; the catalog is rebuilt lazily after hot reload.
        var catalog = CatalogBuilder.Build(options);
        var overridable = ServiceOverrides.CollectTypes(catalog).ToHashSet();

        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<WorkbenchOptions>(options);
        var razor = services.AddRazorComponents().AddInteractiveServerComponents(o => o.DetailedErrors = builder.Environment.IsDevelopment());
        if (options.Interactivity == WorkbenchInteractivity.WebAssembly)
            razor.AddInteractiveWebAssemblyComponents();
        services.AddBlazorWorkbenchRuntime(options, catalog, overridable, validateDependencies: options.Interactivity == WorkbenchInteractivity.Server);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, WorkbenchCircuitHandler>());
        services.TryAddSingleton<IVisualResultsProvider, FileVisualResultsProvider>();
        services.TryAddSingleton<ITestResultsProvider, FileTestResultsProvider>();
        services.TryAddSingleton<IAccessibilityPolicyProvider, FileAccessibilityPolicyProvider>();
        if (options.ShowChanges)
            services.TryAddSingleton<IChangesProvider, GitChangesProvider>();

        // Rewrite overridable registrations right before the container is built, after every host registration.
        var development = builder.Environment.IsDevelopment();
        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = development, ValidateOnBuild = development }),
            collection => ServiceOverrides.Apply(collection, overridable));
        return builder;
    }

    /// <summary>Maps the Workbench UI, preview pages, static assets and the <c>/_workbench/api</c> endpoints.</summary>
    public static WebApplication MapBlazorWorkbench(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<WorkbenchHostOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BlazorWorkbench");
        if (!app.Environment.IsDevelopment() && !options.AllowNonDevelopmentEnvironments)
            throw new InvalidOperationException(
                $"Blazor Workbench is a development tool and refuses to start in the '{app.Environment.EnvironmentName}' environment. " +
                "Set AllowNonDevelopmentEnvironments = true for a protected catalog server.");

        app.UseMiddleware<WorkbenchSecurityMiddleware>();
        app.UseAntiforgery();
        app.MapStaticAssets();

        var catalogs = app.Services.GetRequiredService<WorkbenchCatalogAccessor>();
        var catalog = catalogs.Catalog;
        foreach (var issue in catalog.Issues.Where(i => i.Severity == IssueSeverity.Error))
            logger.LogWarning("Workbench {Code}: {Message} {Remedy}", issue.Code, issue.Message, issue.Remedy);
        logger.LogInformation("Blazor Workbench: {Components} components, {Scenarios} scenarios, {Errors} errors.",
            catalog.Components.Count, catalog.Scenarios.Count, catalog.Issues.Count(i => i.Severity == IssueSeverity.Error));

        var razor = app.MapRazorComponents<WorkbenchApp>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(typeof(WorkbenchShell).Assembly);
        if (options.Interactivity == WorkbenchInteractivity.WebAssembly)
            razor.AddInteractiveWebAssemblyRenderMode();

        var api = app.MapGroup("/_workbench");
        MapApi(api, options);

        if (options.AllowRemoteAccess && options.RemoteAccessPolicy is { } policy)
        {
            razor.RequireAuthorization(policy);
            api.RequireAuthorization(policy);
        }

        return app;
    }

    private static void MapApi(RouteGroupBuilder group, WorkbenchHostOptions options)
    {
        group.MapGet("/api/health", (WorkbenchCatalogAccessor c) => Results.Json(new
        {
            status = "ok",
            hostingModel = options.Interactivity.ToString(),
            components = c.Catalog.Components.Count,
            scenarios = c.Catalog.Scenarios.Count,
            version = Version,
            approveFromUi = options.CanApproveFromUi && options.BaselineDirectory is not null && options.ResultsDirectory is not null,
        }, WorkbenchJson.Options));

        group.MapGet("/api/catalog", (WorkbenchCatalogAccessor c) =>
            Results.Json(CatalogMapper.ToDto(c.Catalog, path => RelativeSource(path, options), Version, options.Interactivity.ToString()), WorkbenchJson.Options));

        group.MapGet("/api/validate", (WorkbenchCatalogAccessor c) =>
            Results.Json(CatalogMapper.ToValidationReport(c.Catalog), WorkbenchJson.Options));

        group.MapGet("/api/scenarios/{id}", (string id, WorkbenchCatalogAccessor c) =>
        {
            var scenario = c.Catalog.FindScenario(id);
            if (scenario is null)
                return Results.NotFound(new { error = $"Scenario '{id}' was not found." });
            var component = c.Catalog.GetComponentOf(scenario)!;
            return Results.Json(new
            {
                scenario = CatalogMapper.ToDto(scenario, component.Metadata),
                issues = c.Catalog.IssuesFor(scenario),
            }, WorkbenchJson.Options);
        });

        // XML documentation for WebAssembly clients, limited to the configured assemblies.
        group.MapGet("/api/xmldoc/{assembly}", (string assembly) =>
        {
            var known = options.Assemblies.Concat(options.ScenarioAssemblies).Select(a => a.GetName().Name).ToHashSet(StringComparer.Ordinal);
            if (!known.Contains(assembly))
                return Results.NotFound();
            var path = Path.Combine(AppContext.BaseDirectory, assembly + ".xml");
            return File.Exists(path) ? Results.File(path, "application/xml") : Results.NotFound();
        });

        group.MapGet("/api/results", async (ITestResultsProvider provider, CancellationToken ct) =>
            Results.Json(await provider.GetSummaryAsync(ct), WorkbenchJson.Options));

        // Read-only: the explorer's test runner asks for the accessibility policy of the scenarios it is about to run.
        group.MapPost("/api/accessibility-policy", async (string[] ids, IAccessibilityPolicyProvider provider, CancellationToken ct) =>
            Results.Json(await provider.GetAsync(ids.Take(5000).ToArray(), ct), WorkbenchJson.Options));

        group.MapGet("/api/changes", async (IServiceProvider services, CancellationToken ct) =>
            Results.Json(services.GetService<IChangesProvider>() is { } provider ? await provider.GetChangesAsync(ct) : CatalogChangeSummary.Empty, WorkbenchJson.Options));

        group.MapGet("/api/visual/{id}", async (string id, IVisualResultsProvider provider, CancellationToken ct) =>
            Results.Json(await provider.GetAsync(id, ct), WorkbenchJson.Options));

        // Approving writes baselines. Browsers send a custom header only for same-origin requests here (there is no CORS
        // policy, so a cross-site page fails the preflight), which keeps other sites from approving through a visitor's browser.
        group.MapPost("/api/visual/{id}/approve", async (string id, string? variant, HttpContext http, IVisualResultsProvider provider, CancellationToken ct) =>
        {
            if (!http.Request.Headers.TryGetValue("X-Workbench-Request", out var marker) || marker != "1" ||
                http.Request.Headers.TryGetValue("Sec-Fetch-Site", out var site) && site != "same-origin")
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!await provider.CanApproveAsync(ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Json(await provider.ApproveAsync(id, variant, ct), WorkbenchJson.Options);
        });

        group.MapGet("/axe.min.js", () =>
        {
            var assembly = typeof(Deque.AxeCore.Commons.AxeResult).Assembly;
            var stream = assembly.GetManifestResourceStream("Deque.AxeCore.Commons.Resources.axe.min.js");
            return stream is null ? Results.NotFound() : Results.Stream(stream, "text/javascript");
        });

        group.MapGet("/visual/{root}/{**path}", (string root, string path) =>
        {
            var directory = root switch
            {
                "baselines" => options.BaselineDirectory,
                "results" => options.ResultsDirectory,
                _ => null,
            };
            if (directory is null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound();
            var full = Path.GetFullPath(Path.Combine(directory, path));
            if (!PathSafety.IsInside(full, directory) || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, "image/png");
        });
    }

    internal static string? RelativeSource(string path, WorkbenchHostOptions options)
    {
        if (options.SourceRoot is not { } root)
            return null;
        var relative = Path.GetRelativePath(root, path);
        // Never publish absolute or out-of-tree paths.
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(path) : relative.Replace('\\', '/');
    }

    internal static string Version { get; } =
        typeof(WorkbenchHostingExtensions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}

/// <summary>Applies the scenario culture to every inbound circuit activity (events, JS interop, rendering).</summary>
internal sealed class WorkbenchCircuitHandler(ScenarioSession session) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            ScenarioSessionContext.Current = session;
            if (session.Culture is { } culture)
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (session.IsActive)
                {
                    session.ServerMetrics.Record(elapsed);
                    WorkbenchTelemetry.ActivityDuration.Record(elapsed);
                }
            }
        };
}

/// <summary>
/// Restricts Workbench to loopback clients that address it by a local host name (unless remote access is configured) and
/// adds defensive headers that the host has not set itself.
/// </summary>
internal sealed class WorkbenchSecurityMiddleware(RequestDelegate next, WorkbenchHostOptions options)
{
    internal const string DefaultContentSecurityPolicy = "frame-ancestors 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        if (!options.AllowRemoteAccess)
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return ForbidAsync(context, "Blazor Workbench accepts local connections only. Enable AllowRemoteAccess with an authorization policy to share it.");

            // A loopback connection can still come from a browser that a foreign site pointed at 127.0.0.1 through DNS
            // rebinding (evil.example resolving to 127.0.0.1). Such requests carry the foreign name in the Host header.
            if (!IsLocalHost(context.Request.Host))
                return ForbidAsync(context, "Blazor Workbench answers requests addressed to localhost, 127.0.0.1 or [::1] only. Enable AllowRemoteAccess with an authorization policy to use another host name.");
        }

        // Applied when the response starts, so headers the host sets (before or after this middleware) are never overwritten.
        context.Response.OnStarting(static state =>
        {
            AddDefaultHeaders(((HttpResponse)state).Headers);
            return Task.CompletedTask;
        }, context.Response);
        return next(context);
    }

    internal static void AddDefaultHeaders(IHeaderDictionary headers)
    {
        if (!headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.XContentTypeOptions))
            headers.XContentTypeOptions = "nosniff";
        if (!headers.ContainsKey("Referrer-Policy"))
            headers["Referrer-Policy"] = "no-referrer";
        if (!headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentSecurityPolicy))
            headers.ContentSecurityPolicy = DefaultContentSecurityPolicy;
    }

    /// <summary>True for <c>localhost</c>, <c>*.localhost</c> and loopback IP addresses, with any port.</summary>
    internal static bool IsLocalHost(HostString host)
    {
        if (!host.HasValue)
            return false;
        var name = host.Host;
        if (name.Length > 2 && name[0] == '[' && name[^1] == ']')
            name = name[1..^1];
        name = name.TrimEnd('.');
        if (name.Length == 0)
            return false;
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(name, out var ip) && IPAddress.IsLoopback(ip));
    }

    private static Task ForbidAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return context.Response.WriteAsync(message);
    }
}

/// <summary>Reads the status of every scenario from the last CLI run (<c>report.json</c> in the results directory).</summary>
/// <summary>The accessibility policy from the options (filled from <c>workbench.json</c>) and the accessibility baselines.</summary>
internal sealed class FileAccessibilityPolicyProvider(WorkbenchHostOptions options, WorkbenchCatalogAccessor catalogs) : IAccessibilityPolicyProvider
{
    public Task<AccessibilityPolicy> GetAsync(IReadOnlyList<string> scenarioIds, CancellationToken cancellationToken = default)
    {
        var known = new Dictionary<string, IReadOnlyList<AccessibilityBaseline.KnownViolation>>(StringComparer.OrdinalIgnoreCase);
        if (options.BaselineDirectory is { } directory)
        {
            // Only scenarios of the catalog: ids become file names.
            foreach (var id in scenarioIds.Where(id => catalogs.Catalog.FindScenario(id) is not null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (AccessibilityBaseline.Load(directory, id) is { Violations.Count: > 0 } baseline)
                    known[id] = baseline.Violations;
            }
        }

        return Task.FromResult(new AccessibilityPolicy(options.AccessibilityFailOn ?? "serious", options.AccessibilityTags.ToArray(), known));
    }
}

internal sealed class FileTestResultsProvider(WorkbenchHostOptions options) : ITestResultsProvider
{
    public Task<TestRunSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (options.ResultsDirectory is not { } dir || !File.Exists(Path.Combine(dir, "report.json")))
                return Task.FromResult(TestRunSummary.Empty);
            var report = TestRunReport.Load(Path.Combine(dir, "report.json"));
            if (report is null)
                return Task.FromResult(TestRunSummary.Empty);
            var statuses = report.Scenarios
                .GroupBy(s => s.ScenarioId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new ScenarioRunStatus(g.Last().Status.ToString(), g.Last().Message, g.Last().Attempts > 1 && g.Last().Status == CheckStatus.Passed),
                    StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new TestRunSummary(report.FinishedAt, statuses));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Task.FromResult(TestRunSummary.Empty);
        }
    }
}

/// <summary>
/// Maps the changes of the git working copy (and of the branch since <see cref="WorkbenchHostOptions.ChangesBase"/>) to
/// scenarios. Results are cached briefly, because every explorer that opens asks for them.
/// </summary>
internal sealed class GitChangesProvider(WorkbenchHostOptions options, WorkbenchCatalogAccessor catalogs) : IChangesProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(3);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTimeOffset At, WorkbenchCatalog Catalog, CatalogChangeSummary Summary)? _cached;

    public async Task<CatalogChangeSummary> GetChangesAsync(CancellationToken cancellationToken = default)
    {
        var catalog = catalogs.Catalog;
        if (_cached is { } hit && ReferenceEquals(hit.Catalog, catalog) && DateTimeOffset.UtcNow - hit.At < CacheFor)
            return hit.Summary;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } again && ReferenceEquals(again.Catalog, catalog) && DateTimeOffset.UtcNow - again.At < CacheFor)
                return again.Summary;
            var root = options.SourceRoot ?? Directory.GetCurrentDirectory();
            var (files, description) = await GitWorkingCopy.ReadAsync(root, options.ChangesBase, cancellationToken).ConfigureAwait(false);
            var summary = files is null
                ? CatalogChangeSummary.Empty with { Description = description }
                : CatalogChanges.Map(catalog, files, root, description);
            _cached = (DateTimeOffset.UtcNow, catalog, summary);
            return summary;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Reads visual results from the last CLI run (<c>report.json</c>) and the baseline directory.</summary>
internal sealed class FileVisualResultsProvider(WorkbenchHostOptions options) : IVisualResultsProvider
{
    public Task<IReadOnlyList<VisualComparisonView>> GetAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        var results = new List<VisualComparisonView>();
        try
        {
            var report = options.ResultsDirectory is { } dir ? TestRunReport.Load(Path.Combine(dir, "report.json")) : null;
            var scenario = report?.Scenarios.FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            foreach (var check in scenario?.Checks.Where(c => c.Kind == CheckKind.Visual && c.Visual is not null) ?? [])
            {
                var v = check.Visual!;
                var status = check.Status is CheckStatus.Failed or CheckStatus.Missing && IsApproved(report!, scenario!.ScenarioId, v) ? "Approved" : check.Status.ToString();
                results.Add(new VisualComparisonView(v.Variant, status,
                    Url("baselines", v.BaselinePath), Url("results", v.ActualPath), Url("results", v.DiffPath),
                    v.DiffRatio, check.Message, report!.FinishedAt));
            }

            if (results.Count == 0 && options.BaselineDirectory is { } baselines && Directory.Exists(baselines))
            {
                // Without run results, show the approved baselines of every platform and hosting model.
                foreach (var platform in Directory.EnumerateDirectories(baselines).Order(StringComparer.Ordinal))
                {
                    var folder = Path.Combine(platform, scenarioId);
                    if (!Directory.Exists(folder))
                        continue;
                    foreach (var file in Directory.EnumerateFiles(folder, "*.png").Order(StringComparer.Ordinal))
                    {
                        results.Add(new VisualComparisonView($"{Path.GetFileName(platform)} · {Path.GetFileNameWithoutExtension(file)}", "Baseline",
                            Url("baselines", $"{Path.GetFileName(platform)}/{scenarioId}/{Path.GetFileName(file)}"), null, null, null, "Approved baseline (no test run results).", null));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            results.Add(new VisualComparisonView("report", "Error", null, null, null, null, $"Results could not be read: {ex.Message}", null));
        }

        return Task.FromResult<IReadOnlyList<VisualComparisonView>>(results);
    }

    public string Describe() =>
        $"Baselines: {options.BaselineDirectory ?? "(not configured)"} · Results: {options.ResultsDirectory ?? "(not configured)"}";

    public ValueTask<bool> CanApproveAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(options.CanApproveFromUi && options.BaselineDirectory is not null && options.ResultsDirectory is not null);

    public async Task<VisualApprovalResult> ApproveAsync(string scenarioId, string? variant = null, CancellationToken cancellationToken = default)
    {
        if (!await CanApproveAsync(cancellationToken).ConfigureAwait(false))
            return new VisualApprovalResult([], "Approving from the Workbench is turned off (AllowApprovalFromUi), or no baseline or results directory is configured.");
        try
        {
            var report = TestRunReport.Load(Path.Combine(options.ResultsDirectory!, "report.json"));
            if (report is null)
                return new VisualApprovalResult([], "There is no report.json from a test run. Run workbench test --visual first.");
            var approved = BaselineApproval.Approve(report, options.ResultsDirectory!, options.BaselineDirectory!,
                id => string.Equals(id, scenarioId, StringComparison.OrdinalIgnoreCase), variant: variant);
            return new VisualApprovalResult(approved.Select(a => a.File).ToArray(), approved.Count == 0
                ? "Nothing to approve: no failed or missing visual check of this scenario has a screenshot."
                : $"Approved {approved.Count} baseline(s). Review and commit them with the change that caused them.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new VisualApprovalResult([], $"Approving failed: {ex.Message}");
        }
    }

    /// <summary>True when the run's screenshot is already the approved baseline (approved after the run).</summary>
    private bool IsApproved(TestRunReport report, string scenarioId, VisualComparison visual)
    {
        if (options.BaselineDirectory is not { } baselines || options.ResultsDirectory is not { } results || visual.ActualPath is null)
            return false;
        var baseline = Path.Combine(baselines, BaselineApproval.RelativePath(report.Environment.Platform ?? BaselineApproval.CurrentPlatform, scenarioId, visual.Variant));
        var actual = Path.Combine(results, visual.ActualPath);
        if (!File.Exists(baseline) || !File.Exists(actual) || new FileInfo(baseline).Length != new FileInfo(actual).Length)
            return false;
        return File.ReadAllBytes(baseline).AsSpan().SequenceEqual(File.ReadAllBytes(actual));
    }

    private static string? Url(string root, string? path) =>
        path is null ? null : $"_workbench/visual/{root}/{string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString))}";
}
