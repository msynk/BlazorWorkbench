using BlazorWorkbench.Catalog;
using BlazorWorkbench.Core.Tests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Core.Tests;

public class CatalogTests
{
    private static WorkbenchCatalog Build(Action<WorkbenchOptions>? configure = null, Func<Type, bool>? isService = null)
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<TestButton>();
        options.ExcludedNamespaces.Add("BlazorWorkbench.Core.Tests.Invalid");
        configure?.Invoke(options);
        return CatalogBuilder.Build(options, isService);
    }

    [Fact]
    public void Discovers_components_and_assigns_stable_ids()
    {
        var catalog = Build();

        var button = catalog.FindComponent("inputs-test-button");
        Assert.NotNull(button);
        Assert.Equal(["inputs-test-button--primary", "inputs-test-button--danger", "inputs-test-button--with-items"], button.Scenarios.Select(s => s.Id));
        Assert.Equal("Inputs", button.Category);
        Assert.Contains("inputs", button.Tags);
    }

    [Fact]
    public void Discovery_is_deterministic()
    {
        var first = Build().Scenarios.Select(s => s.Id).ToArray();
        var second = Build().Scenarios.Select(s => s.Id).ToArray();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Generates_default_scenario_for_components_without_scenarios()
    {
        var catalog = Build();
        var plain = catalog.Components.Single(c => c.ComponentType == typeof(PlainComponent));
        var scenario = Assert.Single(plain.Scenarios);
        Assert.True(scenario.IsGenerated);
        Assert.Contains(catalog.IssuesFor(plain), i => i.Code == "WB1015");
    }

    [Fact]
    public void Skips_routable_ignored_and_open_generic_components_with_reasons()
    {
        var catalog = Build();
        Assert.DoesNotContain(catalog.Components, c => c.ComponentType == typeof(RoutablePage));
        Assert.DoesNotContain(catalog.Components, c => c.ComponentType == typeof(IgnoredComponent));
        Assert.Contains(catalog.Issues, i => i.Code == "WB0004" && i.Message.Contains("Generic<TItem>"));
        Assert.Contains(catalog.Components, c => c.ComponentType == typeof(Generic<int>));
    }

    [Fact]
    public void Includes_routable_components_when_enabled()
    {
        var catalog = Build(o => o.IncludeRoutableComponents = true);
        Assert.Contains(catalog.Components, c => c.ComponentType == typeof(RoutablePage));
    }

    [Fact]
    public void Explicit_registration_works_without_assembly_scanning()
    {
        var options = new WorkbenchOptions();
        options.AddComponent<PlainComponent>();
        var catalog = CatalogBuilder.Build(options);
        Assert.Single(catalog.Components);
        Assert.Equal("PlainComponent", catalog.Components[0].DisplayName);
    }

    [Fact]
    public void Reads_parameter_metadata_and_documentation_kinds()
    {
        var metadata = Build().FindComponent("inputs-test-button")!.Metadata;

        var label = metadata.FindParameter("Label")!;
        Assert.True(label.IsRequired);
        Assert.Equal(EditorKind.Text, label.Editor);

        Assert.Equal(EditorKind.Enum, metadata.FindParameter("Tone")!.Editor);
        Assert.Equal(["Neutral", "Danger"], metadata.FindParameter("Tone")!.EnumValues);
        Assert.Equal("Tone.Neutral", metadata.FindParameter("Tone")!.DefaultValueDisplay);
        Assert.Equal(EditorKind.FlagsEnum, metadata.FindParameter("Styles")!.Editor);
        Assert.True(metadata.FindParameter("Count")!.IsNullable);
        Assert.Equal(EditorKind.Integer, metadata.FindParameter("Count")!.Editor);
        Assert.Equal(ParameterKind.ChildContent, metadata.FindParameter("ChildContent")!.Kind);
        Assert.Equal(ParameterKind.EventCallback, metadata.FindParameter("OnClick")!.Kind);
        Assert.Equal("ValueChanged", metadata.FindParameter("Value")!.BoundTo);
        Assert.Equal("Value", metadata.FindParameter("ValueChanged")!.BoundTo);
        Assert.Equal(ParameterKind.Cascading, metadata.FindParameter("Theme")!.Kind);
        Assert.Equal(EditorKind.Unsupported, metadata.FindParameter("Items")!.Editor);
        Assert.Contains(metadata.Injections, i => i.ServiceType == typeof(IGreeter));
    }

    [Fact]
    public void Scenario_service_types_are_computed()
    {
        var scenario = Build().FindScenario("inputs-test-button--primary")!;
        Assert.Contains(typeof(IGreeter), scenario.ServiceTypes);
    }

    [Fact]
    public void Dependency_validation_reports_missing_services()
    {
        var catalog = Build(isService: _ => false);
        var plain = catalog.Issues.Where(i => i.Code == "WB1005").ToList();
        Assert.Empty(plain); // TestButton's IGreeter is supplied by the scenario.

        var options = new WorkbenchOptions();
        options.AddComponent<TestButton>();
        var bare = CatalogBuilder.Build(options, _ => false);
        Assert.Contains(bare.Issues, i => i.Code == "WB1005" && i.Message.Contains("IGreeter"));
    }

    [Fact]
    public void Filter_supports_globs_tags_and_alternatives()
    {
        var catalog = Build();
        Assert.Equal(3, catalog.Filter("inputs-*").Count);
        Assert.Equal(3, catalog.Filter("tag:inputs").Count);
        Assert.Equal(2, catalog.Filter("*--primary, *--danger").Count);
        Assert.Equal(catalog.Scenarios.Count, catalog.Filter(null).Count);
    }

    [Fact]
    public void Scenario_set_failures_are_isolated()
    {
        var options = new WorkbenchOptions();
        options.AddComponent<PlainComponent>();
        options.AddScenarios(new ThrowingSet());
        var catalog = CatalogBuilder.Build(options);
        Assert.Contains(catalog.Issues, i => i.Code == "WB0003");
        Assert.Single(catalog.Components);
    }

    [Fact]
    public void Duplicate_scenario_keys_are_reported_and_disambiguated()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new DuplicateSet());
        var catalog = CatalogBuilder.Build(options);
        Assert.Contains(catalog.Issues, i => i.Code == "WB0006");
        Assert.Equal(["components-plain-component--same", "components-plain-component--same-2"], catalog.Scenarios.Select(s => s.Id));
    }

    private sealed class ThrowingSet : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set) => throw new InvalidOperationException("boom");
    }

    private sealed class DuplicateSet : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set) => set.Add("Same").Add("same");
    }
}

public class ValidationTests
{
    private static IReadOnlyList<CatalogIssue> Validate(Action<ScenarioBuilder<TestButton>> configure)
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new InlineSet(configure));
        return CatalogBuilder.Build(options).Issues;
    }

    private sealed class InlineSet(Action<ScenarioBuilder<TestButton>> configure) : ScenarioSet<TestButton>
    {
        protected override void Configure(ScenarioSetBuilder<TestButton> set) =>
            set.Setup(s => s.Service<IGreeter, Greeter>()).Add("Case", configure);
    }

    [Fact]
    public void Unknown_parameter_is_an_error() =>
        Assert.Contains(Validate(s => s.Parameter("Nope", 1)), i => i.Code == "WB1001");

    [Fact]
    public void Wrong_value_type_is_an_error() =>
        Assert.Contains(Validate(s => s.Parameter("Count", "three")), i => i.Code == "WB1002");

    [Fact]
    public void Null_for_non_nullable_value_type_is_an_error() =>
        Assert.Contains(Validate(s => s.Parameter("Disabled", null)), i => i.Code == "WB1002");

    [Fact]
    public void Missing_required_parameter_is_a_warning()
    {
        var issue = Assert.Single(Validate(_ => { }), i => i.Code == "WB1003");
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Missing_cascading_value_is_a_warning() =>
        Assert.Contains(Validate(s => s.Parameter(b => b.Label, "x")), i => i.Code == "WB1004");

    [Fact]
    public void Unknown_culture_viewport_and_theme_are_errors()
    {
        var issues = Validate(s => s.Parameter(b => b.Label, "x").Culture("xx-NOPE").Viewport("watch").Theme("neon"));
        Assert.Contains(issues, i => i.Code == "WB1007");
        Assert.Contains(issues, i => i.Code == "WB1008");
        Assert.Contains(issues, i => i.Code == "WB1009");
    }

    [Fact]
    public void Interaction_with_unknown_callback_is_an_error() =>
        Assert.Contains(Validate(s => s.Parameter(b => b.Label, "x").Interaction("t", i => i.ExpectCallback("OnMissing"))), i => i.Code == "WB1011");

    [Fact]
    public void Throwing_service_configuration_is_an_error() =>
        Assert.Contains(Validate(s => s.Services(_ => throw new InvalidOperationException("bad"))), i => i.Code == "WB1006");

    [Fact]
    public void Overriding_time_provider_is_rejected() =>
        Assert.Contains(Validate(s => s.Services(c => c.AddSingleton(TimeProvider.System))), i => i.Code == "WB1022");

    [Fact]
    public void Unknown_fixture_is_an_error() =>
        Assert.Contains(Validate(s => s.Fixture(b => b.Items, "Missing")), i => i.Code == "WB1014");

    [Fact]
    public void Invalid_http_rule_is_an_error() =>
        Assert.Contains(Validate(s => s.Http(h => h.On("BREW", "/coffee"))), i => i.Code == "WB1010");

    [Fact]
    public void Valid_scenario_has_no_errors()
    {
        var issues = Validate(s => s.Parameter(b => b.Label, "Save").Cascade(new Theme("light")).Culture("fa-IR"));
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Text_content_on_non_fragment_parameter_is_an_error()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new TextSet());
        Assert.Contains(CatalogBuilder.Build(options).Issues, i => i.Code == "WB1002");
    }

    private sealed class TextSet : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set) =>
            set.Add("Bad", s => s.Parameter("Text", (RenderFragment)(_ => { })));
    }
}
