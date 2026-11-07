using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Values;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SampleComponents;
using SampleComponents.Inputs;
using SampleWorkbench.Fakes;
using SampleWorkbench.Scenarios;

namespace BlazorWorkbench.Components.Tests;

public class ServiceOverrideTests
{
    public interface IClock
    {
        string Now();
    }

    public sealed class RealClock : IClock
    {
        public string Now() => "real";
    }

    public sealed class FakeClock : IClock
    {
        public string Now() => "fake";
    }

    public sealed class ClockConsumer(IClock clock)
    {
        public IClock Clock { get; } = clock;
    }

    private static (ServiceProvider Provider, WorkbenchCatalog Catalog) Build(params ScenarioDefinition[] _)
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new ClockSet());
        var catalog = CatalogBuilder.Build(options);
        var services = new ServiceCollection();
        services.AddSingleton<IClock, RealClock>();
        services.AddSingleton(TimeProvider.System); // framework singletons consume TimeProvider
        services.AddSingleton<ClockConsumer>();
        services.AddBlazorWorkbenchRuntime(options, catalog, ServiceOverrides.CollectTypes(catalog).ToHashSet());
        ServiceOverrides.Apply(services, ServiceOverrides.CollectTypes(catalog));
        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }), catalog);
    }

    public sealed class ClockSet : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set) =>
            set.Add("Fake clock", s => s.Service<IClock, FakeClock>()).Add("Real clock");
    }

    [Fact]
    public void Scenario_override_applies_only_in_active_sessions()
    {
        var (provider, catalog) = Build();
        using (var shell = provider.CreateScope())
            Assert.IsType<RealClock>(shell.ServiceProvider.GetRequiredService<IClock>());

        using var preview = provider.CreateScope();
        preview.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Key == "fake-clock"));
        Assert.IsType<FakeClock>(preview.ServiceProvider.GetRequiredService<IClock>());
        Assert.Same(preview.ServiceProvider.GetRequiredService<IClock>(), preview.ServiceProvider.GetRequiredService<IClock>());

        using var plain = provider.CreateScope();
        plain.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Key == "real-clock"));
        Assert.IsType<RealClock>(plain.ServiceProvider.GetRequiredService<IClock>());
    }

    [Fact]
    public void Time_provider_stays_singleton_and_follows_the_session_clock()
    {
        var (provider, _) = Build();
        var time = provider.GetRequiredService<TimeProvider>();
        Assert.IsType<WorkbenchTimeProvider>(time);
        Assert.Same(time, provider.GetRequiredService<TimeProvider>());

        var options = new WorkbenchOptions();
        var scenario = new ScenarioDefinition { Key = "k", Name = "k", ComponentType = typeof(Button), Environment = new EnvironmentDefinition { Clock = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) } };
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(scenario);
        ScenarioSessionContext.Current = session;
        try
        {
            Assert.Equal(2030, time.GetUtcNow().Year);
        }
        finally
        {
            ScenarioSessionContext.Current = null;
        }

        Assert.NotEqual(2030, time.GetUtcNow().Year);
        _ = options;
    }

    [Fact]
    public async Task Authentication_provider_is_stable_and_switches_on_activation()
    {
        var (provider, _) = Build();
        using var scope = provider.CreateScope();
        var early = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(new ScenarioDefinition
        {
            Key = "u",
            Name = "u",
            ComponentType = typeof(Button),
            Environment = new EnvironmentDefinition { User = new SimulatedUser { Name = "ada", Roles = ["Admin"] } },
        });
        Assert.Same(early, scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>());
        var state = await early.GetAuthenticationStateAsync();
        Assert.True(state.User.IsInRole("Admin"));
        Assert.Equal("ada", state.User.Identity!.Name);
    }

    [Fact]
    public void Activating_twice_is_rejected()
    {
        var (provider, catalog) = Build();
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(catalog.Scenarios[0]);
        Assert.Throws<InvalidOperationException>(() => session.Activate(catalog.Scenarios[0]));
    }

    [Fact]
    public void Describes_service_sources()
    {
        var (provider, catalog) = Build();
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(catalog.Scenarios.First(s => s.Key == "fake-clock"));
        scope.ServiceProvider.GetRequiredService<IClock>();
        var sources = session.DescribeServices();
        Assert.Contains(sources, s => s.ServiceType.Contains("IClock", StringComparison.Ordinal) && s.Source == "Scenario override" && s.Implementation.Contains("FakeClock", StringComparison.Ordinal));
        Assert.Contains(sources, s => s.ServiceType.StartsWith("HttpClient", StringComparison.Ordinal) && s.Source == "Simulated");
    }

    public sealed class KeyedClockSet : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set) =>
            set.Add("Keyed clock", s => s.Services(services => services.AddKeyedSingleton<IClock, FakeClock>("x")));
    }

    [Fact]
    public void Keyed_registrations_never_replace_the_unkeyed_service()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new KeyedClockSet());
        var catalog = CatalogBuilder.Build(options);
        Assert.Contains(catalog.Issues, i => i.Code == "WB1013" && i.Message.Contains("Keyed", StringComparison.Ordinal));
        Assert.Empty(catalog.Scenarios[0].ServiceTypes);

        var services = new ServiceCollection();
        services.AddSingleton<IClock, RealClock>();
        services.AddSingleton(TimeProvider.System);
        // IClock is overridable (another scenario overrides it), so the keyed registration must not hijack it.
        var types = ServiceOverrides.CollectTypes(catalog).Append(typeof(IClock)).ToHashSet();
        services.AddBlazorWorkbenchRuntime(options, catalog, types);
        ServiceOverrides.Apply(services, types);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(catalog.Scenarios[0]);

        Assert.IsType<RealClock>(scope.ServiceProvider.GetRequiredService<IClock>());
        Assert.Contains(session.Diagnostics.Snapshot(), d => d.Severity == Diagnostics.DiagnosticSeverity.Error && d.Message.Contains("Keyed service", StringComparison.Ordinal));
        Assert.NotEmpty(session.DescribeServices());
    }

    [Fact]
    public void Scenario_context_uses_the_activation_culture()
    {
        var (provider, catalog) = Build();
        using var scope = provider.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(catalog.Scenarios[0], new ActivationOptions(Culture: "fr-FR"));
        Assert.Equal("fr-FR", session.Culture!.Name);
        Assert.Equal("fr-FR", session.Context!.Culture.Name);
    }
}

public class HttpSimulationTests
{
    private static async Task<(HttpClient Client, ScenarioSession Session)> CreateAsync(Action<HttpSimulationBuilder> http, bool offline = false)
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new HttpSet(http, offline));
        var catalog = CatalogBuilder.Build(options);
        var session = new ScenarioSession(new WorkbenchCatalogAccessor(options, catalog), []);
        session.Activate(catalog.Scenarios[0]);
        await Task.Yield();
        return (new HttpClient(session.Http, false) { BaseAddress = new Uri(HttpSimulation.DefaultBaseAddress) }, session);
    }

    private sealed class HttpSet(Action<HttpSimulationBuilder> http, bool offline) : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set) => set.Add("Http", s => s.Http(http).Offline(offline));
    }

    [Fact]
    public async Task Times_rules_fall_through_in_order()
    {
        var (client, _) = await CreateAsync(h =>
        {
            h.Get("/api/x").Times(2).Fail(System.Net.HttpStatusCode.ServiceUnavailable);
            h.Get("/api/x").RespondText("ok");
        });
        Assert.Equal(503, (int)(await client.GetAsync("api/x")).StatusCode);
        Assert.Equal(503, (int)(await client.GetAsync("api/x")).StatusCode);
        Assert.Equal("ok", await client.GetStringAsync("api/x"));
    }

    [Fact]
    public async Task Faults_mimic_real_exceptions()
    {
        var (client, _) = await CreateAsync(h =>
        {
            h.Get("/net").NetworkError();
            h.Get("/slow").Timeout();
        });
        var network = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("net"));
        Assert.Equal(HttpRequestError.ConnectionError, network.HttpRequestError);
        var timeout = await Assert.ThrowsAsync<TaskCanceledException>(() => client.GetAsync("slow"));
        Assert.IsType<TimeoutException>(timeout.InnerException);
    }

    [Fact]
    public async Task Offline_fails_every_request()
    {
        var (client, _) = await CreateAsync(h => h.Get("/x").RespondText("never"), offline: true);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("x"));
    }

    [Fact]
    public async Task Latency_and_activity_tracking()
    {
        var (client, session) = await CreateAsync(h => h.Get("/x").RespondJson(new { a = 1 }).Delay(TimeSpan.FromMilliseconds(80)));
        var pending = client.GetAsync("x");
        Assert.Equal(1, session.Activity.Pending);
        var response = await pending;
        Assert.Equal(0, session.Activity.Pending);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        // Task.Delay runs on the system tick (about 15.6 ms on Windows), so the measured time can be up to one tick short.
        Assert.True(session.Http.Exchanges.Single().DurationMs >= 60);
    }

    [Fact]
    public async Task Failed_gate_becomes_network_error()
    {
        var (client, session) = await CreateAsync(h => h.Get("/x").Hold("g").RespondText("late"));
        var request = client.GetAsync("x");
        await Task.Delay(50);
        session.Gates.Fail("g", "server down");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => request);
        Assert.Contains("server down", ex.Message);
    }

    [Fact]
    public async Task Urls_are_redacted_in_diagnostics()
    {
        var (client, session) = await CreateAsync(h => h.Get("/x").RespondText("ok"));
        await client.GetAsync("x?access_token=abc123");
        Assert.DoesNotContain("abc123", session.Http.Exchanges.Single().Url);
        Assert.All(session.Diagnostics.Snapshot(), d => Assert.DoesNotContain("abc123", d.Message));
    }
}

public class InspectorTests : BunitContext
{
    [Fact]
    public void Renders_editors_and_raises_overrides()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();
        var catalog = CatalogBuilder.Build(options);
        Services.AddSingleton(new WorkbenchCatalogAccessor(options, catalog));
        var scenario = catalog.FindScenario("inputs-button--primary")!;
        Dictionary<string, ArgOverride>? changed = null;

        var cut = Render<ParameterInspector>(p => p
            .Add(i => i.Component, catalog.GetComponentOf(scenario)!)
            .Add(i => i.Scenario, scenario)
            .Add(i => i.Overrides, new Dictionary<string, ArgOverride>())
            .Add(i => i.OverridesChanged, (Dictionary<string, ArgOverride> o) => changed = o));

        Assert.Equal("Primary", cut.Find("#wb-param-Variant").GetAttribute("value") ?? cut.Find("#wb-param-Variant option[selected]").TextContent);
        cut.Find("#wb-param-Disabled").Change(true);
        Assert.Equal(JsonValueKind.True, changed!["Disabled"].Value!.Value.ValueKind);

        cut.Find("#wb-param-Variant").Change("Danger");
        Assert.Equal("Danger", changed["Variant"].Value!.Value.GetString());
    }

    [Fact]
    public void Invalid_text_shows_error_and_does_not_apply()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();
        var catalog = CatalogBuilder.Build(options);
        Services.AddSingleton(new WorkbenchCatalogAccessor(options, catalog));
        var scenario = catalog.FindScenario("inputs-counter--default")!;
        var calls = 0;

        var cut = Render<ParameterInspector>(p => p
            .Add(i => i.Component, catalog.GetComponentOf(scenario)!)
            .Add(i => i.Scenario, scenario)
            .Add(i => i.Overrides, new Dictionary<string, ArgOverride>())
            .Add(i => i.OverridesChanged, (Dictionary<string, ArgOverride> _) => calls++));

        cut.Find("#wb-param-Step").Change("five");
        Assert.Equal(0, calls);
        Assert.Contains("is not a valid int", cut.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Fixture_parameters_offer_named_fixtures()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();
        var catalog = CatalogBuilder.Build(options);
        Services.AddSingleton(new WorkbenchCatalogAccessor(options, catalog));
        var scenario = catalog.FindScenario("data-data-table--few-rows")!;

        var cut = Render<ParameterInspector>(p => p
            .Add(i => i.Component, catalog.GetComponentOf(scenario)!)
            .Add(i => i.Scenario, scenario)
            .Add(i => i.Overrides, new Dictionary<string, ArgOverride>()));

        var options2 = cut.FindAll("#wb-param-Items option").Select(o => o.TextContent).ToList();
        Assert.Contains("Fifty people", options2);
        Assert.Contains("Nobody", options2);
        _ = SampleData.Products;
        _ = typeof(Person);
    }
}
