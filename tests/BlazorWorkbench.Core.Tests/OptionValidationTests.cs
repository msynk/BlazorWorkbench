using BlazorWorkbench.Catalog;
using BlazorWorkbench.Core.Tests.Components;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Validation of the options and builders added for decorators, globals, panels and catalog references.</summary>
public class OptionValidationTests
{
    public sealed class MismatchedConstructor(int start, int end)
    {
        public int Min { get; } = start;
        public int Max { get; } = end;
    }

    [Fact]
    public void Types_whose_constructor_does_not_bind_to_properties_are_not_json_editable() =>
        Assert.False(ParameterValueConverter.IsJsonEditable(typeof(MismatchedConstructor)));

    [Theory]
    [InlineData("Theme")]          // would overwrite data-theme
    [InlineData("Workbench state")] // would corrupt data-workbench-state
    [InlineData("Größe")]          // not an ASCII attribute name
    [InlineData("!!!")]            // no id at all
    public void Toolbar_globals_cannot_take_reserved_or_unusable_ids(string label) =>
        Assert.Throws<ArgumentException>(() => new WorkbenchOptions().AddGlobal(label, ["a", "b"]));

    [Fact]
    public void Scenario_values_of_globals_are_validated_and_ignored_when_invalid()
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddGlobal("Density", ["comfortable", "compact"], defaultValue: "comfortable");
        options.AddScenarios(new GlobalScenarios());
        var catalog = CatalogBuilder.Build(options);

        var issues = catalog.Issues.Where(i => i.Code == "WB1028").Select(i => i.ScenarioId).ToArray();
        Assert.Equal(2, issues.Length);
        var typo = catalog.Scenarios.Single(s => s.Key == "typo");
        Assert.Equal("comfortable", options.ResolveGlobals(typo.Environment, null)["density"]);
    }

    private sealed class GlobalScenarios : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set)
        {
            set.Add("Typo", s => s.Global("density", "compakt"));
            set.Add("Label instead of id", s => s.Global("Density", "compact"));
            set.Add("Fine", s => s.Global("density", "compact"));
        }
    }

    [Fact]
    public void Root_relative_catalog_references_are_accepted_on_every_platform()
    {
        var options = new WorkbenchOptions();
        options.AddCatalogReference("Icons", "/catalogs/icons/");
        Assert.Equal(new Uri("http://localhost:5000/catalogs/icons/"), options.CatalogReferences[0].Resolve(new Uri("http://localhost:5000/app/")));
        Assert.Throws<ArgumentException>(() => options.AddCatalogReference("Other", "file://server/catalog"));
    }

    [Fact]
    public void Duplicate_and_empty_registrations_are_rejected()
    {
        var options = new WorkbenchOptions();
        options.AddToolbarItem<PlainComponent>();
        Assert.Throws<ArgumentException>(options.AddToolbarItem<PlainComponent>);
        Assert.Throws<ArgumentException>(() => options.AddPanel<PlainComponent>("???"));
        Assert.Throws<ArgumentException>(() => options.AddCatalogReference("...", "https://example.com/"));
    }

    [Fact]
    public void Catalog_decorators_can_use_the_scenario_context_and_be_named()
    {
        var options = new WorkbenchOptions();
        options.AddDecorator("Seeded", (story, ctx) => story);
        options.AddDecorator<PlainComponent>(name: "Plain wrapper");
        Assert.Equal(["Seeded", "Plain wrapper"], options.Decorators.Select(d => d.Name));
    }

    [Theory]
    [InlineData("#0f766e", true)]
    [InlineData("rebeccapurple", true)]
    [InlineData("oklch(0.6 0.15 200)", true)]
    [InlineData("rgb(0 0 0 / 50%)", true)]
    [InlineData("url(//evil.example/x)", false)]
    [InlineData("var(--x)", false)]
    [InlineData("red; background: url(x)", false)]
    [InlineData("rgb(1,2,3)) url(x", false)]
    public void Only_color_values_are_accepted_as_accent_colors(string value, bool safe) =>
        Assert.Equal(safe, WorkbenchOptions.IsSafeCssColor(value));
}
