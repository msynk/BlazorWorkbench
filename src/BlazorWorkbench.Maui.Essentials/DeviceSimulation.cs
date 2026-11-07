using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace BlazorWorkbench.Maui.Essentials;

/// <summary>How a simulated location request behaves.</summary>
public enum LocationAvailability
{
    /// <summary>Requests return the configured location.</summary>
    Available,

    /// <summary>Requests throw <see cref="PermissionException"/>.</summary>
    PermissionDenied,

    /// <summary>Location services are off: requests throw <see cref="FeatureNotEnabledException"/>.</summary>
    Disabled,

    /// <summary>The device has no location support: requests throw <see cref="FeatureNotSupportedException"/>.</summary>
    NotSupported,
}

/// <summary>An immutable description of the simulated device of a scenario. Each activation creates fresh fakes from it.</summary>
public sealed record DeviceSimulation
{
    public DevicePlatform Platform { get; init; } = DevicePlatform.Android;
    public DeviceIdiom Idiom { get; init; } = DeviceIdiom.Phone;
    public DeviceType DeviceType { get; init; } = DeviceType.Virtual;
    public string Manufacturer { get; init; } = "Workbench";
    public string Model { get; init; } = "Simulated device";
    public string DeviceName { get; init; } = "Workbench device";
    public string OsVersion { get; init; } = "15.0";

    public NetworkAccess NetworkAccess { get; init; } = NetworkAccess.Internet;
    public IReadOnlyList<ConnectionProfile> ConnectionProfiles { get; init; } = [ConnectionProfile.WiFi];

    public LocationAvailability LocationAvailability { get; init; } = LocationAvailability.Available;
    public double Latitude { get; init; } = 47.6062;
    public double Longitude { get; init; } = -122.3321;
    public double? LocationAccuracy { get; init; } = 10;

    public double BatteryLevel { get; init; } = 0.8;
    public BatteryState BatteryState { get; init; } = BatteryState.Discharging;
    public BatteryPowerSource PowerSource { get; init; } = BatteryPowerSource.Battery;
    public EnergySaverStatus EnergySaver { get; init; } = EnergySaverStatus.Off;

    public bool ShareSupported { get; init; } = true;
    public IReadOnlyList<string> LaunchableSchemes { get; init; } = ["http", "https", "mailto", "tel", "sms"];
    public string? ClipboardText { get; init; }
    public bool VibrationSupported { get; init; } = true;

    public string AppName { get; init; } = "Workbench App";
    public string PackageName { get; init; } = "com.workbench.app";
    public string AppVersion { get; init; } = "1.0.0";
    public string AppBuild { get; init; } = "1";
    public AppTheme RequestedTheme { get; init; } = AppTheme.Unspecified;

    public IReadOnlyDictionary<string, object?> Preferences { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, string> SecureValues { get; init; } = new Dictionary<string, string>();

    /// <summary>The default simulation: an online Android phone with a known location.</summary>
    public static DeviceSimulation Default { get; } = new();
}

/// <summary>Configures the simulated device of a scenario.</summary>
public sealed class DeviceSimulationBuilder
{
    private DeviceSimulation _value;
    private readonly Dictionary<string, object?> _preferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _secure = new(StringComparer.Ordinal);

    public DeviceSimulationBuilder(DeviceSimulation? start = null)
    {
        _value = start ?? DeviceSimulation.Default;
        foreach (var (key, value) in _value.Preferences) _preferences[key] = value;
        foreach (var (key, value) in _value.SecureValues) _secure[key] = value;
    }

    /// <summary>Sets the platform, form factor and OS version.</summary>
    public DeviceSimulationBuilder Platform(DevicePlatform platform, DeviceIdiom idiom, string? osVersion = null)
    {
        _value = _value with { Platform = platform, Idiom = idiom, OsVersion = osVersion ?? _value.OsVersion };
        return this;
    }

    public DeviceSimulationBuilder AndroidPhone(string osVersion = "15.0") =>
        Platform(DevicePlatform.Android, DeviceIdiom.Phone, osVersion).Model("Google", "Pixel 9");

    public DeviceSimulationBuilder IPhone(string osVersion = "18.0") =>
        Platform(DevicePlatform.iOS, DeviceIdiom.Phone, osVersion).Model("Apple", "iPhone 16");

    public DeviceSimulationBuilder IPad(string osVersion = "18.0") =>
        Platform(DevicePlatform.iOS, DeviceIdiom.Tablet, osVersion).Model("Apple", "iPad Air");

    public DeviceSimulationBuilder WindowsDesktop(string osVersion = "10.0.26100") =>
        Platform(DevicePlatform.WinUI, DeviceIdiom.Desktop, osVersion).Model("Contoso", "Surface Laptop");

    public DeviceSimulationBuilder Mac(string osVersion = "15.0") =>
        Platform(DevicePlatform.MacCatalyst, DeviceIdiom.Desktop, osVersion).Model("Apple", "MacBook Air");

    /// <summary>Sets the manufacturer, model and device name.</summary>
    public DeviceSimulationBuilder Model(string manufacturer, string model, string? deviceName = null)
    {
        _value = _value with { Manufacturer = manufacturer, Model = model, DeviceName = deviceName ?? $"{manufacturer} {model}" };
        return this;
    }

    /// <summary>Reports a physical device instead of an emulator.</summary>
    public DeviceSimulationBuilder Physical() { _value = _value with { DeviceType = DeviceType.Physical }; return this; }

    /// <summary>Sets network access and connection profiles.</summary>
    public DeviceSimulationBuilder Network(NetworkAccess access, params ConnectionProfile[] profiles)
    {
        _value = _value with { NetworkAccess = access, ConnectionProfiles = profiles };
        return this;
    }

    public DeviceSimulationBuilder Online(ConnectionProfile profile = ConnectionProfile.WiFi) => Network(NetworkAccess.Internet, profile);

    public DeviceSimulationBuilder Offline() => Network(NetworkAccess.None);

    /// <summary>A known location (latitude and longitude in degrees, accuracy in meters).</summary>
    public DeviceSimulationBuilder Location(double latitude, double longitude, double? accuracyMeters = 10)
    {
        _value = _value with { LocationAvailability = LocationAvailability.Available, Latitude = latitude, Longitude = longitude, LocationAccuracy = accuracyMeters };
        return this;
    }

    /// <summary>Location requests fail as configured.</summary>
    public DeviceSimulationBuilder Location(LocationAvailability availability) { _value = _value with { LocationAvailability = availability }; return this; }

    public DeviceSimulationBuilder LocationPermissionDenied() => Location(LocationAvailability.PermissionDenied);

    public DeviceSimulationBuilder LocationDisabled() => Location(LocationAvailability.Disabled);

    /// <summary>Battery level between 0 and 1.</summary>
    public DeviceSimulationBuilder Battery(double level, BatteryState state = BatteryState.Discharging, BatteryPowerSource source = BatteryPowerSource.Battery,
        EnergySaverStatus energySaver = EnergySaverStatus.Off)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 1);
        _value = _value with { BatteryLevel = level, BatteryState = state, PowerSource = source, EnergySaver = energySaver };
        return this;
    }

    /// <summary>Makes <c>IShare.RequestAsync</c> throw <see cref="FeatureNotSupportedException"/>.</summary>
    public DeviceSimulationBuilder ShareUnsupported() { _value = _value with { ShareSupported = false }; return this; }

    /// <summary>URI schemes the launcher can open (others report <c>false</c> from <c>CanOpenAsync</c>/<c>TryOpenAsync</c>).</summary>
    public DeviceSimulationBuilder LaunchableSchemes(params string[] schemes) { _value = _value with { LaunchableSchemes = schemes }; return this; }

    public DeviceSimulationBuilder Clipboard(string? text) { _value = _value with { ClipboardText = text }; return this; }

    public DeviceSimulationBuilder NoVibration() { _value = _value with { VibrationSupported = false }; return this; }

    /// <summary>Sets the reported app identity.</summary>
    public DeviceSimulationBuilder App(string name, string version = "1.0.0", string build = "1", string? packageName = null)
    {
        _value = _value with { AppName = name, AppVersion = version, AppBuild = build, PackageName = packageName ?? _value.PackageName };
        return this;
    }

    /// <summary>The theme the user selected in the OS (<c>IAppInfo.RequestedTheme</c>).</summary>
    public DeviceSimulationBuilder RequestedTheme(AppTheme theme) { _value = _value with { RequestedTheme = theme }; return this; }

    /// <summary>A stored preference (in the default shared container).</summary>
    public DeviceSimulationBuilder Preference(string key, object? value) { _preferences[key] = value; return this; }

    /// <summary>A value in secure storage.</summary>
    public DeviceSimulationBuilder SecureValue(string key, string value) { _secure[key] = value; return this; }

    public DeviceSimulation Build() => _value with
    {
        Preferences = new Dictionary<string, object?>(_preferences, StringComparer.Ordinal),
        SecureValues = new Dictionary<string, string>(_secure, StringComparer.Ordinal),
    };
}

/// <summary>Registers simulated MAUI Essentials services.</summary>
public static class DeviceSimulationExtensions
{
    /// <summary>
    /// Simulates MAUI Essentials for this scenario: every supported Essentials interface (<c>IConnectivity</c>,
    /// <c>IDeviceInfo</c>, <c>IGeolocation</c>, <c>IBattery</c>, <c>IShare</c>, <c>ILauncher</c>, <c>IClipboard</c>,
    /// <c>IPreferences</c>, <c>ISecureStorage</c>, <c>IAppInfo</c>, <c>IVibration</c>, <c>IHapticFeedback</c>) is replaced
    /// by a fake that follows <paramref name="configure"/> and reports what the component asked it to do in the Diagnostics panel.
    /// </summary>
    public static ScenarioBuilder<TComponent> Device<TComponent>(this ScenarioBuilder<TComponent> scenario, Action<DeviceSimulationBuilder>? configure = null)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var builder = new DeviceSimulationBuilder();
        configure?.Invoke(builder);
        var simulation = builder.Build();
        return scenario.Services((services, context) => services.AddDeviceSimulation(simulation, context, replace: true));
    }

    /// <summary>
    /// Registers simulated Essentials services in a host (for example an Interactive Server or WebAssembly Workbench host,
    /// where MAUI Essentials has no implementation), so every scenario gets a working default device. Existing registrations
    /// are kept. Scenarios refine the device with <c>.Device(...)</c>.
    /// </summary>
    public static IServiceCollection AddSimulatedDevice(this IServiceCollection services, Action<DeviceSimulationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var builder = new DeviceSimulationBuilder();
        configure?.Invoke(builder);
        return services.AddDeviceSimulation(builder.Build(), context: null, replace: false);
    }

    private static IServiceCollection AddDeviceSimulation(this IServiceCollection services, DeviceSimulation simulation, ScenarioContext? context, bool replace)
    {
        // In host mode (AddSimulatedDevice) there is no scenario at registration time, so the scenario context (and with it
        // the scenario clock) is resolved per scope, like the diagnostics log.
        void Add<TService>(Func<PlatformLog, TimeProvider, TService> create) where TService : class
        {
            var descriptor = ServiceDescriptor.Scoped<TService>(sp =>
            {
                var scenario = context ?? TryGetContext(sp);
                return create(new PlatformLog(scenario), scenario?.Time ?? sp.GetService<TimeProvider>() ?? TimeProvider.System);
            });
            if (replace) services.Replace(descriptor);
            else services.TryAdd(descriptor);
        }

        Add<IConnectivity>((log, _) => new FakeConnectivity(simulation, log));
        Add<IDeviceInfo>((_, _) => new FakeDeviceInfo(simulation));
        Add<IGeolocation>((log, time) => new FakeGeolocation(simulation, log, time));
        Add<IBattery>((_, _) => new FakeBattery(simulation));
        Add<IShare>((log, _) => new FakeShare(simulation, log));
        Add<ILauncher>((log, _) => new FakeLauncher(simulation, log));
        Add<IClipboard>((log, _) => new FakeClipboard(simulation, log));
        Add<IPreferences>((log, _) => new FakePreferences(simulation, log));
        Add<ISecureStorage>((log, _) => new FakeSecureStorage(simulation, log));
        Add<IAppInfo>((log, _) => new FakeAppInfo(simulation, log));
        Add<IVibration>((log, _) => new FakeVibration(simulation, log));
        Add<IHapticFeedback>((log, _) => new FakeHapticFeedback(simulation, log));
        return services;
    }

    private static ScenarioContext? TryGetContext(IServiceProvider services)
    {
        try
        {
            return services.GetService<ScenarioContext>();
        }
        catch (InvalidOperationException)
        {
            return null; // No active scenario (for example while the host starts).
        }
    }
}
