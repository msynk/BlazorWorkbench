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
        options.ResultsDirectory ??= config?.Resolve(config.Output);
        options.SourceRoot ??= config?.RootDirectory ?? builder.Environment.ContentRootPath;

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
        services.AddRazorComponents().AddInteractiveServerComponents(o => o.DetailedErrors = builder.Environment.IsDevelopment());
        services.AddBlazorWorkbenchRuntime(options, catalog, overridable);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, WorkbenchCircuitHandler>());
        services.TryAddSingleton<IVisualResultsProvider, FileVisualResultsProvider>();

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
            components = c.Catalog.Components.Count,
            scenarios = c.Catalog.Scenarios.Count,
            version = Version,
        }, WorkbenchJson.Options));

        group.MapGet("/api/catalog", (WorkbenchCatalogAccessor c) =>
            Results.Json(CatalogMapper.ToDto(c.Catalog, path => RelativeSource(path, options), Version), WorkbenchJson.Options));

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
            var rootFull = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
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
        context =>
        {
            ScenarioSessionContext.Current = session;
            if (session.Culture is { } culture)
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            return next(context);
        };
}

/// <summary>Restricts Workbench to loopback clients (unless configured otherwise) and adds defensive headers.</summary>
internal sealed class WorkbenchSecurityMiddleware(RequestDelegate next, WorkbenchHostOptions options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (!options.AllowRemoteAccess && remote is not null && !IPAddress.IsLoopback(remote))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsync("Blazor Workbench accepts local connections only. Enable AllowRemoteAccess with an authorization policy to share it.");
        }

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = "frame-ancestors 'self'";
        return next(context);
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
                results.Add(new VisualComparisonView(v.Variant, check.Status.ToString(),
                    Url("baselines", v.BaselinePath), Url("results", v.ActualPath), Url("results", v.DiffPath),
                    v.DiffRatio, check.Message, report!.FinishedAt));
            }

            if (results.Count == 0 && options.BaselineDirectory is { } baselines && Directory.Exists(Path.Combine(baselines, scenarioId)))
            {
                foreach (var file in Directory.EnumerateFiles(Path.Combine(baselines, scenarioId), "*.png").Order(StringComparer.Ordinal))
                {
                    results.Add(new VisualComparisonView(Path.GetFileNameWithoutExtension(file), "Baseline",
                        Url("baselines", $"{scenarioId}/{Path.GetFileName(file)}"), null, null, null, "Approved baseline (no test run results).", null));
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

    private static string? Url(string root, string? path) =>
        path is null ? null : $"_workbench/visual/{root}/{string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString))}";
}
