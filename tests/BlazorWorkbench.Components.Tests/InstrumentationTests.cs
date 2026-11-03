using System.Diagnostics;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Testing.Bunit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SampleComponents.Inputs;
using SampleWorkbench.Scenarios;

namespace BlazorWorkbench.Components.Tests;

public class InstrumentationTests : BunitContext
{
    private static readonly System.Reflection.Assembly[] Assemblies = [typeof(ButtonScenarios).Assembly, typeof(Button).Assembly];

    [Fact]
    public void Measures_render_cost_and_component_render_count()
    {
        var measurement = this.MeasureScenario("inputs-counter--default", Assemblies);
        Assert.True(measurement.AllocatedBytes > 0);
        Assert.True(measurement.Elapsed > TimeSpan.Zero);
        var initial = measurement.Component.ComponentRenderCount();
        Assert.Equal(1, initial);

        // The click renders the counter, and the instrumented OnChanged callback re-renders the host (like a real parent),
        // which passes parameters to the counter again.
        measurement.Component.Find("button[aria-label=Increase]").Click();
        Assert.Equal(3, measurement.Component.ComponentRenderCount());
    }

    [Fact]
    public void Emits_open_telemetry_activities_with_correlation_ids()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WorkbenchTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (activities) activities.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = this.RenderScenario<WeatherScenarios>("loaded");
        cut.WaitForScenarioIdle();
        var sessionId = cut.Session().SessionId;

        lock (activities)
        {
            Assert.Contains(activities, a => a.OperationName == "workbench.scenario.activate" && (string?)a.GetTagItem("workbench.session.id") == sessionId);
            Assert.Contains(activities, a => a.OperationName == "workbench.http GET" && a.GetTagItem("http.response.status_code") is 200);
        }

        Assert.All(cut.Session().Diagnostics.Snapshot(), d => Assert.Equal(sessionId, d.SessionId));
    }

    [Fact]
    public void Hot_reload_rebuilds_the_catalog()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<Button>();
        using var accessor = new WorkbenchCatalogAccessor(options);
        var before = accessor.Catalog;
        var changed = 0;
        accessor.Changed += () => changed++;

        WorkbenchHotReload.UpdateApplication(null);

        Assert.Equal(1, changed);
        Assert.NotSame(before, accessor.Catalog);
        Assert.Equal(before.Scenarios.Select(s => s.Id), accessor.Catalog.Scenarios.Select(s => s.Id));
    }
}

/// <summary>The explorer stays cheap with large catalogs: closed groups render no children.</summary>
public class LargeCatalogExplorerTests : BunitContext
{
    [Fact]
    public void Explorer_renders_a_thousand_components_lazily()
    {
        var options = new WorkbenchOptions();
        options.AddAssembly(BlazorWorkbench.Core.Tests.SyntheticLibrary.Create(1000, 4));
        var catalog = CatalogBuilder.Build(options);

        var watch = Stopwatch.StartNew();
        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, catalog));
        var render = watch.Elapsed;

        // 25 closed groups and no component rows until a group is opened.
        Assert.Equal(25, cut.FindAll(".wb-node-group").Count);
        Assert.Empty(cut.FindAll(".wb-node-component"));
        Assert.True(render < TimeSpan.FromSeconds(5), $"Explorer render took {render}.");

        cut.FindAll(".wb-node-group")[0].Click();
        Assert.Equal(40, cut.FindAll(".wb-node-component").Count);

        // Search stays responsive.
        watch.Restart();
        cut.Find("#wb-search").Input("Component99");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Search took {watch.Elapsed}.");
        Assert.NotEmpty(cut.FindAll(".wb-results a"));
        Xunit.TestContext.Current.TestOutputHelper?.WriteLine($"Explorer: initial render {render.TotalMilliseconds:0} ms for 1000 components");
    }
}
