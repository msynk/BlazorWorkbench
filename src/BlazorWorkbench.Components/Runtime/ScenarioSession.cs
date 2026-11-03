using System.Globalization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Diagnostics;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>Per-activation overrides supplied by the toolbar (they never modify the scenario definition).</summary>
public sealed record ActivationOptions(string? Culture = null, string? Theme = null, TextDirection? Direction = null);

/// <summary>Describes where a service used by the scenario comes from.</summary>
public sealed record ServiceSourceInfo(string ServiceType, string Source, string Implementation);

/// <summary>
/// The scoped state of one scenario preview (one browser tab / circuit, or one bUnit context): the active scenario,
/// its simulated environment, diagnostics, callbacks and gates. A session is activated once; reset means a new session.
/// </summary>
public sealed class ScenarioSession : IAsyncDisposable, IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, ServiceDescriptor> _overrides = [];
    private readonly Dictionary<Type, object?> _instances = [];
    private readonly List<object> _owned = [];
    private readonly HashSet<Type> _resolvedEarly = [];
    private readonly CancellationTokenSource _disposed = new();
    private bool _isDisposed;

    public ScenarioSession(WorkbenchCatalogAccessor catalogs, IEnumerable<IDiagnosticCollector> collectors)
    {
        Catalogs = catalogs;
        Redactor = new Redactor(catalogs.Options.Redaction);
        Diagnostics = new DiagnosticLog(catalogs.Options.MaxDiagnostics, Redactor, collectors, SessionId);
        Diagnostics.Added += e => WorkbenchTelemetry.Diagnostics.Add(1,
            new KeyValuePair<string, object?>("workbench.source", e.Source.ToString()),
            new KeyValuePair<string, object?>("workbench.severity", e.Severity.ToString()));
        Http = new WorkbenchHttpMessageHandler(this);
        Auth = new WorkbenchAuthenticationStateProvider(this);
        Time = new SessionTimeProvider(this);
    }

    public WorkbenchCatalogAccessor Catalogs { get; }

    /// <summary>Correlates diagnostics, traces and reports of one preview session.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Server processing time of circuit activities (Interactive Server only).</summary>
    public ServerActivityMetrics ServerMetrics { get; } = new();
    public bool IsActive => Scenario is not null;
    public ScenarioDefinition? Scenario { get; private set; }
    public ComponentEntry? Component { get; private set; }
    public ScenarioContext? Context { get; private set; }
    public ActivationOptions Activation { get; private set; } = new();
    public ScenarioGates Gates { get; } = new();
    public ScenarioActivityTracker Activity { get; } = new();
    public DiagnosticLog Diagnostics { get; }
    public CallbackLog Callbacks { get; } = new();
    public Redactor Redactor { get; }
    public WorkbenchHttpMessageHandler Http { get; }
    public WorkbenchAuthenticationStateProvider Auth { get; }
    public SessionTimeProvider Time { get; }

    /// <summary>The fixed clock when the scenario declares one (advance it from the Environment panel or code).</summary>
    public FakeTimeProvider? Clock { get; private set; }

    public CultureInfo? Culture { get; private set; }
    public CancellationToken Disposed => _disposed.Token;
    public string ScenarioId => Scenario?.Id ?? "";

    /// <summary>Raised after activation.</summary>
    public event Action? Activated;

    /// <summary>Activates <paramref name="scenario"/>. Must run before components that use overridden services are created.</summary>
    public void Activate(ScenarioDefinition scenario, ActivationOptions? options = null, IServiceProvider? services = null)
    {
        using var trace = WorkbenchTelemetry.ActivitySource.StartActivity("workbench.scenario.activate");
        trace?.SetTag("workbench.scenario.id", scenario.Id);
        trace?.SetTag("workbench.session.id", SessionId);
        lock (_lock)
        {
            if (IsActive)
                throw new InvalidOperationException($"The session already runs '{ScenarioId}'. Each activation needs a new session (reload the preview).");

            var catalog = Catalogs.Catalog;
            Scenario = scenario;
            Component = catalog.GetComponentOf(scenario);
            Activation = options ?? new ActivationOptions();

            var env = scenario.Environment;
            if (env.Clock is { } now)
            {
                Clock = new FakeTimeProvider(now);
                Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
            }

            var cultureName = Activation.Culture ?? env.Culture;
            Culture = cultureName is { Length: > 0 } ? CultureInfo.GetCultureInfo(cultureName) : null;

            // WebAssembly runs one preview per document on a single thread: the session and culture are process-wide.
            if (OperatingSystem.IsBrowser())
            {
                ScenarioSessionContext.Browser = this;
                if (Culture is not null)
                {
                    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
                    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
                }
            }

            Context = new ScenarioContext(scenario, Time, Gates, Activity) { Services = services };

            foreach (var rule in env.Http?.Rules ?? [])
            {
                if (rule.Gate is not null)
                    Gates.Declare(rule.Gate);
            }

            var collection = new ServiceCollection();
            try
            {
                foreach (var configure in scenario.ServiceConfigurations)
                    configure(collection, Context);
            }
            catch (Exception ex)
            {
                Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Error, scenario.Id, $"Scenario service configuration failed: {ex.Message}", ex.ToString());
            }

            foreach (var descriptor in collection)
            {
                if (descriptor.ServiceType == typeof(TimeProvider))
                {
                    Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Error, scenario.Id,
                        "TimeProvider cannot be overridden with Services(...); use .Clock(...) to freeze the scenario clock.");
                    continue;
                }

                _overrides[descriptor.ServiceType] = descriptor;
                if (!Catalogs.OverridableTypes.Contains(descriptor.ServiceType) && Catalogs.OverridableTypes.Count > 0)
                    Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Error, scenario.Id,
                        $"The override for {Naming.FormatTypeName(descriptor.ServiceType)} was added after the host started and is not applied. Restart the Workbench host.");
                if (_resolvedEarly.Contains(descriptor.ServiceType))
                    Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Warning, scenario.Id,
                        $"{Naming.FormatTypeName(descriptor.ServiceType)} was resolved before the scenario was activated; the override may not reach every consumer.");
            }

            Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Info, scenario.Id,
                $"Activated {scenario.Id} (seed {Context.Seed}{(Clock is not null ? $", clock {Clock.GetUtcNow():O}" : "")}{(Culture is not null ? $", culture {Culture.Name}" : "")}).");
        }

        Auth.NotifyChanged();
        Activated?.Invoke();
    }

    /// <summary>Resolves a service through the scenario (used by the overridable registrations).</summary>
    internal object? Resolve(Type serviceType, IServiceProvider services)
    {
        lock (_lock)
        {
            if (IsActive && _overrides.TryGetValue(serviceType, out var descriptor))
            {
                if (descriptor.Lifetime != ServiceLifetime.Transient && _instances.TryGetValue(serviceType, out var cached))
                    return cached;

                var instance = Create(descriptor, services);
                if (descriptor.Lifetime != ServiceLifetime.Transient)
                    _instances[serviceType] = instance;
                return instance;
            }

            if (!IsActive)
                _resolvedEarly.Add(serviceType);
        }

        // Built-in simulations are stable instances whose behavior follows the session, so resolving them early is safe.
        if (serviceType == typeof(HttpClient))
            return Own(new HttpClient(Http, disposeHandler: false) { BaseAddress = new Uri(Scenario?.Environment.Http?.BaseAddress ?? HttpSimulation.DefaultBaseAddress) });
        if (serviceType == typeof(IHttpClientFactory))
            return new WorkbenchHttpClientFactory(this);
        if (serviceType == typeof(AuthenticationStateProvider))
            return Auth;

        return services.GetKeyedService(serviceType, ServiceOverrides.OriginalKey)
               ?? throw new InvalidOperationException(
                   $"{Naming.FormatTypeName(serviceType)} is not registered by the host and the active scenario ({(IsActive ? ScenarioId : "none")}) does not provide it. " +
                   $"Provide it with .Service<{Naming.FormatTypeName(serviceType)}>(ctx => ...) in the scenario or register it in the Workbench host.");
    }

    /// <summary>Lists real, simulated and overridden services relevant to the scenario.</summary>
    public IReadOnlyList<ServiceSourceInfo> DescribeServices()
    {
        var result = new List<ServiceSourceInfo>();
        lock (_lock)
        {
            foreach (var (type, descriptor) in _overrides.OrderBy(o => o.Key.FullName, StringComparer.Ordinal))
            {
                var impl = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType() ??
                           (_instances.TryGetValue(type, out var instance) ? instance?.GetType() : null);
                result.Add(new(Naming.FormatTypeName(type), "Scenario override", impl is null ? "factory" : Naming.FormatTypeName(impl)));
            }
        }

        var http = Scenario?.Environment.Http;
        result.Add(new("HttpClient / IHttpClientFactory", "Simulated", Scenario?.Environment.Offline == true
            ? "Offline (all requests fail)"
            : $"Fake backend, {http?.Rules.Count ?? 0} rule(s), unmatched → {(int)(http?.UnmatchedStatus ?? System.Net.HttpStatusCode.NotFound)}"));
        result.Add(new("AuthenticationStateProvider", "Simulated", Scenario?.Environment.User?.ToString() ?? "Anonymous (unspecified)"));
        result.Add(new("TimeProvider", Clock is null ? "Real" : "Simulated", Clock is null ? "System clock" : $"Fixed at {Clock.GetUtcNow():O}"));

        if (Component is not null)
        {
            foreach (var injection in Component.Metadata.Injections)
            {
                if (result.Any(r => r.ServiceType == injection.ServiceTypeName) || injection.ServiceTypeName is "HttpClient" or "IHttpClientFactory" or "AuthenticationStateProvider" or "TimeProvider")
                    continue;
                result.Add(new(injection.ServiceTypeName, "Host registration", "Real (from the Workbench host)"));
            }
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (!BeginDispose(out var owned))
            return;

        foreach (var item in owned)
        {
            try
            {
                if (item is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (item is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception ex)
            {
                Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Warning, ScenarioId, $"Disposing {item.GetType().Name} failed: {ex.Message}");
            }
        }

        EndDispose();
    }

    /// <summary>Synchronous disposal for containers disposed with <c>Dispose()</c>. Async-only services are disposed synchronously.</summary>
    public void Dispose()
    {
        if (!BeginDispose(out var owned))
            return;

        foreach (var item in owned)
        {
            try
            {
                if (item is IDisposable disposable) disposable.Dispose();
                else if (item is IAsyncDisposable asyncDisposable) asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Diagnostics.Add(DiagnosticSource.Scenario, DiagnosticSeverity.Warning, ScenarioId, $"Disposing {item.GetType().Name} failed: {ex.Message}");
            }
        }

        EndDispose();
    }

    private bool BeginDispose(out object[] owned)
    {
        lock (_lock)
        {
            owned = [];
            if (_isDisposed)
                return false;
            _isDisposed = true;
            owned = [.. Enumerable.Reverse(_owned)];
            _owned.Clear();
            _instances.Clear();
        }

        // Cancel held and in-flight fake requests so nothing continues in a closed preview.
        _disposed.Cancel();
        return true;
    }

    // The cancellation source is intentionally not disposed: late requests may still link to its (canceled) token.
    private void EndDispose() => Http.Dispose();

    private object Create(ServiceDescriptor descriptor, IServiceProvider services)
    {
        if (descriptor.IsKeyedService)
            throw new InvalidOperationException("Keyed services cannot be overridden by scenarios.");
        if (descriptor.ImplementationInstance is { } existing)
            return existing; // Supplied instances are owned by the scenario author (like Microsoft.Extensions.DependencyInjection).
        var created = descriptor.ImplementationFactory is { } factory
            ? factory(services)
            : ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);
        return Own(created);
    }

    private T Own<T>(T instance) where T : notnull
    {
        if (instance is IDisposable or IAsyncDisposable)
        {
            lock (_lock)
                _owned.Add(instance);
        }

        return instance;
    }
}

/// <summary>Callback invocations recorded for event callback parameters that the scenario did not handle.</summary>
public sealed class CallbackLog
{
    private readonly Lock _lock = new();
    private readonly List<CallbackEntry> _entries = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public event Action<CallbackEntry>? Added;

    public CallbackEntry Add(string name, string summary)
    {
        CallbackEntry entry;
        lock (_lock)
        {
            _counts[name] = _counts.GetValueOrDefault(name) + 1;
            entry = new CallbackEntry(DateTimeOffset.UtcNow, name, summary, _counts[name]);
            _entries.Add(entry);
            if (_entries.Count > 500)
                _entries.RemoveAt(0);
        }

        Added?.Invoke(entry);
        return entry;
    }

    public int Count(string name)
    {
        lock (_lock)
            return _counts.GetValueOrDefault(name);
    }

    public IReadOnlyList<CallbackEntry> Snapshot()
    {
        lock (_lock)
            return _entries.ToArray();
    }
}

public sealed record CallbackEntry(DateTimeOffset Timestamp, string Name, string Arguments, int Invocation);

/// <summary>
/// Tracks the session of the current circuit activity. Set by the circuit handler for every inbound activity and by
/// <c>ScenarioHost</c> while rendering, so singleton services (such as <see cref="WorkbenchTimeProvider"/>) can follow the
/// scenario that is executing.
/// </summary>
public static class ScenarioSessionContext
{
    private static readonly AsyncLocal<ScenarioSession?> s_current = new();

    public static ScenarioSession? Current
    {
        get => s_current.Value ?? Browser;
        set => s_current.Value = value;
    }

    /// <summary>The single active session of a WebAssembly preview document.</summary>
    public static ScenarioSession? Browser { get; internal set; }
}

/// <summary>Singleton <see cref="TimeProvider"/> that uses the scenario clock of the current activity, otherwise the host's provider.</summary>
public sealed class WorkbenchTimeProvider(TimeProvider fallback) : TimeProvider
{
    private TimeProvider Inner => ScenarioSessionContext.Current?.Clock ?? fallback;

    public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
    public override TimeZoneInfo LocalTimeZone => Inner.LocalTimeZone;
    public override long TimestampFrequency => Inner.TimestampFrequency;
    public override long GetTimestamp() => Inner.GetTimestamp();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Inner.CreateTimer(callback, state, dueTime, period);
}

/// <summary>A <see cref="TimeProvider"/> that follows the session: the scenario's fixed clock when declared, otherwise the system clock.</summary>
public sealed class SessionTimeProvider(ScenarioSession session) : TimeProvider
{
    private TimeProvider Inner => session.Clock ?? System;

    public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
    public override TimeZoneInfo LocalTimeZone => Inner.LocalTimeZone;
    public override long TimestampFrequency => Inner.TimestampFrequency;
    public override long GetTimestamp() => Inner.GetTimestamp();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Inner.CreateTimer(callback, state, dueTime, period);
}
