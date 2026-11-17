using System.Globalization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Testing.Bunit;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorWorkbench.Components.Tests;

/// <summary>Toolbar globals (<c>options.AddGlobal</c>, <c>.Global(...)</c>) reach components, service factories and the preview.</summary>
public class GlobalsTests : BunitContext
{
    public GlobalsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    public sealed class DensityLabel : ComponentBase
    {
        [CascadingParameter(Name = "density")] public string? Density { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "density");
            builder.AddContent(2, Density ?? "unset");
            builder.CloseElement();
        }
    }

    private sealed class DensityScenarios : ScenarioSet<DensityLabel>
    {
        protected override void Configure(ScenarioSetBuilder<DensityLabel> set)
        {
            set.Title("Tests/Density label");
            set.Add("Default");
            set.Add("Compact", s => s.Global("density", "compact"));
        }
    }

    private static WorkbenchCatalog Catalog()
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddGlobal("Density", ["comfortable", "compact"], defaultValue: "comfortable");
        options.AddScenarios(new DensityScenarios());
        return CatalogBuilder.Build(options);
    }

    [Fact]
    public void The_default_and_the_scenario_value_are_cascaded_by_id()
    {
        var catalog = Catalog();
        Assert.Equal("comfortable", this.RenderScenario(catalog, catalog.FindScenario("tests-density-label--default")!).Find(".density").TextContent);
    }

    [Fact]
    public void A_scenario_value_wins_over_the_default()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-density-label--compact")!);
        Assert.Equal("compact", cut.Find(".density").TextContent);
        Assert.Equal("compact", cut.Session().Context!.Global("density"));
        Assert.Contains(ScenarioFacts.Describe(cut.Instance.Scenario, catalog.Options), f => f.Kind == "Global" && f.Text == "Density: compact");
    }

    [Fact]
    public void The_toolbar_choice_wins_and_unknown_values_are_ignored()
    {
        var catalog = Catalog();
        var scenario = catalog.FindScenario("tests-density-label--compact")!;
        var chosen = catalog.Options.ResolveGlobals(scenario.Environment, new Dictionary<string, string> { ["density"] = "comfortable" });
        Assert.Equal("comfortable", chosen["density"]);
        var invalid = catalog.Options.ResolveGlobals(scenario.Environment, new Dictionary<string, string> { ["density"] = "huge" });
        Assert.Equal("compact", invalid["density"]);
    }

    [Fact]
    public void Globals_are_validated_when_added()
    {
        var options = new WorkbenchOptions();
        Assert.Throws<ArgumentException>(() => options.AddGlobal("Empty", []));
        Assert.Throws<ArgumentException>(() => options.AddGlobal("Brand", ["a", "b"], defaultValue: "c"));
        options.AddGlobal("Brand", ["a"]);
        Assert.Throws<ArgumentException>(() => options.AddGlobal("brand", ["x"]));
        Assert.Equal("brand", options.Globals[0].Id);
    }
}
