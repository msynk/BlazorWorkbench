using System.Globalization;

namespace BlazorWorkbench;

/// <summary>
/// Per-activation state available to fixture, parameter, cascading value and service factories.
/// A new context is created every time a scenario is activated (opened, reset, or executed in automation).
/// </summary>
public sealed class ScenarioContext
{
    public ScenarioContext(ScenarioDefinition scenario, TimeProvider time, ScenarioGates gates, ScenarioActivityTracker activity)
    {
        Scenario = scenario;
        Time = time;
        Gates = gates;
        Activity = activity;
        Seed = scenario.Environment.Seed ?? StableHash.Compute(scenario.Id);
        Random = new Random(Seed);
        Culture = scenario.Environment.Culture is { Length: > 0 } c ? CultureInfo.GetCultureInfo(c) : CultureInfo.CurrentCulture;
    }

    public ScenarioDefinition Scenario { get; }
    public string ScenarioId => Scenario.Id;

    /// <summary>Deterministic random source seeded from the scenario (see <see cref="EnvironmentDefinition.Seed"/>).</summary>
    public Random Random { get; }

    public int Seed { get; }

    /// <summary>The scenario clock. Fixed when <see cref="EnvironmentDefinition.Clock"/> is set.</summary>
    public TimeProvider Time { get; }

    /// <summary>Named gates used to control asynchronous completion.</summary>
    public ScenarioGates Gates { get; }

    /// <summary>Tracks pending asynchronous operations; the preview is not ready while operations are pending.</summary>
    public ScenarioActivityTracker Activity { get; }

    public CultureInfo Culture { get; }

    /// <summary>The service provider of the preview (available while rendering; null during static validation).</summary>
    public IServiceProvider? Services { get; internal set; }

    /// <summary>The session's diagnostic log, so simulated services can report what the component asked them to do.</summary>
    public Diagnostics.DiagnosticLog? Diagnostics { get; internal set; }
}

/// <summary>Deterministic string hash (stable across processes, unlike <see cref="string.GetHashCode()"/>).</summary>
public static class StableHash
{
    public static int Compute(string value)
    {
        unchecked
        {
            // FNV-1a 32 bit.
            var hash = (int)2166136261;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash & 0x7FFFFFFF;
        }
    }
}

/// <summary>The observable state of a gate.</summary>
public enum GateStatus
{
    Pending,
    Released,
    Failed,
}

/// <summary>A snapshot of a gate.</summary>
public sealed record GateSnapshot(string Name, GateStatus Status, int Waiters, string? FailureMessage);

/// <summary>Raised when a gate is failed. Fake HTTP rules translate it into a network failure.</summary>
public sealed class GateFailedException(string gate, string? message)
    : Exception(message ?? $"Gate '{gate}' was failed by the developer or test.")
{
    public string Gate { get; } = gate;
}

/// <summary>
/// Named latches that let developers and tests decide exactly when asynchronous work completes.
/// Waiting on a gate does not count as pending activity, so a scenario held at a gate is "ready" in its loading state.
/// Gates are latched: once released or failed, later waits complete immediately until <see cref="Reset"/> is called.
/// </summary>
public sealed class ScenarioGates
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, GateState> _gates = new(StringComparer.Ordinal);

    /// <summary>Raised (on an arbitrary thread) whenever a gate is created, awaited, released or failed.</summary>
    public event Action? Changed;

    public async Task WaitAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        GateState state;
        lock (_lock)
        {
            state = GetOrCreate(name);
            state.Waiters++;
        }

        Changed?.Invoke();
        try
        {
            await state.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
                state.Waiters--;
            Changed?.Invoke();
        }
    }

    /// <summary>Declares a gate so it is listed before anything waits on it.</summary>
    public void Declare(string name)
    {
        lock (_lock)
            GetOrCreate(name);
        Changed?.Invoke();
    }

    public bool Release(string name)
    {
        bool changed;
        lock (_lock)
        {
            var state = GetOrCreate(name);
            changed = state.Completion.TrySetResult();
            if (changed)
                state.Status = GateStatus.Released;
        }

        Changed?.Invoke();
        return changed;
    }

    public bool Fail(string name, string? message = null)
    {
        bool changed;
        lock (_lock)
        {
            var state = GetOrCreate(name);
            changed = state.Completion.TrySetException(new GateFailedException(name, message));
            if (changed)
            {
                state.Status = GateStatus.Failed;
                state.FailureMessage = message;
            }
        }

        Changed?.Invoke();
        return changed;
    }

    /// <summary>Releases every pending gate.</summary>
    public void ReleaseAll()
    {
        string[] names;
        lock (_lock)
            names = _gates.Where(g => g.Value.Status == GateStatus.Pending).Select(g => g.Key).ToArray();
        foreach (var name in names)
            Release(name);
    }

    /// <summary>Returns a gate to the pending state. Existing waiters stay attached to the previous completion.</summary>
    public void Reset(string name)
    {
        lock (_lock)
            _gates[name] = new GateState();
        Changed?.Invoke();
    }

    public IReadOnlyList<GateSnapshot> Snapshot()
    {
        lock (_lock)
        {
            return _gates
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new GateSnapshot(g.Key, g.Value.Status, g.Value.Waiters, g.Value.FailureMessage))
                .ToArray();
        }
    }

    private GateState GetOrCreate(string name)
    {
        if (!_gates.TryGetValue(name, out var state))
            _gates[name] = state = new GateState();
        return state;
    }

    private sealed class GateState
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GateStatus Status { get; set; } = GateStatus.Pending;
        public int Waiters { get; set; }
        public string? FailureMessage { get; set; }
    }
}

/// <summary>
/// Counts pending asynchronous operations started by Workbench-controlled infrastructure (fake HTTP, fixtures).
/// Readiness checks wait until the count drops to zero instead of relying on fixed delays.
/// </summary>
public sealed class ScenarioActivityTracker
{
    private int _pending;

    public event Action<int>? Changed;

    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Marks an operation as pending until the returned handle is disposed.</summary>
    public IDisposable Begin()
    {
        var value = Interlocked.Increment(ref _pending);
        Changed?.Invoke(value);
        return new Handle(this);
    }

    /// <summary>Tracks a task until it completes.</summary>
    public async Task<T> Track<T>(Task<T> task)
    {
        using (Begin())
            return await task.ConfigureAwait(false);
    }

    private void End()
    {
        var value = Interlocked.Decrement(ref _pending);
        Changed?.Invoke(value);
    }

    private sealed class Handle(ScenarioActivityTracker owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.End();
        }

        /// <summary>Temporarily stops counting while waiting at a gate.</summary>
        internal void Suspend() => Dispose();
    }
}
