using System.Diagnostics.Metrics;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>What one component type did while a profile was recorded.</summary>
/// <param name="Component">The component type (full name).</param>
/// <param name="ParameterUpdates">How often it received parameters (<c>SetParametersAsync</c>): its first render plus every re-render caused by its parent.</param>
/// <param name="ParameterMs">Time spent in <c>SetParametersAsync</c>, including <c>OnInitialized</c>/<c>OnParametersSet</c> and the render it triggers.</param>
/// <param name="MaxParameterMs">The slowest single parameter update.</param>
public sealed record ComponentRenderStats(string Component, int ParameterUpdates, double ParameterMs, double MaxParameterMs);

/// <summary>An event handler that ran while a profile was recorded.</summary>
public sealed record EventHandlerStats(string Component, string? Method, string? Attribute, int Count, double TotalMs, double MaxMs);

/// <summary>The result of a render profile of a preview.</summary>
/// <param name="Available">False when the host does not publish Blazor's component metrics (see <see cref="Note"/>).</param>
/// <param name="DurationMs">How long the profile recorded.</param>
/// <param name="Batches">Render batches (one diff of every component that changed, sent to the browser).</param>
/// <param name="BatchMs">Time spent computing those diffs.</param>
/// <param name="BatchEdits">The total size of the diffs (render tree edits).</param>
/// <param name="Components">Parameter updates by component type, most first.</param>
/// <param name="Events">Event handlers by component, method and attribute, slowest first.</param>
/// <param name="Note">Why nothing could be recorded, or null.</param>
public sealed record RenderProfileResult(
    bool Available,
    double DurationMs,
    int Batches,
    double BatchMs,
    long BatchEdits,
    IReadOnlyList<ComponentRenderStats> Components,
    IReadOnlyList<EventHandlerStats> Events,
    string? Note);

/// <summary>
/// Records Blazor's built-in component metrics (.NET 10: <c>Microsoft.AspNetCore.Components</c> and
/// <c>Microsoft.AspNetCore.Components.Lifecycle</c> meters) for one preview: parameter updates per component type, event
/// handler durations and render batches. Measurements of other renderers in the same process (the Workbench shell, other
/// previews) are told apart by the renderer's synchronization context when the measurement is taken on it; Workbench's
/// own components are always left out.
/// </summary>
public sealed class RenderProfiler : IDisposable
{
    internal const string ComponentsMeter = "Microsoft.AspNetCore.Components";
    internal const string LifecycleMeter = "Microsoft.AspNetCore.Components.Lifecycle";
    internal const string ParametersInstrument = "aspnetcore.components.update_parameters.duration";
    internal const string EventInstrument = "aspnetcore.components.handle_event.duration";
    internal const string BatchDurationInstrument = "aspnetcore.components.render_diff.duration";
    internal const string BatchSizeInstrument = "aspnetcore.components.render_diff.size";
    private const string TypeTag = "aspnetcore.components.type";
    private const string AttributeTag = "aspnetcore.components.attribute.name";
    private const int MaxComponents = 500;

    private static readonly Lock s_lock = new();
    private static readonly List<RenderProfiler> s_active = [];
    private static MeterListener? s_listener;
    private static int s_activeCount;
    private static readonly HashSet<string> s_instrumentsSeen = new(StringComparer.Ordinal);

    private readonly Lock _lock = new();
    private readonly SynchronizationContext? _context;
    private readonly long _started = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly Dictionary<string, (int Count, double Total, double Max)> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Component, string? Method, string? Attribute), (int Count, double Total, double Max)> _events = [];
    private int _batches;
    private double _batchMs;
    private long _batchEdits;
    private long? _stopped;

    /// <param name="rendererContext">The synchronization context of the preview's renderer, captured while it renders; null when unknown.</param>
    public RenderProfiler(SynchronizationContext? rendererContext)
    {
        _context = rendererContext;
        lock (s_lock)
        {
            s_listener ??= CreateListener();
            s_active.Add(this);
            s_activeCount = s_active.Count;
        }
    }

    /// <summary>
    /// Starts listening to the component meters. Blazor decides whether to measure a component's parameter updates when the
    /// component is created, so this must run before the preview renders its component (ScenarioHost does), not only
    /// when a profile starts. Measurements are dropped right away while nothing records.
    /// </summary>
    public static void EnsureListening()
    {
        if (Volatile.Read(ref s_listener) is not null)
            return;
        lock (s_lock)
            s_listener ??= CreateListener();
    }

    /// <summary>True when the component meters have been published in this process (a host that registered Blazor's metrics).</summary>
    public static bool MetricsPublished
    {
        get { lock (s_lock) return s_instrumentsSeen.Count > 0; }
    }

    public bool IsRecording => _stopped is null;

    /// <summary>Stops recording (idempotent) and returns what was recorded.</summary>
    public RenderProfileResult Stop()
    {
        lock (s_lock)
        {
            s_active.Remove(this);
            s_activeCount = s_active.Count;
        }

        lock (_lock)
        {
            _stopped ??= System.Diagnostics.Stopwatch.GetTimestamp();
            var components = _components
                .Select(c => new ComponentRenderStats(c.Key, c.Value.Count, Math.Round(c.Value.Total, 3), Math.Round(c.Value.Max, 3)))
                .OrderByDescending(c => c.ParameterUpdates).ThenByDescending(c => c.ParameterMs)
                .ToArray();
            var events = _events
                .Select(e => new EventHandlerStats(e.Key.Component, e.Key.Method, e.Key.Attribute, e.Value.Count, Math.Round(e.Value.Total, 3), Math.Round(e.Value.Max, 3)))
                .OrderByDescending(e => e.TotalMs)
                .ToArray();
            var available = MetricsPublished;
            return new RenderProfileResult(available, Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(_started, _stopped.Value).TotalMilliseconds, 1),
                _batches, Math.Round(_batchMs, 3), _batchEdits, components, events,
                available ? null : "This host does not publish Blazor's component metrics. Interactive Server hosts do by default; other hosts need services.AddMetrics() and services.AddComponentsMetrics() (Workbench adds them), and trimmed WebAssembly apps need <MetricsSupport>true</MetricsSupport>.");
        }
    }

    public void Dispose() => Stop();

    private static MeterListener CreateListener()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name is ComponentsMeter or LifecycleMeter &&
                    instrument.Name is ParametersInstrument or EventInstrument or BatchDurationInstrument or BatchSizeInstrument)
                {
                    lock (s_lock)
                        s_instrumentsSeen.Add(instrument.Name);
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Dispatch(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Dispatch(instrument.Name, value, tags));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Dispatch(instrument.Name, value, tags));
        listener.Start();
        return listener;
    }

    private static void Dispatch(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (Volatile.Read(ref s_activeCount) == 0)
            return;
        string? type = null, method = null, attribute = null;
        foreach (var tag in tags)
        {
            if (tag.Key == TypeTag) type = tag.Value as string;
            else if (tag.Key == AttributeTag) attribute = tag.Value as string;
            else if (tag.Key.EndsWith(".method", StringComparison.Ordinal) || tag.Key == "code.function.name" || tag.Key == "code.function") method = tag.Value as string;
        }

        // The shell and the preview infrastructure are Workbench's own components: never part of a profile.
        if (type is not null && IsWorkbenchType(type))
            return;

        RenderProfiler[] active;
        lock (s_lock)
        {
            if (s_active.Count == 0)
                return;
            active = [.. s_active];
        }

        var current = SynchronizationContext.Current;
        var onRenderer = current is not null && current.GetType().Name.Contains("Renderer", StringComparison.Ordinal);
        foreach (var profiler in active)
        {
            // Taken on another renderer's context: another circuit (the shell, another preview). Off a renderer context
            // (an async completion on the thread pool) it cannot be told apart and counts for every recording profile.
            if (onRenderer && profiler._context is not null && !ReferenceEquals(current, profiler._context))
                continue;
            profiler.Record(instrument, value, type, method, attribute);
        }
    }

    private static readonly string[] s_workbenchNamespaces =
    [
        "BlazorWorkbench.Components.Shell.", "BlazorWorkbench.Components.Preview.", "BlazorWorkbench.Components.Runtime.",
        "BlazorWorkbench.Hosting.", "BlazorWorkbench.Hybrid.", "BlazorWorkbench.Maui.",
    ];

    internal static bool IsWorkbenchType(string type) => s_workbenchNamespaces.Any(n => type.StartsWith(n, StringComparison.Ordinal));

    private void Record(string instrument, double value, string? type, string? method, string? attribute)
    {
        lock (_lock)
        {
            if (_stopped is not null)
                return;
            switch (instrument)
            {
                case ParametersInstrument when type is not null:
                    if (_components.Count >= MaxComponents && !_components.ContainsKey(type))
                        return;
                    var ms = value * 1000;
                    var c = _components.GetValueOrDefault(type);
                    _components[type] = (c.Count + 1, c.Total + ms, Math.Max(c.Max, ms));
                    break;
                case EventInstrument when type is not null:
                    var key = (type, method, attribute);
                    var eventMs = value * 1000;
                    var e = _events.GetValueOrDefault(key);
                    _events[key] = (e.Count + 1, e.Total + eventMs, Math.Max(e.Max, eventMs));
                    break;
                case BatchDurationInstrument:
                    _batches++;
                    _batchMs += value * 1000;
                    break;
                case BatchSizeInstrument:
                    _batchEdits += (long)value;
                    break;
            }
        }
    }
}
