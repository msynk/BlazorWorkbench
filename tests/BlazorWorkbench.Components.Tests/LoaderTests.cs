using System.Globalization;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Testing.Bunit;
using Bunit;

namespace BlazorWorkbench.Components.Tests;

/// <summary>Loaders (<c>.Load(...)</c>) run before the component renders and feed factories through the scenario context.</summary>
public class LoaderTests : BunitContext
{
    private static readonly TaskCompletionSource s_slow = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LoaderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    private sealed class LoadedButtonScenarios : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set)
        {
            set.Title("Tests/Loaded button");
            set.Add("Loaded", s => s
                .Load(async (ctx, ct) =>
                {
                    await Task.Yield();
                    ctx.Set("label", "Loaded label");
                })
                .ParameterFrom(b => b.ChildContent, ctx => (Microsoft.AspNetCore.Components.RenderFragment)(r => r.AddContent(0, ctx.Get<string>("label")))));
            set.Add("Slow", s => s
                .Load(_ => s_slow.Task)
                .ChildContent("After the wait"));
            set.Add("Broken", s => s
                .Load(_ => throw new InvalidOperationException("sample data missing"))
                .ChildContent("Never"));
        }
    }

    private static WorkbenchCatalog Catalog()
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddScenarios(new LoadedButtonScenarios());
        return CatalogBuilder.Build(options);
    }

    [Fact]
    public void Loaded_values_reach_parameter_factories()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-loaded-button--loaded")!);
        cut.WaitForAssertion(() => Assert.Contains("Loaded label", cut.Find("button").TextContent));
        Assert.Contains(ScenarioFacts.Describe(cut.Instance.Scenario, catalog.Options), f => f.Kind == "Loader");
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void The_preview_stays_busy_until_loaders_finish()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-loaded-button--slow")!);
        Assert.Equal(1, cut.Session().Activity.Pending);
        Assert.Empty(cut.FindAll("button"));
        Assert.Equal("true", cut.Find("#workbench-preview-root").GetAttribute("data-workbench-loading"));

        s_slow.TrySetResult();
        cut.WaitForAssertion(() => Assert.Contains("After the wait", cut.Find("button").TextContent));
        Assert.Equal(0, cut.Session().Activity.Pending);
    }

    [Fact]
    public void A_failing_loader_is_a_setup_error()
    {
        var catalog = Catalog();
        var cut = this.RenderScenario(catalog, catalog.FindScenario("tests-loaded-button--broken")!);
        cut.WaitForAssertion(() => Assert.Contains("sample data missing", cut.Find(".wb-preview-error").TextContent));
        Assert.Equal(0, cut.Session().Activity.Pending);
    }
}
