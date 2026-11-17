using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BlazorWorkbench.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using DiagnosticSource = BlazorWorkbench.Diagnostics.DiagnosticSource;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>A recorded JavaScript interop call (arguments are redacted and shortened).</summary>
/// <param name="Timestamp">When the call started.</param>
/// <param name="Identifier">The function or module export.</param>
/// <param name="Module">The module path for calls on an imported module.</param>
/// <param name="Arguments">The arguments as JSON (references and streams by type name).</param>
/// <param name="Outcome"><c>planned</c>, <c>browser</c>, <c>error</c>, <c>unplanned</c> or <c>held at gate …</c>.</param>
/// <param name="Rule">The rule that answered the call, if any.</param>
/// <param name="DurationMs">Time until the call completed.</param>
public sealed record JSCallRecord(DateTimeOffset Timestamp, string Identifier, string? Module, string Arguments, string Outcome, string? Rule, double DurationMs);

/// <summary>
/// Answers JavaScript interop calls from the scenario's <see cref="JSInteropSimulation"/> and records every call that
/// components make. Components receive a decorated <see cref="IJSRuntime"/> once the scenario is active; the framework and
/// the Workbench bridge keep the original runtime.
/// </summary>
public sealed class JSInteropSimulator
{
    private const int MaxCalls = 200;
    private readonly ScenarioSession _session;
    private readonly Lock _lock = new();
    private readonly Dictionary<JSInteropRule, int> _hits = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<JSCallRecord> _calls = [];

    internal JSInteropSimulator(ScenarioSession session) => _session = session;

    /// <summary>Raised after a call completes (on the thread that completed it).</summary>
    public event Action<JSCallRecord>? Called;

    /// <summary>The most recent calls, oldest first.</summary>
    public IReadOnlyList<JSCallRecord> Calls
    {
        get
        {
            lock (_lock)
                return _calls.ToArray();
        }
    }

    /// <summary>How often the component called <paramref name="identifier"/> (global function or module export).</summary>
    public int Count(string identifier)
    {
        lock (_lock)
            return _counts.GetValueOrDefault(identifier);
    }

    private JSInteropSimulation? Simulation => _session.Scenario?.Environment.JSInterop;

    /// <summary>Decorates <paramref name="inner"/>; the result implements <see cref="IJSInProcessRuntime"/> when the inner runtime does.</summary>
    public IJSRuntime Wrap(IJSRuntime inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner switch
        {
            WorkbenchJSRuntime => inner,
            IJSInProcessRuntime inProcess => new WorkbenchInProcessJSRuntime(this, inProcess),
            _ => new WorkbenchJSRuntime(this, inner),
        };
    }

    /// <summary>
    /// Resolves <see cref="IJSRuntime"/> for the container rewrite in <see cref="ServiceOverrides.Apply"/>: the original runtime
    /// until the scope's scenario is active (the framework initializes it when the circuit or page starts), then the decorator.
    /// </summary>
    internal static IJSRuntime Resolve(IServiceProvider services)
    {
        var inner = services.GetRequiredKeyedService<IJSRuntime>(ServiceOverrides.OriginalKey);
        ScenarioSession? session;
        try
        {
            session = services.GetService<ScenarioSession>();
        }
        catch (InvalidOperationException)
        {
            // Resolved from the root provider (scope validation): no scenario applies there.
            return inner;
        }

        return session is { IsActive: true } && session.Catalogs.Options.InterceptJSInterop ? session.JSInterop.Wrap(inner) : inner;
    }

    internal async ValueTask<TValue> InvokeAsync<TValue>(IJSRuntime runtime, string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        args ??= [];
        var call = new JSCall(identifier, null, args);
        var rule = Match(call);
        if (rule is null && identifier == "import" && args is [string path, ..] && Simulation?.HasModuleRules(path) == true)
        {
            if (!typeof(TValue).IsAssignableFrom(typeof(WorkbenchJSModule)))
            {
                // For example IJSInProcessObjectReference (WebAssembly, Hybrid): the planned module cannot stand in for it,
                // so the real module is imported and the planned answers do not apply.
                Record(call, "real module", "import " + path, Stopwatch.StartNew(), DiagnosticSeverity.Warning,
                    $"The component imports {path} as {typeof(TValue).Name}, which a planned module cannot provide: the real module is used and the module's planned answers do not apply.");
                return await runtime.InvokeAsync<TValue>(identifier, cancellationToken, args).ConfigureAwait(false);
            }

            Record(call, "planned module", "import " + path, Stopwatch.StartNew(), DiagnosticSeverity.Info);
            var module = new WorkbenchJSModule(this, JSInteropRule.NormalizeModule(path), ct => runtime.InvokeAsync<IJSObjectReference>("import", ct, args));
            return (TValue)(object)module;
        }

        return await AnswerAsync<TValue>(call, rule, ct => runtime.InvokeAsync<TValue>(identifier, ct, args), cancellationToken).ConfigureAwait(false);
    }

    internal TValue Invoke<TValue>(IJSInProcessRuntime runtime, string identifier, object?[]? args)
    {
        args ??= [];
        var call = new JSCall(identifier, null, args);
        var rule = Match(call);
        var watch = Stopwatch.StartNew();
        if (rule is null)
        {
            ThrowIfUnplanned(call, watch);
            try
            {
                var result = runtime.Invoke<TValue>(identifier, args);
                Record(call, "browser", null, watch, null);
                return result;
            }
            catch (JSException ex)
            {
                Record(call, "error", null, watch, DiagnosticSeverity.Warning, $"JavaScript call {identifier} failed: {ex.Message}");
                throw;
            }
        }

        if (rule.Gate is not null || rule.Delay is not null)
            _session.Diagnostics.Add(DiagnosticSource.JsInterop, DiagnosticSeverity.Warning, _session.ScenarioId,
                $"The rule for {rule} delays or holds calls, which synchronous calls (IJSInProcessRuntime.Invoke) cannot do; it answered at once.");
        return Planned<TValue>(call, rule, watch);
    }

    internal ValueTask<TValue> InvokeModuleAsync<TValue>(WorkbenchJSModule module, string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        args ??= [];
        var call = new JSCall(identifier, module.Path, args);
        return AnswerAsync<TValue>(call, Match(call), async ct =>
        {
            var real = await module.GetRealModuleAsync(ct).ConfigureAwait(false);
            return await real.InvokeAsync<TValue>(identifier, ct, args).ConfigureAwait(false);
        }, cancellationToken);
    }

    private async ValueTask<TValue> AnswerAsync<TValue>(JSCall call, JSInteropRule? rule, Func<CancellationToken, ValueTask<TValue>> browser, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        if (rule is null)
        {
            ThrowIfUnplanned(call, watch);
            try
            {
                var result = await browser(cancellationToken).ConfigureAwait(false);
                Record(call, "browser", null, watch, null);
                return result;
            }
            catch (JSException ex)
            {
                Record(call, "error", null, watch, DiagnosticSeverity.Warning, $"JavaScript call {Describe(call)} failed: {ex.Message}");
                throw;
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _session.Disposed);
        var activity = _session.Activity.Begin();
        try
        {
            if (rule.Gate is { } gate)
            {
                // Waiting at a gate is intentional: the scenario is ready in its pending state.
                activity.Dispose();
                _session.Diagnostics.Add(DiagnosticSource.JsInterop, DiagnosticSeverity.Info, _session.ScenarioId, $"JavaScript call {Describe(call)} is held at gate '{gate}'.");
                try
                {
                    await _session.Gates.WaitAsync(gate, linked.Token).ConfigureAwait(false);
                }
                catch (GateFailedException ex)
                {
                    Record(call, "gate failed", rule.ToString(), watch, DiagnosticSeverity.Warning);
                    throw new JSException($"Simulated failure released by gate '{gate}': {ex.Message}");
                }
                finally
                {
                    activity = _session.Activity.Begin();
                }
            }

            if (rule.Delay is { } delay && delay > TimeSpan.Zero)
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            return Planned<TValue>(call, rule, watch);
        }
        finally
        {
            activity.Dispose();
        }
    }

    private TValue Planned<TValue>(JSCall call, JSInteropRule rule, Stopwatch watch)
    {
        if (rule.Error is { } error)
        {
            Record(call, "planned error", rule.ToString(), watch, DiagnosticSeverity.Info, $"JavaScript call {Describe(call)} failed as planned: {error}");
            throw new JSException(error);
        }

        object? value;
        try
        {
            value = rule.Handler is { } handler ? handler(call, _session.Context!) : rule.Result;
        }
        catch (Exception ex) when (ex is not JSException)
        {
            Record(call, "error", rule.ToString(), watch, DiagnosticSeverity.Error, $"The handler for JavaScript call {Describe(call)} failed: {ex.Message}");
            throw new JSException($"The scenario's handler for {call.Identifier} failed: {ex.Message}", ex);
        }

        TValue result;
        try
        {
            result = Convert<TValue>(value);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidCastException)
        {
            Record(call, "error", rule.ToString(), watch, DiagnosticSeverity.Error,
                $"The planned answer for {Describe(call)} cannot be converted to {Naming.FormatTypeName(typeof(TValue))}: {ex.Message}");
            throw new JSException($"The planned answer for {call.Identifier} cannot be converted to {Naming.FormatTypeName(typeof(TValue))}.", ex);
        }

        Record(call, "planned", rule.ToString(), watch, DiagnosticSeverity.Info);
        return result;
    }

    /// <summary>Converts a planned value to the type the component asked for (directly, else through JSON).</summary>
    internal static TValue Convert<TValue>(object? value)
    {
        if (typeof(TValue) == typeof(IJSVoidResult) || value is null)
            return default!;
        if (value is TValue typed)
            return typed;
        var element = value is JsonElement json ? json : JsonSerializer.SerializeToElement(value, value.GetType(), JsonSerializerOptions.Web);
        return element.Deserialize<TValue>(JsonSerializerOptions.Web)!;
    }

    private void ThrowIfUnplanned(JSCall call, Stopwatch watch)
    {
        if (Simulation is not { Strict: true } || call.Module is null && (call.Identifier.StartsWith("Blazor.", StringComparison.Ordinal) || IsFrameworkImport(call)))
            return;
        Record(call, "unplanned", null, watch, DiagnosticSeverity.Error,
            $"Unplanned JavaScript call {Describe(call)} in a strict scenario. Add .JSInterop(js => js.{(call.Module is null ? "" : $"Module(\"{call.Module}\", m => m.")}Setup(\"{call.Identifier}\")...){(call.Module is null ? "" : ")")}.");
        throw new JSException($"Unplanned JavaScript interop call '{Describe(call)}' (the scenario's JavaScript interop simulation is strict).");
    }

    // Framework components (QuickGrid, the Virtualize and InputFile helpers) import their own modules; strict scenarios allow it.
    private static bool IsFrameworkImport(JSCall call) =>
        call.Identifier == "import" && call.Arguments is [string path, ..] &&
        path.Replace("./", "", StringComparison.Ordinal).TrimStart('/').StartsWith("_content/Microsoft.AspNetCore.", StringComparison.Ordinal);

    private JSInteropRule? Match(JSCall call)
    {
        if (Simulation is not { } simulation)
            return null;
        lock (_lock)
        {
            foreach (var rule in simulation.Rules)
            {
                bool matched;
                try
                {
                    matched = rule.IsMatch(call);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _session.Diagnostics.Add(DiagnosticSource.JsInterop, DiagnosticSeverity.Error, _session.ScenarioId, $"The condition of JavaScript rule {rule} failed: {ex.Message}");
                    continue;
                }

                if (!matched)
                    continue;
                var hits = _hits.GetValueOrDefault(rule);
                if (rule.Times is { } times && hits >= times)
                    continue;
                _hits[rule] = hits + 1;
                return rule;
            }
        }

        return null;
    }

    private void Record(JSCall call, string outcome, string? rule, Stopwatch watch, DiagnosticSeverity? severity, string? message = null)
    {
        var arguments = _session.Redactor.RedactText(JSInteropFormat.Arguments(call.Arguments));
        var record = new JSCallRecord(DateTimeOffset.UtcNow, call.Identifier, call.Module, arguments, outcome, rule, Math.Round(watch.Elapsed.TotalMilliseconds, 1));
        lock (_lock)
        {
            // The bridge's own calls never come through here: only calls made by the scenario's components are counted.
            _counts[call.Identifier] = _counts.GetValueOrDefault(call.Identifier) + 1;
            _calls.Add(record);
            if (_calls.Count > MaxCalls)
                _calls.RemoveAt(0);
        }

        WorkbenchTelemetry.JSInteropCalls.Add(1, new KeyValuePair<string, object?>("workbench.outcome", outcome));
        if (severity is { } level)
        {
            _session.Diagnostics.Add(DiagnosticSource.JsInterop, level, _session.ScenarioId,
                message ?? $"JavaScript call {Describe(call)}({arguments}) → {outcome} ({record.DurationMs.ToString(CultureInfo.InvariantCulture)} ms){(rule is null ? "" : $" [{rule}]")}",
                data: new Dictionary<string, string> { ["identifier"] = call.Identifier, ["outcome"] = outcome });
        }

        Called?.Invoke(record);
    }

    private static string Describe(JSCall call) => call.Module is null ? call.Identifier : $"{call.Module} → {call.Identifier}";
}

/// <summary>The <see cref="IJSRuntime"/> handed to components of an active scenario.</summary>
internal class WorkbenchJSRuntime(JSInteropSimulator simulator, IJSRuntime inner) : IJSRuntime
{
    protected JSInteropSimulator Simulator { get; } = simulator;

    public IJSRuntime Inner { get; } = inner;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        Simulator.InvokeAsync<TValue>(Inner, identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        Simulator.InvokeAsync<TValue>(Inner, identifier, cancellationToken, args);

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args) => Inner.InvokeConstructorAsync(identifier, args);

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        Inner.InvokeConstructorAsync(identifier, cancellationToken, args);

    public ValueTask<TValue> GetValueAsync<TValue>(string identifier) => Inner.GetValueAsync<TValue>(identifier);

    public ValueTask<TValue> GetValueAsync<TValue>(string identifier, CancellationToken cancellationToken) => Inner.GetValueAsync<TValue>(identifier, cancellationToken);

    public ValueTask SetValueAsync<TValue>(string identifier, TValue value) => Inner.SetValueAsync(identifier, value);

    public ValueTask SetValueAsync<TValue>(string identifier, TValue value, CancellationToken cancellationToken) => Inner.SetValueAsync(identifier, value, cancellationToken);
}

/// <summary>The decorator for in-process runtimes (WebAssembly, Blazor Hybrid), so <c>IJSInProcessRuntime</c> casts keep working.</summary>
internal sealed class WorkbenchInProcessJSRuntime(JSInteropSimulator simulator, IJSInProcessRuntime inner) : WorkbenchJSRuntime(simulator, inner), IJSInProcessRuntime
{
    private IJSInProcessRuntime InProcess => (IJSInProcessRuntime)Inner;

    public TResult Invoke<TResult>(string identifier, params object?[]? args) => Simulator.Invoke<TResult>(InProcess, identifier, args);

    public IJSInProcessObjectReference InvokeConstructor(string identifier, object?[]? args) => InProcess.InvokeConstructor(identifier, args);

    public TValue GetValue<TValue>(string identifier) => InProcess.GetValue<TValue>(identifier);

    public void SetValue<TValue>(string identifier, TValue value) => InProcess.SetValue(identifier, value);
}

/// <summary>
/// A .NET stand-in for a JavaScript module with planned exports. Exports without a rule are called on the real module,
/// which is imported on first use.
/// </summary>
internal sealed class WorkbenchJSModule(JSInteropSimulator simulator, string path, Func<CancellationToken, ValueTask<IJSObjectReference>> import) : IJSObjectReference
{
    // Never disposed: an import can still be waiting on it when the module is disposed, and a SemaphoreSlim that never
    // allocated a wait handle holds no unmanaged resources.
    private readonly SemaphoreSlim _importLock = new(1, 1);
    private IJSObjectReference? _real;
    private int _disposed;

    public string Path { get; } = path;

    internal async ValueTask<IJSObjectReference> GetRealModuleAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _real) is { } existing)
            return existing;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _importLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _real) is { } imported)
                return imported;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var module = await import(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _real, module);
            // Disposed while importing: DisposeAsync may have run before the module was stored, so release it here.
            // Both sides use full fences, so at least one of them sees the module and Exchange lets only one dispose it.
            if (Volatile.Read(ref _disposed) != 0)
            {
                if (Interlocked.Exchange(ref _real, null) is { } orphan)
                    await DisposeRealAsync(orphan).ConfigureAwait(false);
                throw new ObjectDisposedException(GetType().FullName);
            }

            return module;
        }
        finally
        {
            _importLock.Release();
        }
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => simulator.InvokeModuleAsync<TValue>(this, identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        simulator.InvokeModuleAsync<TValue>(this, identifier, cancellationToken, args);

    public async ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args) =>
        await (await GetRealModuleAsync(CancellationToken.None).ConfigureAwait(false)).InvokeConstructorAsync(identifier, args).ConfigureAwait(false);

    public async ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        await (await GetRealModuleAsync(cancellationToken).ConfigureAwait(false)).InvokeConstructorAsync(identifier, cancellationToken, args).ConfigureAwait(false);

    public async ValueTask<TValue> GetValueAsync<TValue>(string identifier) =>
        await (await GetRealModuleAsync(CancellationToken.None).ConfigureAwait(false)).GetValueAsync<TValue>(identifier).ConfigureAwait(false);

    public async ValueTask<TValue> GetValueAsync<TValue>(string identifier, CancellationToken cancellationToken) =>
        await (await GetRealModuleAsync(cancellationToken).ConfigureAwait(false)).GetValueAsync<TValue>(identifier, cancellationToken).ConfigureAwait(false);

    public async ValueTask SetValueAsync<TValue>(string identifier, TValue value) =>
        await (await GetRealModuleAsync(CancellationToken.None).ConfigureAwait(false)).SetValueAsync(identifier, value).ConfigureAwait(false);

    public async ValueTask SetValueAsync<TValue>(string identifier, TValue value, CancellationToken cancellationToken) =>
        await (await GetRealModuleAsync(cancellationToken).ConfigureAwait(false)).SetValueAsync(identifier, value, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (Interlocked.Exchange(ref _real, null) is { } real)
            await DisposeRealAsync(real).ConfigureAwait(false);
    }

    private static async ValueTask DisposeRealAsync(IJSObjectReference module)
    {
        try { await module.DisposeAsync().ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
    }
}
