using System.Globalization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Documentation;
using BlazorWorkbench.Testing.Bunit;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorWorkbench.Components.Tests;

/// <summary>Decorators (<c>.Wrap(...)</c>, <c>options.AddDecorator(...)</c>) wrap the scenario's component in other markup.</summary>
public class DecoratorTests : BunitContext
{
    public DecoratorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    /// <summary>A wrapper component that receives the scenario as its child content.</summary>
    public sealed class Frame : ComponentBase
    {
        [Parameter] public RenderFragment? ChildContent { get; set; }
        [Parameter] public string Label { get; set; } = "";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "section");
            builder.AddAttribute(1, "data-frame", Label);
            builder.AddContent(2, ChildContent);
            builder.CloseElement();
        }
    }

    private sealed class WrappedButtonScenarios : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set)
        {
            set.Title("Tests/Wrapped button");
            set.Setup(s => s.Wrap<Frame>(new Dictionary<string, object?> { ["Label"] = "outer" }));
            set.Add("Styled", s => s
                .ChildContent("Save")
                .WrapStyle("width: 320px")
                .Wrap(story => b =>
                {
                    b.OpenElement(0, "form");
                    b.AddAttribute(1, "class", "inner");
                    b.AddContent(2, story);
                    b.CloseElement();
                }, "Form"));
            set.Add("Context", s => s
                .ChildContent("Go")
                .Seed(42)
                .Wrap((story, context) => b =>
                {
                    b.OpenElement(0, "div");
                    b.AddAttribute(1, "data-seed", context.Scenario.Environment.Seed?.ToString(CultureInfo.InvariantCulture));
                    b.AddContent(2, story);
                    b.CloseElement();
                }, "Seeded"));
            set.Add("Throws", s => s.ChildContent("Boom").Wrap(_ => throw new InvalidOperationException("decorator failed"), "Broken"));
        }
    }

    private static WorkbenchCatalog Catalog(Action<WorkbenchOptions>? configure = null)
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddScenarios(new WrappedButtonScenarios());
        configure?.Invoke(options);
        return CatalogBuilder.Build(options);
    }

    [Fact]
    public void Decorators_wrap_the_component_with_the_first_declared_outermost()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-wrapped-button--styled")!);

        var frame = cut.Find("section[data-frame='outer']");
        var styled = frame.QuerySelector("div[data-workbench-wrapper]")!;
        Assert.Equal("width: 320px", styled.GetAttribute("style"));
        var form = styled.QuerySelector("form.inner")!;
        Assert.Contains("Save", form.QuerySelector("button")!.TextContent);
        // Inside the preview root, so readiness checks, scans and screenshots include the wrapper.
        Assert.NotNull(cut.Find("#workbench-preview-root > section[data-frame]"));
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void Decorators_receive_the_scenario_context()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-wrapped-button--context")!);
        Assert.Equal("42", cut.Find("div[data-seed]").GetAttribute("data-seed"));
    }

    [Fact]
    public void Catalog_wide_decorators_wrap_the_scenario_decorators()
    {
        var catalog = Catalog(o => o.AddDecorator("App shell", story => b =>
        {
            b.OpenElement(0, "main");
            b.AddAttribute(1, "class", "app-shell");
            b.AddContent(2, story);
            b.CloseElement();
        }));
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-wrapped-button--styled")!);
        Assert.NotNull(cut.Find("main.app-shell > section[data-frame='outer'] form.inner button"));
    }

    [Fact]
    public void A_failing_decorator_is_reported_as_a_setup_error()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-wrapped-button--throws")!);
        Assert.Contains("decorator failed", cut.Find(".wb-preview-error").TextContent);
        Assert.Throws<ScenarioAssertionException>(cut.AssertNoScenarioErrors);
    }

    [Fact]
    public void Decorators_are_listed_in_the_facts_and_the_razor_snippet()
    {
        var catalog = Catalog(o => o.AddDecorator("App shell", story => story));
        var scenario = catalog.FindScenario("tests-wrapped-button--styled")!;

        var wrappers = ScenarioFacts.Describe(scenario, catalog.Options).Where(f => f.Kind == "Wrapper").Select(f => f.Text).ToArray();
        // Nested types are named with their declaring type, like everywhere else in the Workbench.
        Assert.Equal(["App shell", "DecoratorTests.Frame", "<div style=\"width: 320px\">", "Form"], wrappers);

        var snippet = RazorSnippetGenerator.Generate(scenario, catalog.GetComponentOf(scenario)!.Metadata);
        Assert.Contains("inside: DecoratorTests.Frame > <div style=\"width: 320px\"> > Form", snippet);
    }

    [Fact]
    public void Wrapping_in_a_type_that_is_not_a_component_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScenarioDecorator.Component(typeof(string)));
    }
}
