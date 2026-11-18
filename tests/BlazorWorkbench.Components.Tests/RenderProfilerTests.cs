using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Components.Tests;

[CollectionDefinition(nameof(RenderProfilerCollection), DisableParallelization = true)]
public sealed class RenderProfilerCollection;

/// <summary>The Profiler panel records Blazor's component metrics (.NET 10) for one preview.</summary>
[Collection(nameof(RenderProfilerCollection))] // the meter listener is process-wide: profiles of parallel tests could mix
public class RenderProfilerTests : BunitContext
{
    public RenderProfilerTests()
    {
        // What AddBlazorWorkbenchRuntime registers for WebAssembly and Hybrid hosts.
        Services.AddBlazorWorkbenchRuntime(new WorkbenchOptions());
    }

    [Fact]
    public async Task Records_parameter_updates_events_and_batches_of_the_preview()
    {
        RenderProfiler.EnsureListening(); // ScenarioHost does this before the scenario's component renders
        var cut = Render<ProfiledList>();
        using var profiler = new RenderProfiler(await Renderer.Dispatcher.InvokeAsync(() => SynchronizationContext.Current));

        cut.Find("button").Click();
        cut.Find("button").Click();
        var result = profiler.Stop();

        Assert.True(result.Available);
        Assert.True(result.Batches >= 2);
        Assert.True(result.BatchEdits > 0);
        // Every click re-renders the parent, which passes parameters to its three rows again.
        var rows = Assert.Single(result.Components, c => c.Component == typeof(ProfiledRow).FullName);
        Assert.Equal(6, rows.ParameterUpdates);
        var click = Assert.Single(result.Events);
        Assert.Equal(typeof(ProfiledList).FullName, click.Component);
        Assert.Equal("Increment", click.Method);
        Assert.Equal("onclick", click.Attribute);
        Assert.Equal(2, click.Count);

        // Nothing is recorded after Stop.
        cut.Find("button").Click();
        Assert.Equal(6, profiler.Stop().Components.Single(c => c.Component == typeof(ProfiledRow).FullName).ParameterUpdates);
    }

    [Fact]
    public void Measurements_of_another_renderer_are_left_out()
    {
        RenderProfiler.EnsureListening();
        var cut = Render<ProfiledList>();
        using var other = new RenderProfiler(new SynchronizationContext()); // a profile of some other preview's renderer
        cut.Find("button").Click();
        var result = other.Stop();
        Assert.Empty(result.Components);
        Assert.Equal(0, result.Batches);
    }

    [Fact]
    public void Workbench_components_are_never_profiled()
    {
        Assert.True(RenderProfiler.IsWorkbenchType(typeof(WorkbenchShell).FullName!));
        Assert.True(RenderProfiler.IsWorkbenchType("BlazorWorkbench.Components.Preview.ScenarioHost"));
        Assert.False(RenderProfiler.IsWorkbenchType("BlazorWorkbench.Demo.Components.Inputs.Button"));
        Assert.False(RenderProfiler.IsWorkbenchType(typeof(ProfiledRow).FullName!)); // BlazorWorkbench.Components.Tests
    }

    [Fact]
    public void The_panel_shows_components_events_and_a_hint_for_hot_components()
    {
        var scenario = new ScenarioDefinition { ComponentType = typeof(ProfiledList), Key = "y", Name = "Y" };
        var profile = new RenderProfileView(true, 1200, 25, 4.5, 300,
            [new ComponentRenderStats("Lib.Rows.ProfiledRow", 75, 12.25, 1.5), new ComponentRenderStats("Lib.ProfiledList", 25, 20, 2)],
            [new EventHandlerStats("Lib.ProfiledList", "Increment", "onclick", 25, 3, 0.4), new EventHandlerStats("Lib.Counter", "<BuildRenderTree>b__29_1", "onclick", 2, 1, 0.6)], null, 80);

        var cut = Render<ProfilerPanel>(p => p.Add(x => x.Scenario, scenario).Add(x => x.Profile, profile));

        Assert.Equal("25", cut.Find("#wb-profile-batches").TextContent);
        Assert.Contains("ProfiledRow", cut.Find(".wb-callout-tip").TextContent);
        Assert.Contains("75", cut.Find(".wb-profile-hot").TextContent);
        Assert.Contains("ProfiledList.Increment", cut.Find("#wb-profile-events").TextContent);
        Assert.Contains("Counter, lambda in BuildRenderTree", cut.Find("#wb-profile-events").TextContent);

        var unavailable = Render<ProfilerPanel>(p => p.Add(x => x.Scenario, scenario)
            .Add(x => x.Profile, profile with { Available = false, Note = "This host does not publish Blazor's component metrics." }));
        Assert.Contains("does not publish", unavailable.Markup);
        Assert.Empty(unavailable.FindAll("#wb-profile-components"));
    }
}

public sealed class ProfiledList : ComponentBase
{
    private int _clicks;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Increment));
        builder.AddContent(2, _clicks);
        builder.CloseElement();
        for (var i = 0; i < 3; i++)
        {
            builder.OpenComponent<ProfiledRow>(3);
            builder.AddComponentParameter(4, nameof(ProfiledRow.Item), new List<int> { i }); // a new list every render: rows re-render
            builder.CloseComponent();
        }
    }

    private void Increment() => _clicks++;
}

public sealed class ProfiledRow : ComponentBase
{
    [Parameter] public List<int>? Item { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Item?.Count);
}
