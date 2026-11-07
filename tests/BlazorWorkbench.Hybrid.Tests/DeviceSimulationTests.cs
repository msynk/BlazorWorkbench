using Bunit;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Maui.Essentials;
using BlazorWorkbench.Testing.Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace BlazorWorkbench.Hybrid.Tests;

public class DeviceSimulationTests
{
    private static DeviceSimulation Build(Action<DeviceSimulationBuilder> configure)
    {
        var builder = new DeviceSimulationBuilder();
        configure(builder);
        return builder.Build();
    }

    private static PlatformLog Log() => new(null);

    [Fact]
    public void Default_device_is_an_online_phone_with_a_location()
    {
        var device = DeviceSimulation.Default;
        Assert.Equal(NetworkAccess.Internet, new FakeConnectivity(device, Log()).NetworkAccess);
        Assert.Equal(DeviceIdiom.Phone, new FakeDeviceInfo(device).Idiom);
        Assert.True(new FakeGeolocation(device, Log(), TimeProvider.System).IsEnabled);
    }

    [Fact]
    public void Connectivity_changes_raise_the_event()
    {
        var connectivity = new FakeConnectivity(Build(d => d.Offline()), Log());
        Assert.Equal(NetworkAccess.None, connectivity.NetworkAccess);
        Assert.Empty(connectivity.ConnectionProfiles);

        ConnectivityChangedEventArgs? raised = null;
        connectivity.ConnectivityChanged += (_, e) => raised = e;
        connectivity.Set(NetworkAccess.Internet, ConnectionProfile.Cellular);
        Assert.Equal(NetworkAccess.Internet, raised?.NetworkAccess);
        Assert.Equal([ConnectionProfile.Cellular], connectivity.ConnectionProfiles);
    }

    [Fact]
    public async Task Geolocation_reports_the_location_and_simulated_failures()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var located = new FakeGeolocation(Build(d => d.Location(51.5, -0.12, accuracyMeters: 25)), Log(), clock);
        var location = await located.GetLocationAsync(new GeolocationRequest(), CancellationToken.None);
        Assert.Equal(51.5, location!.Latitude);
        Assert.Equal(25, location.Accuracy);
        Assert.Equal(clock.GetUtcNow(), location.Timestamp);

        await Assert.ThrowsAsync<PermissionException>(() => new FakeGeolocation(Build(d => d.LocationPermissionDenied()), Log(), clock).GetLastKnownLocationAsync());
        await Assert.ThrowsAsync<FeatureNotEnabledException>(() => new FakeGeolocation(Build(d => d.LocationDisabled()), Log(), clock).GetLocationAsync(new GeolocationRequest(), CancellationToken.None));
        await Assert.ThrowsAsync<FeatureNotSupportedException>(() => new FakeGeolocation(Build(d => d.Location(LocationAvailability.NotSupported)), Log(), clock).GetLastKnownLocationAsync());
    }

    [Fact]
    public async Task Geolocation_raises_location_changes_only_while_listening()
    {
        var geolocation = new FakeGeolocation(DeviceSimulation.Default, Log(), TimeProvider.System);
        var changes = 0;
        geolocation.LocationChanged += (_, _) => changes++;
        geolocation.Move(1, 2);
        Assert.True(await geolocation.StartListeningForegroundAsync(new GeolocationListeningRequest()));
        geolocation.Move(3, 4);
        geolocation.StopListeningForeground();
        geolocation.Move(5, 6);
        Assert.Equal(1, changes);
        Assert.Equal(5, (await geolocation.GetLastKnownLocationAsync())!.Latitude);
    }

    [Fact]
    public async Task Share_and_launcher_record_requests_and_simulate_missing_support()
    {
        var log = Log();
        await new FakeShare(DeviceSimulation.Default, log).RequestAsync(new ShareTextRequest { Text = "Lake loop", Title = "Trail" });
        Assert.Equal(["Share: text \"Lake loop\" titled \"Trail\"."], log.Entries);
        await Assert.ThrowsAsync<FeatureNotSupportedException>(() => new FakeShare(Build(d => d.ShareUnsupported()), Log()).RequestAsync(new ShareTextRequest { Text = "x" }));

        var launcher = new FakeLauncher(Build(d => d.LaunchableSchemes("https")), Log());
        Assert.True(await launcher.CanOpenAsync(new Uri("https://example.com")));
        Assert.False(await launcher.TryOpenAsync(new Uri("mailto:someone@example.com")));
        Assert.Contains(launcher.Log.Entries, e => e.Contains("no app can open", StringComparison.Ordinal));
    }

    [Fact]
    public void Preferences_are_typed_and_scoped_by_container()
    {
        var preferences = new FakePreferences(Build(d => d.Preference("count", 3).Preference("limit", "5")), Log());
        Assert.Equal(3, preferences.Get("count", 0));
        Assert.Equal(5, preferences.Get("limit", 0));
        Assert.Equal("fallback", preferences.Get("missing", "fallback"));

        preferences.Set("theme", "dark", sharedName: "ui");
        Assert.True(preferences.ContainsKey("theme", "ui"));
        Assert.False(preferences.ContainsKey("theme"));
        preferences.Clear("ui");
        Assert.False(preferences.ContainsKey("theme", "ui"));
        Assert.True(preferences.ContainsKey("count"));
    }

    [Fact]
    public async Task Secure_storage_never_logs_values()
    {
        var storage = new FakeSecureStorage(Build(d => d.SecureValue("token", "s3cr3t")), Log());
        Assert.Equal("s3cr3t", await storage.GetAsync("token"));
        await storage.SetAsync("refresh", "r3fr3sh");
        Assert.DoesNotContain(storage.Log.Entries, e => e.Contains("r3fr3sh", StringComparison.Ordinal));
        Assert.True(storage.Remove("token"));
        Assert.Null(await storage.GetAsync("token"));
    }

    [Fact]
    public void Presets_describe_common_devices()
    {
        var iphone = new FakeDeviceInfo(Build(d => d.IPhone("18.1")));
        Assert.Equal(DevicePlatform.iOS, iphone.Platform);
        Assert.Equal(new Version(18, 1), iphone.Version);
        Assert.Equal("Apple", iphone.Manufacturer);
        Assert.Equal(DeviceIdiom.Desktop, new FakeDeviceInfo(Build(d => d.WindowsDesktop())).Idiom);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceSimulationBuilder().Battery(1.5));
        Assert.Throws<FeatureNotSupportedException>(() => new FakeVibration(Build(d => d.NoVibration()), Log()).Vibrate());
    }

    [Fact]
    public void Host_level_simulation_keeps_existing_registrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShare>(new FakeShare(DeviceSimulation.Default, Log()));
        services.AddSimulatedDevice(d => d.Offline());
        Assert.Single(services, s => s.ServiceType == typeof(IShare));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(NetworkAccess.None, scope.ServiceProvider.GetRequiredService<IConnectivity>().NetworkAccess);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPreferences>());
    }

    [Fact]
    public void Scenario_simulation_drives_the_component_and_reports_to_diagnostics()
    {
        using var context = new BunitContext();
        var cut = context.RenderScenario<ShareCardTestScenarios>("Default");
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Equal("Shared", cut.Find(".share-status").TextContent));
        Assert.Contains(cut.Session().Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.Platform && d.Message.StartsWith("Share: text \"Lake loop\"", StringComparison.Ordinal));
        Assert.Equal(1, cut.Session().Callbacks.Count("OnShared"));
    }

    [Fact]
    public void Unsupported_share_is_explained_to_the_user()
    {
        using var context = new BunitContext();
        var cut = context.RenderScenario<ShareCardTestScenarios>("Unsupported");
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Equal("Sharing is not available on this device.", cut.Find(".share-status").TextContent));
        Assert.Equal(0, cut.Session().Callbacks.Count("OnShared"));
    }

    [Theory]
    [InlineData("Located", "51.5000")]
    [InlineData("Denied", "Allow location access")]
    [InlineData("Off", "Turn on location services")]
    public void Location_card_covers_every_permission_state(string scenario, string expected)
    {
        using var context = new BunitContext();
        var cut = context.RenderScenario<LocationCardTestScenarios>(scenario);
        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Markup, StringComparison.Ordinal));
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void Device_summary_shows_battery_warnings()
    {
        using var context = new BunitContext();
        var cut = context.RenderScenario<DeviceSummaryTestScenarios>("Low battery");
        Assert.Contains("Apple iPhone 16", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(["Low battery", "Energy saver"], cut.FindAll(".badge").Select(b => b.TextContent));
    }
}
