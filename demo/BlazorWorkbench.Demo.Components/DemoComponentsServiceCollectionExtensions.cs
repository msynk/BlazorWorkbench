using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SampleComponents.Account;

namespace SampleComponents;

/// <summary>Registers what the sample components need, the way a real component library would.</summary>
public static class SampleComponentsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the authorization policies of <see cref="WorkspacePolicies"/> and a default <see cref="IFeatureFlags"/> (every
    /// feature off). Call it in the app and in every Workbench host; scenarios then override individual services.
    /// </summary>
    public static IServiceCollection AddSampleComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthorizationCore(WorkspacePolicies.Configure);
        services.TryAddSingleton<IFeatureFlags>(StaticFeatureFlags.None);
        return services;
    }
}
