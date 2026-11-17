namespace BlazorWorkbench.Components.Runtime;

/// <summary>
/// The time zone of the person using the Workbench. With Interactive Server, .NET runs on the server, so
/// <see cref="DateTimeOffset.ToLocalTime"/> would show the server's time; the shell reads the browser's IANA time zone
/// once and panels convert timestamps with <see cref="ToViewer"/>. With WebAssembly the local zone already is the browser's.
/// </summary>
public sealed class ViewerTimeZone
{
    /// <summary>The viewer's zone; the process's local zone until the browser has reported its own.</summary>
    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Local;

    /// <summary>True once the browser has reported a zone that this machine knows.</summary>
    public bool IsKnown { get; private set; }

    /// <summary>Raised when the zone changes, so open panels re-render with the viewer's times.</summary>
    public event Action? Changed;

    /// <summary>Uses the browser's IANA zone id (for example <c>Europe/Berlin</c>). Unknown ids keep the current zone.</summary>
    public bool TrySet(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId) || !TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var zone))
            return false;
        var changed = !IsKnown || zone.Id != Zone.Id;
        Zone = zone;
        IsKnown = true;
        if (changed)
            Changed?.Invoke();
        return true;
    }

    /// <summary><paramref name="value"/> in the viewer's time zone.</summary>
    public DateTimeOffset ToViewer(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Zone);

    /// <summary>
    /// <paramref name="value"/> in the viewer's time zone when <paramref name="services"/> has a <see cref="ViewerTimeZone"/>
    /// (Workbench hosts register one per circuit), else in the local zone.
    /// </summary>
    public static DateTimeOffset Convert(IServiceProvider? services, DateTimeOffset value) =>
        services?.GetService(typeof(ViewerTimeZone)) is ViewerTimeZone viewer ? viewer.ToViewer(value) : value.ToLocalTime();
}
