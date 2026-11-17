using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Values;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Fakes;
using BlazorWorkbench.Demo.Scenarios;

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

    [Fact]
    public async Task Custom_handler_responses_stay_pending_until_the_body_is_read()
    {
        var (client, session) = await CreateAsync(h => h.Get("/x").Handle((request, _) =>
        {
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Accepted) { Content = new StringContent("{\"a\":1}", System.Text.Encoding.UTF8, "application/json") };
            response.Headers.Add("X-Custom", "1");
            return Task.FromResult(response);
        }));
        using var response = await client.GetAsync("x", HttpCompletionOption.ResponseHeadersRead, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, session.Activity.Pending); // readiness waits for the render the body causes
        Assert.Equal(202, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(7, response.Content.Headers.ContentLength);
        Assert.Equal("1", response.Headers.GetValues("X-Custom").Single());
        Assert.Equal("{\"a\":1}", await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(0, session.Activity.Pending);
    }

    [Fact]
    public async Task Custom_handler_stream_ends_activity_when_read_to_the_end()
    {
        var (client, session) = await CreateAsync(h => h.Get("/x").Handle((_, _, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage { Content = new StringContent("streamed") })));
        using var response = await client.GetAsync("x", HttpCompletionOption.ResponseHeadersRead, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(1, session.Activity.Pending);
        await using (var stream = await response.Content.ReadAsStreamAsync(Xunit.TestContext.Current.CancellationToken))
        using (var reader = new StreamReader(stream))
            Assert.Equal("streamed", await reader.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.Equal(0, session.Activity.Pending);
    }

    [Fact]
    public async Task Cancellable_handler_receives_the_request_token()
    {
        var observed = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, session) = await CreateAsync(h => h.Get("/slow").Handle(async (_, _, token) =>
        {
            observed.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return new System.Net.Http.HttpResponseMessage();
        }));
        using var cts = new CancellationTokenSource();
        var request = client.GetAsync("slow", cts.Token);
        var token = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        Assert.True(token.CanBeCanceled);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(0, session.Activity.Pending);
    }

    [Fact]
    public void Builder_records_which_handler_was_set_last()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new HttpSet(h =>
        {
            h.Get("/a").Handle((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage()));
            h.Get("/b").Handle((_, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage()))
                .Handle((_, _, _) => Task.FromResult(new System.Net.Http.HttpResponseMessage()));
        }, offline: false));
        var rules = CatalogBuilder.Build(options).Scenarios[0].Environment.Http!.Rules;
        Assert.NotNull(rules[0].Handler);
        Assert.Null(rules[0].CancellableHandler);
        Assert.Null(rules[1].Handler);
        Assert.NotNull(rules[1].CancellableHandler);
        Assert.All(rules, r => Assert.True(r.HasCustomHandler));
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
        _ = DemoData.Products;
        _ = typeof(Person);
    }
}
