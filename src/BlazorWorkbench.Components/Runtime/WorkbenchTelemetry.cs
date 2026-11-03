using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>
/// OpenTelemetry-compatible instrumentation. Enable it with <c>.AddSource(WorkbenchTelemetry.Name)</c> and
/// <c>.AddMeter(WorkbenchTelemetry.Name)</c> to export scenario activations, fake HTTP exchanges and circuit activity
/// durations to any OpenTelemetry backend.
/// </summary>
public static class WorkbenchTelemetry
{
    public const string Name = "BlazorWorkbench";

    private static readonly string Version = typeof(WorkbenchTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public static ActivitySource ActivitySource { get; } = new(Name, Version);

    public static Meter Meter { get; } = new(Name, Version);

    internal static Counter<long> HttpRequests { get; } = Meter.CreateCounter<long>("workbench.http.requests", description: "Requests answered by the fake HTTP backend.");

    internal static Counter<long> Diagnostics { get; } = Meter.CreateCounter<long>("workbench.diagnostics", description: "Diagnostics recorded by preview sessions.");

    internal static Histogram<double> ActivityDuration { get; } = Meter.CreateHistogram<double>("workbench.circuit.activity.duration", "ms",
        "Server time spent handling one inbound circuit activity (event, JS interop call or render acknowledgement).");
}

/// <summary>Server-side processing measured for a preview session (Interactive Server only).</summary>
public sealed class ServerActivityMetrics
{
    private readonly Lock _lock = new();

    public int Activities { get; private set; }
    public double TotalMs { get; private set; }
    public double MaxMs { get; private set; }

    public void Record(double milliseconds)
    {
        lock (_lock)
        {
            Activities++;
            TotalMs += milliseconds;
            MaxMs = Math.Max(MaxMs, milliseconds);
        }
    }

    public object Snapshot()
    {
        lock (_lock)
            return new { activities = Activities, totalMs = Math.Round(TotalMs, 2), maxMs = Math.Round(MaxMs, 2) };
    }
}
