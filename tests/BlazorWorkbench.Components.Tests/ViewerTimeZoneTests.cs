using BlazorWorkbench.Components.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Components.Tests;

public class ViewerTimeZoneTests
{
    [Fact]
    public void Times_follow_the_zone_the_browser_reports()
    {
        var viewer = new ViewerTimeZone();
        Assert.False(viewer.TrySet("Not/A_Zone"));
        Assert.False(viewer.IsKnown);

        Assert.True(viewer.TrySet("Asia/Tokyo"));
        Assert.True(viewer.IsKnown);
        var noonUtc = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(21, viewer.ToViewer(noonUtc).Hour);

        var services = new ServiceCollection().AddSingleton(viewer).BuildServiceProvider();
        Assert.Equal(TimeSpan.FromHours(9), ViewerTimeZone.Convert(services, noonUtc).Offset);
        Assert.Equal(noonUtc.ToLocalTime(), ViewerTimeZone.Convert(null, noonUtc));
    }
}
