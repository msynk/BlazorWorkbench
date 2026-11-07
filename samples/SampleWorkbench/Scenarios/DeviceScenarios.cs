using BlazorWorkbench;
using BlazorWorkbench.Maui.Essentials;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using SampleComponents.Device;

namespace SampleWorkbench.Scenarios;

// Components that use .NET MAUI Essentials. The same scenarios run in the Interactive Server, WebAssembly and
// MAUI Blazor Hybrid hosts: .Device(...) replaces the platform services with simulations.

public sealed class NetworkStatusScenarios : ScenarioSet<NetworkStatus>
{
    protected override void Configure(ScenarioSetBuilder<NetworkStatus> set)
    {
        set.Title("Device/Network status").Tags("device", "maui");
        set.Documentation("Reads `IConnectivity` from .NET MAUI Essentials. Scenarios simulate the network with `.Device(d => d.Offline())`.");

        set.Add("Wi-Fi", s => s.Device(d => d.Online(ConnectionProfile.WiFi)));
        set.Add("Cellular", s => s.Device(d => d.Online(ConnectionProfile.Cellular)));
        set.Add("Limited", s => s.Device(d => d.Network(NetworkAccess.ConstrainedInternet, ConnectionProfile.Cellular)));
        set.Add("Offline", s => s
            .Device(d => d.Offline())
            .Interaction("offers retry", i => i
                .Click(By.Role("button", "Retry"))
                .ExpectCallback("OnRetry", times: 1)));
    }
}

public sealed class ShareCardScenarios : ScenarioSet<ShareCard>
{
    protected override void Configure(ScenarioSetBuilder<ShareCard> set)
    {
        set.Title("Device/Share card").Tags("device", "maui");
        set.Setup(s => s
            .Parameter(c => c.Title, "Lake loop trail")
            .Parameter(c => c.Text, "5.2 km around the lake, mostly flat.")
            .Parameter(c => c.Url, "https://example.com/trails/lake-loop"));

        set.Add("Default", s => s
            .Device()
            .Interaction("shares the trail", i => i
                .Click(By.Role("button", "Share"))
                .ExpectVisible(By.Text("Shared"))
                .ExpectCallback("OnShared", times: 1)));
        set.Add("Sharing unavailable", s => s
            .Device(d => d.ShareUnsupported())
            .Interaction("explains the failure", i => i
                .Click(By.Role("button", "Share"))
                .ExpectVisible(By.Text("Sharing is not available on this device."))
                .ExpectCallback("OnShared", times: 0)));
    }
}

public sealed class LocationCardScenarios : ScenarioSet<LocationCard>
{
    protected override void Configure(ScenarioSetBuilder<LocationCard> set)
    {
        set.Title("Device/Location card").Tags("device", "maui");
        set.Documentation("Permission and settings failures of `IGeolocation` are simulated, so every state can be reviewed without a device.");

        set.Add("Located", s => s.Device(d => d.Location(48.8584, 2.2945, accuracyMeters: 12)));
        set.Add("Permission denied", s => s.Device(d => d.LocationPermissionDenied()));
        set.Add("Location off", s => s.Device(d => d.LocationDisabled()));
        set.Add("Located (French)", s => s.Device(d => d.Location(48.8584, 2.2945, accuracyMeters: 1200)).Culture("fr-FR"));
    }
}

public sealed class DeviceSummaryScenarios : ScenarioSet<DeviceSummary>
{
    protected override void Configure(ScenarioSetBuilder<DeviceSummary> set)
    {
        set.Title("Device/Device summary").Tags("device", "maui");

        set.Add("Android phone", s => s.Device(d => d.AndroidPhone("15.0").Battery(0.72).App("Trails", "2.4.0", "240")));
        set.Add("Low battery iPhone", s => s.Device(d => d
            .IPhone("18.1")
            .Battery(0.12, energySaver: EnergySaverStatus.On)
            .App("Trails", "2.4.0", "240")));
        set.Add("Charging desktop", s => s.Device(d => d
            .WindowsDesktop()
            .Battery(0.5, BatteryState.Charging, BatteryPowerSource.AC)
            .App("Trails", "2.4.0", "240")));
    }
}
