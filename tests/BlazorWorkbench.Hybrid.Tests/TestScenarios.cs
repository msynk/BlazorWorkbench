using BlazorWorkbench;
using BlazorWorkbench.Maui.Essentials;
using SampleComponents.Device;

// Hybrid hosts make the scenario session and culture process-wide, so these tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BlazorWorkbench.Hybrid.Tests;

public sealed class ShareCardTestScenarios : ScenarioSet<ShareCard>
{
    protected override void Configure(ScenarioSetBuilder<ShareCard> set)
    {
        set.Setup(s => s.Parameter(c => c.Title, "Trail").Parameter(c => c.Text, "Lake loop").Parameter(c => c.Url, "https://example.com/trail"));
        set.Add("Default", s => s.Device());
        set.Add("Unsupported", s => s.Device(d => d.ShareUnsupported()));
        set.Add("French", s => s.Device().Culture("fr-FR"));
    }
}

public sealed class LocationCardTestScenarios : ScenarioSet<LocationCard>
{
    protected override void Configure(ScenarioSetBuilder<LocationCard> set)
    {
        set.Add("Located", s => s.Device(d => d.Location(51.5, -0.12, accuracyMeters: 25)).Culture("en-US"));
        set.Add("Denied", s => s.Device(d => d.LocationPermissionDenied()));
        set.Add("Off", s => s.Device(d => d.LocationDisabled()));
    }
}

public sealed class DeviceSummaryTestScenarios : ScenarioSet<DeviceSummary>
{
    protected override void Configure(ScenarioSetBuilder<DeviceSummary> set) =>
        set.Add("Low battery", s => s.Device(d => d.IPhone("18.1").Battery(0.1, energySaver: Microsoft.Maui.Devices.EnergySaverStatus.On).App("Trails", "2.4.0", "240")));
}
