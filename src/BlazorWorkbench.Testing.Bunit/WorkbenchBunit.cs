using System.Collections.Concurrent;
using System.Reflection;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Preview;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Values;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorWorkbench.Testing.Bunit;

/// <summary>Thrown by scenario assertions.</summary>
public sealed class ScenarioAssertionException(string message) : Exception(message);

/// <summary>
/// Renders Workbench scenarios with bUnit. The scenario's services, fake HTTP backend, simulated user, clock and
/// cascading values are installed in the test context exactly as in the browser preview, without a browser.
/// </summary>
public static class WorkbenchBunit
{
    private static readonly ConcurrentDictionary<string, WorkbenchCatalog> s_catalogs = new();

    /// <summary>
    /// Builds (and caches) the catalog of the given assemblies. Assemblies that declare scenario sets or providers are
    /// scanned for scenarios only (like a Workbench host's entry assembly); the others are scanned for components.
    /// </summary>
    public static WorkbenchCatalog Catalog(params Assembly[] assemblies)
    {
        var key = string.Join('|', assemblies.Select(a => a.FullName).Order(StringComparer.Ordinal));
        return s_catalogs.GetOrAdd(key, _ =>
        {
            var options = new WorkbenchOptions();
            foreach (var assembly in assemblies.Distinct())
            {
                if (DeclaresScenarios(assembly))
                    options.ScenarioAssemblies.Add(assembly);
                else
                    options.AddAssembly(assembly);
            }

            return CatalogBuilder.Build(options);
        });
    }

    private static bool DeclaresScenarios(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes().Any(t => !t.IsAbstract && (typeof(IScenarioSet).IsAssignableFrom(t) || typeof(IScenarioProvider).IsAssignableFrom(t)));
        }
        catch (ReflectionTypeLoadException)
        {
            return false;
        }
    }

    /// <summary>Scenario ids for data-driven tests (<c>[MemberData]</c>, <c>[TestCaseSource]</c>). Scenarios marked <c>SkipTests</c> are excluded.</summary>
    public static IEnumerable<object[]> ScenarioIds(params Assembly[] assemblies) =>
        Catalog(assemblies).Scenarios.Where(s => s.SkipReason is null).Select(s => new object[] { s.Id });

    /// <summary>Renders a scenario by id from the catalog of <paramref name="assemblies"/>.</summary>
    public static IRenderedComponent<ScenarioHost> RenderScenario(this BunitContext context, string scenarioId, params Assembly[] assemblies)
    {
        var catalog = Catalog(assemblies);
        var scenario = catalog.FindScenario(scenarioId)
                       ?? throw new ArgumentException($"Scenario '{scenarioId}' was not found. Known: {string.Join(", ", catalog.Scenarios.Take(10).Select(s => s.Id))}…", nameof(scenarioId));
        return context.RenderScenario(catalog, scenario);
    }

    /// <summary>Renders the scenario with key <paramref name="key"/> (or name) declared by <typeparamref name="TSet"/>.</summary>
    public static IRenderedComponent<ScenarioHost> RenderScenario<TSet>(this BunitContext context, string key, IReadOnlyDictionary<string, ArgOverride>? args = null)
        where TSet : IScenarioSet, new()
    {
        var set = new TSet();
        var catalog = Catalog(typeof(TSet).Assembly, set.ComponentType.Assembly);
        var scenario = catalog.Scenarios.FirstOrDefault(s => s.DeclaringType == typeof(TSet) &&
                                                            (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)))
                       ?? throw new ArgumentException($"{typeof(TSet).Name} declares no scenario '{key}'.", nameof(key));
        return context.RenderScenario(catalog, scenario, args);
    }

    /// <summary>Renders <paramref name="scenario"/>. Call once per test context (each context hosts one scenario session).</summary>
    public static IRenderedComponent<ScenarioHost> RenderScenario(this BunitContext context, WorkbenchCatalog catalog, ScenarioDefinition scenario, IReadOnlyDictionary<string, ArgOverride>? args = null)
    {
        var services = context.Services;
        if (services.Any(d => d.ServiceType == typeof(ScenarioSession)))
            throw new InvalidOperationException("This test context already hosts a scenario. Use a new BunitContext per scenario.");

        var accessor = new WorkbenchCatalogAccessor(catalog.Options, catalog);
        var session = new ScenarioSession(accessor, []);
        session.Activate(scenario, services: services);

        services.AddSingleton(catalog.Options);
        services.AddSingleton(accessor);
        services.AddSingleton(session);
        services.AddSingleton(session.Gates);
        services.AddSingleton(session.Context!);
        // bUnit registers placeholders that throw for HttpClient and authorization; replace them with the real
        // authorization stack and the scenario's simulations.
        services.RemoveAll<IAuthorizationService>();
        services.RemoveAll<IAuthorizationPolicyProvider>();
        services.RemoveAll<AuthenticationStateProvider>();
        services.RemoveAll<HttpClient>();
        services.RemoveAll<IHttpClientFactory>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddSingleton<AuthenticationStateProvider>(session.Auth);
        services.AddSingleton(_ => new HttpClient(session.Http, disposeHandler: false)
        {
            BaseAddress = new Uri(scenario.Environment.Http?.BaseAddress ?? HttpSimulation.DefaultBaseAddress),
        });
        services.AddSingleton<IHttpClientFactory>(new WorkbenchHttpClientFactory(session));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(session.Time);
        foreach (var type in scenario.ServiceTypes.Where(t => t != typeof(TimeProvider)))
        {
            services.RemoveAll(type);
            services.AddSingleton(type, sp => session.Resolve(type, sp)!);
        }

        return context.Render<ScenarioHost>(p => p
            .Add(h => h.Scenario, scenario)
            .Add(h => h.InitialArgs, args)
            .Add(h => h.EnableBridge, false));
    }

    /// <summary>The scenario session of a rendered scenario (diagnostics, callbacks, gates, fake HTTP exchanges).</summary>
    public static ScenarioSession Session(this IRenderedComponent<ScenarioHost> cut) => cut.Services.GetRequiredService<ScenarioSession>();

    /// <summary>Waits until no Workbench-tracked operations (fake HTTP requests) are pending.</summary>
    public static void WaitForScenarioIdle(this IRenderedComponent<ScenarioHost> cut, TimeSpan? timeout = null)
    {
        var session = cut.Session();
        cut.WaitForState(() => session.Activity.Pending == 0, timeout ?? TimeSpan.FromSeconds(5));
    }

    /// <summary>Asserts that rendering produced no error diagnostics and no error boundary output.</summary>
    public static void AssertNoScenarioErrors(this IRenderedComponent<ScenarioHost> cut)
    {
        var session = cut.Session();
        var errors = session.Diagnostics.Snapshot().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new ScenarioAssertionException($"{session.ScenarioId}: {errors.Count} error(s). First: [{errors[0].Source}] {errors[0].Message}\n{errors[0].Details}");
        if (cut.FindAll("[data-workbench-error], .wb-preview-error").Count > 0)
            throw new ScenarioAssertionException($"{session.ScenarioId}: the scenario rendered an error panel: {cut.Find(".wb-preview-error").TextContent.Trim()}");
    }

    /// <summary>Number of times an automatically instrumented <c>EventCallback</c> parameter was invoked.</summary>
    public static int CallbackCount(this IRenderedComponent<ScenarioHost> cut, string parameterName) => cut.Session().Callbacks.Count(parameterName);
}
