using System.Globalization;
using BlazorWorkbench.Diagnostics;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using DiagnosticSource = BlazorWorkbench.Diagnostics.DiagnosticSource;

namespace BlazorWorkbench.Maui.Essentials;

/// <summary>Writes what a simulated platform service was asked to do to the scenario's diagnostics (source <c>Platform</c>).</summary>
public sealed class PlatformLog(ScenarioContext? context)
{
    private readonly List<string> _entries = [];

    /// <summary>Everything logged by this service instance (also useful in bUnit tests).</summary>
    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public void Info(string message) => Write(DiagnosticSeverity.Info, message);

    public void Warning(string message) => Write(DiagnosticSeverity.Warning, message);

    private void Write(DiagnosticSeverity severity, string message)
    {
        lock (_entries)
            _entries.Add(message);
        context?.Diagnostics?.Add(DiagnosticSource.Platform, severity, context.ScenarioId, message);
    }
}

public sealed class FakeConnectivity(DeviceSimulation simulation, PlatformLog log) : IConnectivity
{
    private readonly Lock _lock = new();
    private NetworkAccess _access = simulation.NetworkAccess;
    private IReadOnlyList<ConnectionProfile> _profiles = simulation.ConnectionProfiles;

    public PlatformLog Log { get; } = log;

    public NetworkAccess NetworkAccess
    {
        get { lock (_lock) return _access; }
    }

    public IEnumerable<ConnectionProfile> ConnectionProfiles
    {
        get { lock (_lock) return _profiles; }
    }

    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    /// <summary>Changes connectivity and raises <see cref="ConnectivityChanged"/>.</summary>
    public void Set(NetworkAccess access, params ConnectionProfile[] profiles)
    {
        lock (_lock)
        {
            _access = access;
            _profiles = profiles;
        }

        Log.Info($"Connectivity changed to {access}{(profiles.Length > 0 ? $" ({string.Join(", ", profiles)})" : "")}.");
        ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(access, profiles));
    }
}

public sealed class FakeDeviceInfo(DeviceSimulation simulation) : IDeviceInfo
{
    public string Model => simulation.Model;
    public string Manufacturer => simulation.Manufacturer;
    public string Name => simulation.DeviceName;
    public string VersionString => simulation.OsVersion;
    public Version Version => System.Version.TryParse(simulation.OsVersion, out var v) ? v : new Version(0, 0);
    public DevicePlatform Platform => simulation.Platform;
    public DeviceIdiom Idiom => simulation.Idiom;
    public DeviceType DeviceType => simulation.DeviceType;
}

public sealed class FakeGeolocation(DeviceSimulation simulation, PlatformLog log, TimeProvider time) : IGeolocation
{
    private Location? _current = simulation.LocationAvailability == LocationAvailability.Available
        ? new Location(simulation.Latitude, simulation.Longitude) { Accuracy = simulation.LocationAccuracy, Timestamp = time.GetUtcNow() }
        : null;

    public PlatformLog Log { get; } = log;

    public bool IsListeningForeground { get; private set; }

    public bool IsEnabled => simulation.LocationAvailability is LocationAvailability.Available or LocationAvailability.PermissionDenied;

    public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;

    public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

    public Task<Location?> GetLastKnownLocationAsync()
    {
        Log.Info("Geolocation: last known location requested.");
        ThrowIfUnavailable();
        return Task.FromResult(_current);
    }

    public Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
    {
        Log.Info($"Geolocation: location requested ({request?.DesiredAccuracy.ToString() ?? "default"} accuracy).");
        cancelToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();
        return Task.FromResult(_current);
    }

    public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
    {
        Log.Info("Geolocation: started listening.");
        if (simulation.LocationAvailability != LocationAvailability.Available)
        {
            ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(GeolocationError.Unauthorized));
            return Task.FromResult(false);
        }

        IsListeningForeground = true;
        return Task.FromResult(true);
    }

    public void StopListeningForeground()
    {
        Log.Info("Geolocation: stopped listening.");
        IsListeningForeground = false;
    }

    /// <summary>Moves the simulated device and raises <see cref="LocationChanged"/> while listening.</summary>
    public void Move(double latitude, double longitude)
    {
        _current = new Location(latitude, longitude) { Accuracy = simulation.LocationAccuracy, Timestamp = time.GetUtcNow() };
        if (IsListeningForeground)
            LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(_current));
    }

    private void ThrowIfUnavailable()
    {
        switch (simulation.LocationAvailability)
        {
            case LocationAvailability.PermissionDenied:
                Log.Warning("Geolocation: permission denied (simulated).");
                throw new PermissionException("Location permission was denied.");
            case LocationAvailability.Disabled:
                Log.Warning("Geolocation: location services are off (simulated).");
                throw new FeatureNotEnabledException("Location services are not enabled on the device.");
            case LocationAvailability.NotSupported:
                Log.Warning("Geolocation: not supported (simulated).");
                throw new FeatureNotSupportedException("Geolocation is not supported on this device.");
        }
    }
}

public sealed class FakeBattery(DeviceSimulation simulation) : IBattery
{
    public double ChargeLevel { get; private set; } = simulation.BatteryLevel;
    public BatteryState State { get; private set; } = simulation.BatteryState;
    public BatteryPowerSource PowerSource { get; private set; } = simulation.PowerSource;
    public EnergySaverStatus EnergySaverStatus { get; private set; } = simulation.EnergySaver;

    public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged;

    public event EventHandler<EnergySaverStatusChangedEventArgs>? EnergySaverStatusChanged;

    /// <summary>Changes the battery and raises <see cref="BatteryInfoChanged"/>.</summary>
    public void Set(double level, BatteryState state, BatteryPowerSource source)
    {
        (ChargeLevel, State, PowerSource) = (level, state, source);
        BatteryInfoChanged?.Invoke(this, new BatteryInfoChangedEventArgs(level, state, source));
    }

    /// <summary>Changes energy saver mode and raises <see cref="EnergySaverStatusChanged"/>.</summary>
    public void SetEnergySaver(EnergySaverStatus status)
    {
        EnergySaverStatus = status;
        EnergySaverStatusChanged?.Invoke(this, new EnergySaverStatusChangedEventArgs(status));
    }
}

public sealed class FakeShare(DeviceSimulation simulation, PlatformLog log) : IShare
{
    public PlatformLog Log { get; } = log;

    public Task RequestAsync(ShareTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Share($"text \"{Trim(request.Text ?? request.Uri)}\"{Titled(request.Title)}");
    }

    public Task RequestAsync(ShareFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Share($"file {request.File?.FileName}{Titled(request.Title)}");
    }

    public Task RequestAsync(ShareMultipleFilesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Share($"{request.Files?.Count ?? 0} file(s){Titled(request.Title)}");
    }

    private Task Share(string what)
    {
        if (!simulation.ShareSupported)
        {
            Log.Warning($"Share: not supported (simulated) — {what}.");
            throw new FeatureNotSupportedException("Sharing is not supported on this device.");
        }

        Log.Info($"Share: {what}.");
        return Task.CompletedTask;
    }

    private static string Titled(string? title) => string.IsNullOrEmpty(title) ? "" : $" titled \"{Trim(title)}\"";

    private static string Trim(string? value) => value is { Length: > 120 } ? value[..120] + "…" : value ?? "";
}

public sealed class FakeLauncher(DeviceSimulation simulation, PlatformLog log) : ILauncher
{
    public PlatformLog Log { get; } = log;

    public Task<bool> CanOpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Task.FromResult(CanOpen(uri));
    }

    public Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var opened = CanOpen(uri);
        if (opened) Log.Info($"Launcher: opened {uri}.");
        else Log.Warning($"Launcher: no app can open {uri} (simulated).");
        return Task.FromResult(opened);
    }

    public Task<bool> OpenAsync(OpenFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log.Info($"Launcher: opened file {request.File?.FileName}.");
        return Task.FromResult(true);
    }

    public Task<bool> TryOpenAsync(Uri uri) => OpenAsync(uri);

    private bool CanOpen(Uri uri) => simulation.LaunchableSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
}

public sealed class FakeClipboard(DeviceSimulation simulation, PlatformLog log) : IClipboard
{
    private string? _text = simulation.ClipboardText;

    public PlatformLog Log { get; } = log;

    public bool HasText => !string.IsNullOrEmpty(_text);

    public event EventHandler<EventArgs>? ClipboardContentChanged;

    public Task SetTextAsync(string? text)
    {
        _text = text;
        Log.Info($"Clipboard: set \"{text}\".");
        ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(_text);
}

public sealed class FakePreferences(DeviceSimulation simulation, PlatformLog log) : IPreferences
{
    private readonly Dictionary<(string Shared, string Key), object?> _values =
        simulation.Preferences.ToDictionary(p => ("", p.Key), p => p.Value);

    public PlatformLog Log { get; } = log;

    public bool ContainsKey(string key, string? sharedName = null)
    {
        lock (_values)
            return _values.ContainsKey((sharedName ?? "", key));
    }

    public void Remove(string key, string? sharedName = null)
    {
        lock (_values)
            _values.Remove((sharedName ?? "", key));
        Log.Info($"Preferences: removed '{key}'.");
    }

    public void Clear(string? sharedName = null)
    {
        lock (_values)
        {
            foreach (var key in _values.Keys.Where(k => k.Shared == (sharedName ?? "")).ToList())
                _values.Remove(key);
        }

        Log.Info("Preferences: cleared.");
    }

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        lock (_values)
            _values[(sharedName ?? "", key)] = value;
        Log.Info($"Preferences: set '{key}' = {Convert.ToString(value, CultureInfo.InvariantCulture)}.");
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        object? value;
        lock (_values)
        {
            if (!_values.TryGetValue((sharedName ?? "", key), out value))
                return defaultValue;
        }

        if (value is T typed)
            return typed;
        try
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target == typeof(DateTime) && value is string s)
                return (T)(object)DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return value is null ? defaultValue : (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return defaultValue;
        }
    }
}

public sealed class FakeSecureStorage(DeviceSimulation simulation, PlatformLog log) : ISecureStorage
{
    private readonly Dictionary<string, string> _values = new(simulation.SecureValues, StringComparer.Ordinal);

    public PlatformLog Log { get; } = log;

    public Task<string?> GetAsync(string key)
    {
        lock (_values)
            return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value)
    {
        lock (_values)
            _values[key] = value;
        Log.Info($"SecureStorage: stored '{key}'."); // Values are never logged.
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        bool removed;
        lock (_values)
            removed = _values.Remove(key);
        Log.Info($"SecureStorage: removed '{key}'.");
        return removed;
    }

    public void RemoveAll()
    {
        lock (_values)
            _values.Clear();
        Log.Info("SecureStorage: removed all values.");
    }
}

public sealed class FakeAppInfo(DeviceSimulation simulation, PlatformLog log) : IAppInfo
{
    public PlatformLog Log { get; } = log;
    public string PackageName => simulation.PackageName;
    public string Name => simulation.AppName;
    public string VersionString => simulation.AppVersion;
    public Version Version => System.Version.TryParse(simulation.AppVersion, out var v) ? v : new Version(0, 0);
    public string BuildString => simulation.AppBuild;
    public AppTheme RequestedTheme => simulation.RequestedTheme;
    public AppPackagingModel PackagingModel => AppPackagingModel.Packaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;

    public void ShowSettingsUI() => Log.Info("AppInfo: settings UI opened.");
}

public sealed class FakeVibration(DeviceSimulation simulation, PlatformLog log) : IVibration
{
    public PlatformLog Log { get; } = log;
    public bool IsSupported => simulation.VibrationSupported;

    public void Vibrate() => Vibrate(TimeSpan.FromMilliseconds(500));

    public void Vibrate(TimeSpan duration)
    {
        if (!IsSupported)
            throw new FeatureNotSupportedException("Vibration is not supported on this device.");
        Log.Info($"Vibration: {duration.TotalMilliseconds:0} ms.");
    }

    public void Cancel() => Log.Info("Vibration: canceled.");
}

public sealed class FakeHapticFeedback(DeviceSimulation simulation, PlatformLog log) : IHapticFeedback
{
    public PlatformLog Log { get; } = log;
    public bool IsSupported => simulation.VibrationSupported;

    public void Perform(HapticFeedbackType type)
    {
        if (!IsSupported)
            throw new FeatureNotSupportedException("Haptic feedback is not supported on this device.");
        Log.Info($"Haptic feedback: {type}.");
    }
}
