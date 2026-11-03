using System.Reflection.Metadata;
using BlazorWorkbench.Catalog;
using Microsoft.Extensions.DependencyInjection;

[assembly: MetadataUpdateHandler(typeof(BlazorWorkbench.Components.Runtime.WorkbenchHotReload))]

namespace BlazorWorkbench.Components.Runtime;

/// <summary>
/// Owns the current <see cref="WorkbenchCatalog"/>. The catalog is built once and rebuilt lazily after hot reload,
/// so new scenarios appear without restarting <c>dotnet watch</c>.
/// </summary>
public sealed class WorkbenchCatalogAccessor : IDisposable
{
    private readonly Lock _lock = new();
    private readonly WorkbenchOptions _options;
    private readonly IServiceProviderIsService? _isService;
    private WorkbenchCatalog? _catalog;

    public WorkbenchCatalogAccessor(WorkbenchOptions options, WorkbenchCatalog? initial = null, IServiceProviderIsService? isService = null)
    {
        _options = options;
        _isService = isService;
        _catalog = initial is null ? null : WithDependencies(initial);
        WorkbenchHotReload.Updated += OnHotReload;
    }

    /// <summary>Raised after hot reload invalidates the catalog.</summary>
    public event Action? Changed;

    public WorkbenchOptions Options => _options;

    /// <summary>Service types that were made overridable at startup (scenario overrides of other types need a restart).</summary>
    public IReadOnlySet<Type> OverridableTypes { get; init; } = new HashSet<Type>();

    public WorkbenchCatalog Catalog
    {
        get
        {
            lock (_lock)
                return _catalog ??= WithDependencies(CatalogBuilder.Build(_options));
        }
    }

    public void Invalidate()
    {
        lock (_lock)
            _catalog = null;
        Changed?.Invoke();
    }

    public void Dispose() => WorkbenchHotReload.Updated -= OnHotReload;

    private void OnHotReload() => Invalidate();

    private WorkbenchCatalog WithDependencies(WorkbenchCatalog catalog) =>
        _isService is null ? catalog : CatalogBuilder.ValidateDependencies(catalog, _isService.IsService);
}

/// <summary>Receives .NET hot reload notifications (registered through <see cref="MetadataUpdateHandlerAttribute"/>).</summary>
public static class WorkbenchHotReload
{
    public static event Action? Updated;

    public static void ClearCache(Type[]? updatedTypes)
    {
    }

    public static void UpdateApplication(Type[]? updatedTypes) => Updated?.Invoke();
}
