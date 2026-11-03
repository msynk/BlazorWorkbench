using BlazorWorkbench.Catalog;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>
/// Makes service types replaceable per scenario. Each overridable registration is rewritten to a scoped factory that asks
/// the <see cref="ScenarioSession"/> of the current scope; the host's original registration is kept as a keyed service
/// (<see cref="OriginalKey"/>) and used when the scenario does not override the service.
/// </summary>
/// <remarks>
/// Overridden services become scoped. Host singletons that depend on an overridden service are not supported
/// (the container reports them at startup in Development). Override the consumer instead.
/// </remarks>
public static class ServiceOverrides
{
    public const string OriginalKey = "BlazorWorkbench:original";

    /// <summary>Service types always simulated by the preview (as scoped services).</summary>
    public static IReadOnlyList<Type> SimulatedTypes { get; } = [typeof(HttpClient), typeof(IHttpClientFactory), typeof(AuthenticationStateProvider)];

    /// <summary>Rewrites registrations for <paramref name="types"/>. Call after all host registrations.</summary>
    public static IReadOnlySet<Type> Apply(IServiceCollection services, IEnumerable<Type> types)
    {
        var applied = new HashSet<Type>();
        foreach (var type in types.Distinct())
        {
            if (type.ContainsGenericParameters || !applied.Add(type))
                continue;

            var existing = services.Where(d => d.ServiceType == type && !d.IsKeyedService).ToList();
            foreach (var descriptor in existing)
                services.Remove(descriptor);

            if (existing.LastOrDefault() is { } original)
                services.Add(ToKeyed(original));

            services.Add(ServiceDescriptor.Scoped(type, sp => sp.GetRequiredService<ScenarioSession>().Resolve(type, sp)!));
        }

        // TimeProvider is consumed by framework singletons (Kestrel), so it stays a singleton that follows the session
        // of the current circuit activity (see ScenarioSessionContext).
        var originalTime = services.LastOrDefault(d => d.ServiceType == typeof(TimeProvider) && !d.IsKeyedService);
        services.RemoveAll<TimeProvider>();
        if (originalTime is not null)
            services.Add(ToKeyed(originalTime));
        services.AddSingleton<TimeProvider>(sp => new WorkbenchTimeProvider(sp.GetKeyedService<TimeProvider>(OriginalKey) ?? TimeProvider.System));

        // The framework casts AuthenticationStateProvider to IHostEnvironmentAuthenticationStateProvider at circuit start.
        services.RemoveAll<IHostEnvironmentAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => sp.GetRequiredService<ScenarioSession>().Auth);
        return applied;
    }

    /// <summary>All service types that scenarios in <paramref name="catalog"/> override, plus the simulated types.</summary>
    public static IEnumerable<Type> CollectTypes(WorkbenchCatalog catalog) =>
        SimulatedTypes.Concat(catalog.Scenarios.SelectMany(s => s.ServiceTypes)).Where(t => t != typeof(TimeProvider)).Distinct();

    private static ServiceDescriptor ToKeyed(ServiceDescriptor d)
    {
        if (d.ImplementationInstance is { } instance)
            return new ServiceDescriptor(d.ServiceType, OriginalKey, instance);
        if (d.ImplementationFactory is { } factory)
            return new ServiceDescriptor(d.ServiceType, OriginalKey, (sp, _) => factory(sp), d.Lifetime);
        return new ServiceDescriptor(d.ServiceType, OriginalKey, d.ImplementationType!, d.Lifetime);
    }
}

/// <summary>Registration helpers shared by the preview host and bUnit integration.</summary>
public static class WorkbenchRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Workbench runtime (catalog, sessions, simulations). <paramref name="catalog"/> is the catalog built at
    /// startup; pass null to build it lazily.
    /// </summary>
    public static IServiceCollection AddBlazorWorkbenchRuntime(this IServiceCollection services, WorkbenchOptions options, WorkbenchCatalog? catalog = null, IReadOnlySet<Type>? overridableTypes = null)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton(sp => new WorkbenchCatalogAccessor(options, catalog, sp.GetService<IServiceProviderIsService>())
        {
            OverridableTypes = overridableTypes ?? new HashSet<Type>(),
        });
        services.TryAddScoped<ScenarioSession>();
        services.TryAddScoped(sp => sp.GetRequiredService<ScenarioSession>().Gates);
        services.TryAddScoped(sp => sp.GetRequiredService<ScenarioSession>().Context
                                    ?? throw new InvalidOperationException("ScenarioContext is available only while a scenario is active."));
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        return services;
    }
}
