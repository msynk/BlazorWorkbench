using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing.Tests;

public class PublishAndVariantTests
{
    private static ScenarioDto Scenario(string key, bool generated = false, params string[] tags) => new()
    {
        Id = "c--" + key,
        ComponentId = "c",
        Key = key,
        Name = key,
        IsGenerated = generated,
        Tags = tags,
    };

    private static ComponentDto Component(params ScenarioDto[] scenarios) => new()
    {
        Id = "c",
        Title = "C",
        Name = "C",
        FullName = "Lib.C",
        AssemblyName = "Lib",
        Scenarios = scenarios,
    };

    private static readonly string[] Internal = ["internal"];

    [Fact]
    public void Excluded_scenarios_are_never_published_even_when_every_scenario_is_generated()
    {
        var published = CatalogPublisher.PublishedScenarios(Component(Scenario("default", generated: true), Scenario("secret", generated: true, "internal")), Internal);
        Assert.Equal(["default"], published.Select(s => s.Key));
    }

    [Fact]
    public void Generated_scenarios_are_published_only_without_hand_written_ones()
    {
        var mixed = CatalogPublisher.PublishedScenarios(Component(Scenario("default", generated: true), Scenario("primary"), Scenario("hidden", false, "INTERNAL")), Internal);
        Assert.Equal(["primary"], mixed.Select(s => s.Key));

        // When every hand-written scenario is excluded, the generated ones stand in.
        var fallback = CatalogPublisher.PublishedScenarios(Component(Scenario("default", generated: true), Scenario("hidden", false, "internal")), Internal);
        Assert.Equal(["default"], fallback.Select(s => s.Key));
    }

    [Fact]
    public void Empty_host_lists_fall_back_to_the_default_viewports_and_themes()
    {
        var catalog = new CatalogDto();
        Assert.Equal("laptop", CatalogVariants.DefaultViewport(catalog).Name);
        Assert.Equal("mobile", CatalogVariants.DefaultViewport(catalog, "MOBILE").Name);
        Assert.Equal("light", CatalogVariants.DefaultTheme(catalog).Name);

        var variants = WorkbenchTestRunner.Variants(catalog, Scenario("a"), new TestRunOptions());
        Assert.Equal(["laptop-light"], variants.Select(v => v.Name));
    }

    [Fact]
    public void Host_lists_are_used_when_present()
    {
        var catalog = new CatalogDto
        {
            Viewports = [new ViewportPreset("phone", 360, 640, 2, true)],
            Themes = [new ThemeDto("brand", ColorScheme.Dark, "brand")],
        };
        Assert.Equal("phone", CatalogVariants.DefaultViewport(catalog, "laptop").Name);
        Assert.Equal(["phone-brand"], WorkbenchTestRunner.Variants(catalog, Scenario("a"), new TestRunOptions()).Select(v => v.Name));
        Assert.Throws<WorkbenchInfrastructureException>(() => WorkbenchTestRunner.Variants(catalog, Scenario("a"), new TestRunOptions { Viewports = ["laptop"] }));
    }

    [Fact]
    public void Llms_txt_lists_components_with_links_parameters_and_scenarios()
    {
        var component = new ComponentDto
        {
            Id = "inputs-button",
            Title = "Inputs/Button",
            Name = "Button",
            FullName = "Lib.Button",
            AssemblyName = "Lib",
            Category = "Inputs",
            Summary = "A button.\nSecond line.",
            Parameters = [new() { Name = "Variant", TypeName = "ButtonVariant", Kind = BlazorWorkbench.Catalog.ParameterKind.Value }],
        };
        var text = CatalogPublisher.LlmsText("Trailhead UI", null, [(component, [Scenario("primary")])]);

        Assert.StartsWith("# Trailhead UI", text, StringComparison.Ordinal);
        Assert.Contains("> Component catalog of Trailhead UI: 1 Razor components", text, StringComparison.Ordinal);
        Assert.Contains("## Inputs", text, StringComparison.Ordinal);
        Assert.Contains("- [Inputs/Button](components/inputs-button.html): A button. Second line. Parameters: Variant. Scenarios: primary.", text, StringComparison.Ordinal);
    }
}
