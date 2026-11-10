using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Core.Tests.Components;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Breakpoints, responsive sweeps and the responsive options of scenarios.</summary>
public class ResponsiveTests
{
    private sealed class Set(Action<ScenarioSetBuilder<TestButton>> configure) : ScenarioSet<TestButton>
    {
        protected override void Configure(ScenarioSetBuilder<TestButton> set) => configure(set);
    }

    private static WorkbenchCatalog Catalog(Action<ScenarioSetBuilder<TestButton>> configure, Action<WorkbenchOptions>? options = null)
    {
        var o = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        o.AddScenarios(new Set(configure));
        options?.Invoke(o);
        return CatalogBuilder.Build(o);
    }

    [Fact]
    public void Sweep_checks_the_reflow_width_and_both_edges_of_every_breakpoint()
    {
        Assert.Equal([320, 639, 640, 767, 768, 1023, 1024], BreakpointPreset.SweepWidths([768, 640, 1024, 768]));
        Assert.Equal([320], BreakpointPreset.SweepWidths([]));
        // 321 gives 320 and 321; widths outside what a viewport can be are dropped.
        Assert.Equal([320, 321, 3840], BreakpointPreset.SweepWidths([321, 1, 3841, 20000]));
    }

    [Fact]
    public void Sweep_is_capped()
    {
        var widths = BreakpointPreset.SweepWidths(Enumerable.Range(1, 40).Select(i => 400 + i * 50));
        Assert.Equal(BreakpointPreset.MaxSweepWidths, widths.Count);
        Assert.Equal(widths.Order(), widths);
    }

    [Fact]
    public void Responsive_options_reach_the_scenario_and_the_catalog_contract()
    {
        var catalog = Catalog(set => set.Add("Table", s => s
            .Responsive(r => r.Widths(768, 320, 768).AllowOverflow(".orders").AllowOverflow(".orders"))
            .Responsive(r => r.AllowOverflow("pre"))), o => o.Breakpoints.Add(new BreakpointPreset("md", 768)));

        var scenario = catalog.Scenarios.Single();
        Assert.Equal([320, 768], scenario.Responsive.Widths);
        Assert.Equal([".orders", "pre"], scenario.Responsive.AllowOverflow);
        Assert.False(scenario.Responsive.Skip);
        Assert.DoesNotContain(catalog.IssuesFor(scenario), i => i.Severity == IssueSeverity.Error);

        var dto = CatalogMapper.ToDto(catalog);
        Assert.Equal([320, 768], dto.Scenarios.Single().Responsive.Widths);
        Assert.Equal(new BreakpointPreset("md", 768), dto.Breakpoints.Single());
    }

    [Fact]
    public void Skipping_the_responsive_check_keeps_the_reason()
    {
        var scenario = Catalog(set => set.Add("Wide", s => s.Responsive(r => r.Skip("A poster: fixed width by design.")))).Scenarios.Single();
        Assert.True(scenario.Responsive.Skip);
        Assert.Equal("A poster: fixed width by design.", scenario.Responsive.SkipReason);
    }

    [Fact]
    public void Invalid_widths_and_empty_selectors_are_reported()
    {
        var catalog = Catalog(set => set.Add("Bad", s => s.Responsive(r => r.Widths(80, 5000).AllowOverflow(" "))));
        var issues = catalog.IssuesFor(catalog.Scenarios.Single()).Where(i => i.Code == "WB1026").ToList();
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal(IssueSeverity.Error, i.Severity));
        Assert.Contains(issues, i => i.Message.Contains("80", StringComparison.Ordinal));
    }

    [Fact]
    public void Test_run_settings_record_the_responsive_sweep()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RunSettings { Responsive = true, ResponsiveWidths = [320, 768] }, WorkbenchJson.Options);
        Assert.Contains("\"responsive\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"responsiveWidths\":[320,768]", json, StringComparison.Ordinal);
        Assert.Contains("\"Responsive\"", System.Text.Json.JsonSerializer.Serialize(CheckKind.Responsive, WorkbenchJson.Options), StringComparison.Ordinal);
    }
}
