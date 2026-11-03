using System.Globalization;
using System.Net;
using System.Text.Json;
using BlazorWorkbench.Components.Preview;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Testing.Bunit;
using BlazorWorkbench.Values;
using Bunit;
using SampleComponents;
using SampleComponents.Inputs;
using SampleWorkbench.Scenarios;

namespace BlazorWorkbench.Components.Tests;

/// <summary>Renders sample scenarios with bUnit (no browser) through the same ScenarioHost used by previews.</summary>
public class ScenarioHostTests : BunitContext
{
    private static readonly System.Reflection.Assembly[] Assemblies = [typeof(ButtonScenarios).Assembly, typeof(Button).Assembly];

    public ScenarioHostTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Renders_parameters_and_logs_unhandled_callbacks()
    {
        var cut = this.RenderScenario<ButtonScenarios>("primary");
        var button = cut.Find("button");
        Assert.Contains("Save changes", button.TextContent);

        button.Click();
        button.Click();

        Assert.Equal(2, cut.CallbackCount("OnClick"));
        Assert.Contains(cut.Session().Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.Callback && d.Message.StartsWith("OnClick(", StringComparison.Ordinal));
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void Captured_attributes_and_enums_are_applied()
    {
        var cut = this.RenderScenario<ButtonScenarios>("large-icon-only");
        var button = cut.Find("button");
        Assert.Equal("Add to favorites", button.GetAttribute("aria-label"));
        Assert.Contains("btn-large", button.ClassName);
    }

    [Fact]
    public void Two_way_binding_updates_the_bound_parameter()
    {
        var cut = this.RenderScenario<TextFieldScenarios>("empty");
        cut.Find("input").Input("ada@example.com");

        Assert.Equal("ada@example.com", cut.Find("input").GetAttribute("value"));
        Assert.Equal(ArgKind.Value, cut.Instance.Overrides["Value"].Kind);
        Assert.Equal(1, cut.CallbackCount("ValueChanged"));
    }

    [Fact]
    public void Inspector_overrides_apply_and_unset_remounts()
    {
        var cut = this.RenderScenario<ButtonScenarios>("secondary");
        cut.InvokeAsync(() => cut.Instance.SetOverrides(new Dictionary<string, ArgOverride>
        {
            ["Disabled"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement(true)),
            ["ChildContent"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement("Renamed")),
        }));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        Assert.Contains("Renamed", cut.Find("button").TextContent);

        cut.InvokeAsync(() => cut.Instance.SetOverrides(new Dictionary<string, ArgOverride> { ["Variant"] = ArgOverride.Unset }));
        Assert.Contains("btn-primary", cut.Find("button").ClassName);
        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Invalid_overrides_are_reported_without_corrupting_state()
    {
        var cut = this.RenderScenario<CounterScenarios>("bounded");
        cut.InvokeAsync(() => cut.Instance.SetOverrides(new Dictionary<string, ArgOverride>
        {
            ["Start"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement("not a number")),
            ["Nope"] = ArgOverride.FromValue(JsonSerializer.SerializeToElement(1)),
        }));
        Assert.Contains("Start", cut.Instance.ArgErrors.Keys);
        Assert.Contains("Nope", cut.Instance.ArgErrors.Keys);
        Assert.Equal("9", cut.Find("output").TextContent);
    }

    [Fact]
    public void Fake_http_backend_serves_rules()
    {
        var cut = this.RenderScenario<WeatherScenarios>("loaded");
        cut.WaitForScenarioIdle();
        cut.WaitForAssertion(() => Assert.Equal("31.5 °C", cut.Find("[data-testid=temperature]").TextContent));
        var exchange = Assert.Single(cut.Session().Http.Exchanges);
        Assert.Equal(200, exchange.Status);
        Assert.Contains("city=Tehran", exchange.Url);
    }

    [Fact]
    public void Gated_requests_hold_until_released()
    {
        var cut = this.RenderScenario<WeatherScenarios>("loading");
        cut.WaitForAssertion(() => Assert.Contains("Loading forecast", cut.Markup));
        Assert.Equal(0, cut.Session().Activity.Pending); // held requests do not block readiness
        Assert.Equal(GateStatus.Pending, Assert.Single(cut.Session().Gates.Snapshot()).Status);

        cut.Session().Gates.Release("weather");
        cut.WaitForAssertion(() => Assert.Contains("31.5", cut.Find("[data-testid=temperature]").TextContent));
    }

    [Fact]
    public void Failure_and_retry_states_are_reproducible()
    {
        var cut = this.RenderScenario<WeatherScenarios>("recovers-after-retry");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[role=alert]")));
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Contains("31.5", cut.Find("[data-testid=temperature]").TextContent));
    }

    [Theory]
    [InlineData("offline", "Simulated offline mode")]
    [InlineData("timeout", "timed out")]
    [InlineData("server-error", "500")]
    public void Network_faults_surface_in_the_component(string key, string expected)
    {
        var cut = this.RenderScenario<WeatherScenarios>(key);
        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void Simulated_users_drive_authorization()
    {
        var admin = this.RenderScenario<UserGreetingScenarios>("administrator");
        admin.WaitForAssertion(() => Assert.Contains("Administrator", admin.Markup));
        Assert.Contains("Grace Hopper", admin.Markup);

        using var other = new BunitContext();
        var anonymous = other.RenderScenario<UserGreetingScenarios>("anonymous");
        anonymous.WaitForAssertion(() => Assert.Contains("Sign in", anonymous.Markup));
        Assert.DoesNotContain("Administrator", anonymous.Markup);
    }

    [Fact]
    public void Clock_and_culture_are_deterministic()
    {
        var cut = this.RenderScenario<DateLabelScenarios>("german");
        Assert.Contains("Freitag, 20. März 2026", cut.Markup);
        Assert.Contains("3 h ago", cut.Markup);
    }

    [Fact]
    public void Service_overrides_are_isolated_per_session()
    {
        var first = this.RenderScenario<ProductListScenarios>("in-stock");
        using var otherContext = new BunitContext();
        var second = otherContext.RenderScenario<ProductListScenarios>("in-stock");

        var a = first.Services.GetService(typeof(IProductService));
        var b = second.Services.GetService(typeof(IProductService));
        Assert.NotNull(a);
        Assert.NotSame(a, b);
        first.WaitForAssertion(() => Assert.Equal(3, first.FindAll(".product").Count));
    }

    [Fact]
    public void Fixtures_supply_complex_parameters()
    {
        var cut = this.RenderScenario<PeopleTableScenarios>("few-rows");
        Assert.Equal(3, cut.FindAll("tbody tr").Count);

        using var again = new BunitContext();
        var repeat = again.RenderScenario<PeopleTableScenarios>("few-rows");
        Assert.Equal(cut.Find("tbody").TextContent, repeat.Find("tbody").TextContent); // seeded random
    }

    [Fact]
    public void Render_exceptions_are_contained_and_reported()
    {
        var cut = this.RenderScenario<FlakyWidgetScenarios>("throws-while-rendering");
        Assert.NotNull(cut.Find("[data-workbench-error]"));
        Assert.Contains("configured to fail", cut.Markup);
        Assert.Throws<ScenarioAssertionException>(() => cut.AssertNoScenarioErrors());
        Assert.Contains(cut.Session().Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.Render && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Event_handler_exceptions_are_contained()
    {
        var cut = this.RenderScenario<FlakyWidgetScenarios>("healthy");
        cut.Find("button").Click();
        Assert.Contains("simulated defect", cut.Find("[data-workbench-error]").TextContent);
    }

    [Fact]
    public void Edit_context_is_supplied_for_form_components()
    {
        var cut = this.RenderScenario<SignupFormScenarios>("empty");
        cut.Find("form").Submit();
        Assert.Equal(3, cut.FindAll(".validation-message").Count);
        Assert.Equal(0, cut.CallbackCount("OnSubmit"));
    }

    [Fact]
    public void Navigation_is_intercepted()
    {
        var cut = this.RenderScenario<ToolbarScenarios>("left-to-right");
        var nav = Services.GetService(typeof(Microsoft.AspNetCore.Components.NavigationManager)) as Microsoft.AspNetCore.Components.NavigationManager;
        nav!.NavigateTo("/somewhere-else");
        Assert.DoesNotContain("somewhere-else", nav.Uri);
        Assert.Contains(cut.Session().Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.Navigation);
    }

    [Fact]
    public async Task Session_disposal_cancels_held_requests()
    {
        var cut = this.RenderScenario<WeatherScenarios>("loading");
        var session = cut.Session();
        cut.WaitForAssertion(() => Assert.Single(session.Gates.Snapshot(), g => g.Waiters == 1));
        await session.DisposeAsync();
        cut.WaitForAssertion(() => Assert.Equal(0, session.Gates.Snapshot().Single().Waiters));
    }

    public static IEnumerable<object[]> AllScenarios => WorkbenchBunit.ScenarioIds(Assemblies);

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void Every_sample_scenario_renders_without_errors(string scenarioId)
    {
        var cut = this.RenderScenario(scenarioId, Assemblies);
        cut.WaitForScenarioIdle();
        cut.AssertNoScenarioErrors();
    }

    [Fact]
    public void Unmatched_requests_are_reported_and_never_leave_the_process()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new UnmatchedSet());
        var catalog = Catalog.CatalogBuilder.Build(options);
        var cut = this.RenderScenario(catalog, catalog.Scenarios[0]);
        cut.WaitForAssertion(() => Assert.Contains(cut.Session().Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.Http && d.Message.Contains("No fake HTTP rule matched", StringComparison.Ordinal)));
        Assert.Equal((int)HttpStatusCode.NotFound, cut.Session().Http.Exchanges.Single().Status);
    }

    public sealed class UnmatchedSet : ScenarioSet<SampleComponents.Data.WeatherForecast>
    {
        protected override void Configure(ScenarioSetBuilder<SampleComponents.Data.WeatherForecast> set) =>
            set.Add("No rules", s => s.Parameter(w => w.City, "Nowhere"));
    }
}
