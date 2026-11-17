using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Demo.Components.Feedback;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Scenarios;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Testing.Bunit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace BlazorWorkbench.Components.Tests;

public class JSInteropTests
{
    /// <summary>Stands in for the browser: records calls and answers "browser:{identifier}".</summary>
    public class FakeBrowserRuntime : IJSRuntime
    {
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "import" && typeof(TValue) == typeof(IJSInProcessObjectReference))
                return ValueTask.FromResult((TValue)(object)new FakeInProcessModule());
            if (identifier == "import")
                return ValueTask.FromResult((TValue)(object)new FakeModule(this, (string)args![0]!));
            if (typeof(TValue) == typeof(IJSVoidResult))
                return ValueTask.FromResult(default(TValue)!);
            return ValueTask.FromResult((TValue)(object)("browser:" + identifier));
        }
    }

    public sealed class FakeInProcessRuntime : FakeBrowserRuntime, IJSInProcessRuntime
    {
        public TResult Invoke<TResult>(string identifier, params object?[]? args)
        {
            Calls.Add(identifier);
            return (TResult)(object)("browser:" + identifier);
        }
    }

    public sealed class FakeModule(FakeBrowserRuntime owner, string path) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            owner.Calls.Add($"{path}#{identifier}");
            return ValueTask.FromResult((TValue)(object)("module:" + identifier));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed record Point(int X, int Y);

    public sealed class JsSet : ScenarioSet<Button>
    {
        protected override void Configure(ScenarioSetBuilder<Button> set)
        {
            set.Setup(s => s.JSInterop(js => js.Setup("theme.get").Returns("from setup")));
            set.Add("Planned", s => s.JSInterop(js =>
            {
                js.Setup("theme.get").Returns("dark");
                js.Setup("geo.position").Returns(new { x = 3, y = 4 });
                js.Setup("count").Returns(42L);
                js.Setup("fail").Throws("Permission denied");
                js.Setup("once").Times(1).Returns("first");
                js.Setup("echo").Handle((call, _) => $"echo:{call.Arguments[0]}");
                js.Setup("held").Hold("js-gate").Returns("released");
                js.Setup("store.*").When(call => call.Arguments is ["key"]).Returns("matched");
                js.Module("./_content/Lib/chart.js", m => m.Setup("render").Returns(7));
            }));
            set.Add("Strict", s => s.JSInterop(js => js.Strict().Setup("allowed").Returns("ok")));
            set.Add("Unplanned");
        }
    }

    private static (ServiceProvider Provider, WorkbenchCatalog Catalog) Build(IJSRuntime? browser = null, bool scopedBrowser = true)
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new JsSet());
        var catalog = CatalogBuilder.Build(options);
        var services = new ServiceCollection();
        if (browser is not null)
            services.AddSingleton(browser);
        else if (scopedBrowser)
            services.AddScoped<IJSRuntime, FakeBrowserRuntime>();
        services.AddBlazorWorkbenchRuntime(options, catalog, ServiceOverrides.CollectTypes(catalog).ToHashSet());
        ServiceOverrides.Apply(services, ServiceOverrides.CollectTypes(catalog));
        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }), catalog);
    }

    private static (IServiceScope Scope, IJSRuntime Runtime, FakeBrowserRuntime Browser, ScenarioSession Session) Activate(string key, IJSRuntime? browser = null)
    {
        var (provider, catalog) = Build(browser);
        var scope = provider.CreateScope();
        var original = (FakeBrowserRuntime)scope.ServiceProvider.GetRequiredService<IJSRuntime>();
        var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
        session.Activate(catalog.Scenarios.First(s => s.Key == key), services: scope.ServiceProvider);
        return (scope, scope.ServiceProvider.GetRequiredService<IJSRuntime>(), original, session);
    }

    [Fact]
    public void Runtime_is_the_original_until_the_scenario_is_active()
    {
        var (provider, catalog) = Build();
        using var scope = provider.CreateScope();
        var early = scope.ServiceProvider.GetRequiredService<IJSRuntime>();
        Assert.IsType<FakeBrowserRuntime>(early);
        // The framework initializes the instance it gets first; the decorator wraps that same instance later.
        Assert.Same(early, scope.ServiceProvider.GetRequiredService<IJSRuntime>());

        scope.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Key == "planned"));
        var late = scope.ServiceProvider.GetRequiredService<IJSRuntime>();
        Assert.IsAssignableFrom<WorkbenchJSRuntime>(late);
        Assert.Same(early, ((WorkbenchJSRuntime)late).Inner);
    }

    [Fact]
    public void Applying_the_rewrite_twice_keeps_the_original_runtime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IJSRuntime, FakeBrowserRuntime>();
        ServiceOverrides.Apply(services, []);
        ServiceOverrides.Apply(services, []);
        Assert.Single(services, d => d.ServiceType == typeof(IJSRuntime) && d.IsKeyedService);
        Assert.Single(services, d => d.ServiceType == typeof(IJSRuntime) && !d.IsKeyedService);
        using var provider = services.AddBlazorWorkbenchRuntime(new WorkbenchOptions()).BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<FakeBrowserRuntime>(scope.ServiceProvider.GetRequiredService<IJSRuntime>());
    }

    [Fact]
    public async Task Planned_calls_never_reach_the_browser_and_are_converted()
    {
        var (scope, js, browser, session) = Activate("planned");
        using var _ = scope;

        Assert.Equal("dark", await js.InvokeAsync<string>("theme.get"));
        Assert.Equal(new Point(3, 4), await js.InvokeAsync<Point>("geo.position"));
        Assert.Equal(42, await js.InvokeAsync<int>("count"));
        await js.InvokeVoidAsync("count");
        Assert.Equal("echo:hi", await js.InvokeAsync<string>("echo", "hi"));
        Assert.Equal("matched", await js.InvokeAsync<string>("store.get", "key"));
        Assert.Empty(browser.Calls);

        Assert.Equal(2, session.JSInterop.Count("count"));
        Assert.Contains(session.JSInterop.Calls, c => c.Identifier == "theme.get" && c.Outcome == "planned" && c.Rule == "theme.get");
    }

    [Fact]
    public async Task Scenario_rules_take_precedence_over_setup_rules()
    {
        var (scope, js, _, _) = Activate("planned");
        using var _1 = scope;
        Assert.Equal("dark", await js.InvokeAsync<string>("theme.get"));

        var (scope2, js2, _, _) = Activate("unplanned");
        using var _2 = scope2;
        Assert.Equal("from setup", await js2.InvokeAsync<string>("theme.get"));
    }

    [Fact]
    public async Task Unplanned_calls_reach_the_browser_and_are_recorded()
    {
        var (scope, js, browser, session) = Activate("planned");
        using var _ = scope;

        Assert.Equal("browser:navigator.share", await js.InvokeAsync<string>("navigator.share", "title"));
        // A When condition that does not hold falls through to the browser.
        Assert.Equal("browser:store.get", await js.InvokeAsync<string>("store.get", "other"));
        Assert.Equal(["navigator.share", "store.get"], browser.Calls);
        var call = session.JSInterop.Calls.First(c => c.Identifier == "navigator.share");
        Assert.Equal("browser", call.Outcome);
        Assert.Equal("\"title\"", call.Arguments);
    }

    [Fact]
    public async Task Planned_errors_throw_js_exceptions_and_times_falls_through()
    {
        var (scope, js, browser, session) = Activate("planned");
        using var _ = scope;

        var ex = await Assert.ThrowsAsync<JSException>(() => js.InvokeAsync<string>("fail").AsTask());
        Assert.Equal("Permission denied", ex.Message);
        Assert.Equal("first", await js.InvokeAsync<string>("once"));
        Assert.Equal("browser:once", await js.InvokeAsync<string>("once"));
        Assert.Equal(["once"], browser.Calls);
        Assert.Contains(session.Diagnostics.Snapshot(), d => d.Source == DiagnosticSource.JsInterop && d.Message.Contains("failed as planned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Held_calls_wait_for_their_gate_without_counting_as_pending()
    {
        var (scope, js, _, session) = Activate("planned");
        using var _ = scope;

        Assert.Contains(session.Gates.Snapshot(), g => g.Name == "js-gate");
        var call = js.InvokeAsync<string>("held").AsTask();
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        Assert.False(call.IsCompleted);
        Assert.Equal(0, session.Activity.Pending);
        Assert.True(session.Gates.Release("js-gate"));
        Assert.Equal("released", await call.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Planned_modules_answer_their_exports_and_forward_the_rest()
    {
        var (scope, js, browser, session) = Activate("planned");
        using var _ = scope;

        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Lib/chart.js");
        Assert.Equal(7, await module.InvokeAsync<int>("render", "canvas"));
        Assert.Empty(browser.Calls); // the real module is imported only when an unplanned export is called

        Assert.Equal("module:destroy", await module.InvokeAsync<string>("destroy"));
        Assert.Equal(["import", "./_content/Lib/chart.js#destroy"], browser.Calls); // imported with the path the component used
        Assert.Equal(1, session.JSInterop.Count("render"));
        await module.DisposeAsync();

        // Other modules are imported for real.
        var other = await js.InvokeAsync<IJSObjectReference>("import", "./other.js");
        Assert.IsType<FakeModule>(other);
    }

    public sealed class FakeInProcessModule : IJSInProcessObjectReference
    {
        public TValue Invoke<TValue>(string identifier, params object?[]? args) => default!;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_planned_module_imported_as_in_process_reference_falls_back_to_the_real_module()
    {
        var (scope, js, browser, session) = Activate("planned");
        using var _ = scope;

        // A planned module cannot stand in for IJSInProcessObjectReference: the real import runs, with a warning.
        var module = await js.InvokeAsync<IJSInProcessObjectReference>("import", "./_content/Lib/chart.js");
        Assert.IsType<FakeInProcessModule>(module);
        Assert.Equal(["import"], browser.Calls);
        Assert.Contains(session.Diagnostics.Snapshot(), d => d.Severity == Diagnostics.DiagnosticSeverity.Warning && d.Message.Contains("IJSInProcessObjectReference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Strict_simulations_fail_unplanned_calls_but_not_framework_calls()
    {
        var (scope, js, browser, session) = Activate("strict");
        using var _ = scope;

        Assert.Equal("ok", await js.InvokeAsync<string>("allowed"));
        await Assert.ThrowsAsync<JSException>(() => js.InvokeAsync<string>("localStorage.getItem", "k").AsTask());
        Assert.Equal("browser:Blazor._internal.focus", await js.InvokeAsync<string>("Blazor._internal.focus"));
        Assert.Equal(["Blazor._internal.focus"], browser.Calls);
        Assert.Contains(session.Diagnostics.Snapshot(), d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Unplanned JavaScript call localStorage.getItem", StringComparison.Ordinal));
    }

    [Fact]
    public void In_process_runtimes_stay_in_process()
    {
        var inProcess = new FakeInProcessRuntime();
        var (provider, catalog) = Build(inProcess);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Key == "planned"));
        var js = Assert.IsAssignableFrom<IJSInProcessRuntime>(scope.ServiceProvider.GetRequiredService<IJSRuntime>());
        Assert.Equal("dark", js.Invoke<string>("theme.get"));
        Assert.Equal("browser:other", js.Invoke<string>("other"));
        Assert.Equal(["other"], inProcess.Calls);
    }

    [Fact]
    public void Interception_can_be_turned_off()
    {
        var options = new WorkbenchOptions { InterceptJSInterop = false };
        options.AddScenarios(new JsSet());
        var catalog = CatalogBuilder.Build(options);
        var services = new ServiceCollection();
        services.AddScoped<IJSRuntime, FakeBrowserRuntime>();
        services.AddBlazorWorkbenchRuntime(options, catalog);
        ServiceOverrides.Apply(services, []);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Key == "planned"));
        Assert.IsType<FakeBrowserRuntime>(scope.ServiceProvider.GetRequiredService<IJSRuntime>());
        Assert.Contains(catalog.Issues, i => i.Code == "WB1027");
    }

    [Fact]
    public void Planned_values_convert_like_js_interop()
    {
        Assert.Equal(5, JSInteropSimulator.Convert<int>(5L));
        Assert.Null(JSInteropSimulator.Convert<string?>(null));
        Assert.Equal(0, JSInteropSimulator.Convert<int>(null));
        Assert.Equal(["a", "b"], JSInteropSimulator.Convert<string[]>(new List<string> { "a", "b" }));
        Assert.Equal(new Point(1, 2), JSInteropSimulator.Convert<Point>(new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 }));
    }

    [Fact]
    public void Bunit_scenarios_answer_planned_calls_and_count_them()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        var cut = context.RenderScenario<WhatsNewBannerScenarios>("storage-blocked");
        cut.WaitForElement(".whats-new");
        cut.Find(".whats-new button").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, cut.JSCallCount("localStorage.setItem")));
        Assert.Equal(1, cut.JSCallCount("localStorage.getItem"));
        cut.AssertNoScenarioErrors();
        _ = typeof(WhatsNewBanner);
    }
}

public class WorkbenchJSModuleTests
{
    private sealed class TrackedModule : IJSObjectReference
    {
        public int Disposals { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Disposing_during_an_import_releases_the_imported_module()
    {
        var import = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new WorkbenchJSModule(null!, "./m.js", _ => new ValueTask<IJSObjectReference>(import.Task));
        var pending = module.GetRealModuleAsync(CancellationToken.None).AsTask();

        await module.DisposeAsync(); // must not dispose the lock the import holds
        var real = new TrackedModule();
        import.SetResult(real);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(1, real.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => module.GetRealModuleAsync(CancellationToken.None).AsTask());
        await module.DisposeAsync(); // idempotent
        Assert.Equal(1, real.Disposals);
    }

    [Fact]
    public async Task Disposing_after_an_import_disposes_the_module_once()
    {
        var real = new TrackedModule();
        var module = new WorkbenchJSModule(null!, "./m.js", _ => ValueTask.FromResult<IJSObjectReference>(real));
        Assert.Same(real, await module.GetRealModuleAsync(CancellationToken.None));
        Assert.Same(real, await module.GetRealModuleAsync(CancellationToken.None));
        await module.DisposeAsync();
        await module.DisposeAsync();
        Assert.Equal(1, real.Disposals);
    }
}
