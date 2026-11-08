using System.Globalization;
using System.Net;
using System.Text.Json;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using BlazorWorkbench.Demo.Components.Device;

namespace BlazorWorkbench.Hybrid.Tests;

public sealed class HybridHostTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("wb-hybrid");

    public void Dispose() => _temp.Delete(recursive: true);

    private static (ServiceProvider Services, WorkbenchHybridHost Host) CreateHost(Action<WorkbenchHybridOptions>? configure = null)
    {
        var options = new WorkbenchHybridOptions { Title = "Hybrid tests" };
        options.AddAssemblyOf<ShareCard>();
        options.ScenarioAssemblies.Add(typeof(ShareCardTestScenarios).Assembly);
        configure?.Invoke(options);
        var services = new ServiceCollection();
        var host = services.AddBlazorWorkbenchHybrid(options);
        ServiceOverrides.Apply(services, host.OverridableTypes);
        return (services.BuildServiceProvider(), host);
    }

    [Fact]
    public void Host_page_loads_the_preview_bridge_before_Blazor_and_adds_configured_assets()
    {
        var (_, host) = CreateHost(o =>
        {
            o.PreviewStylesheets.Add("_content/Lib/theme.css");
            o.PreviewStylesheets.Add("x\" onload=\"alert(1)");
            o.PreviewScripts.Add("_content/Lib/lib.js");
        });

        var html = host.BuildHostPage(_ => false);

        var bridge = html.IndexOf("_content/BlazorWorkbench.Components/workbench-preview.js", StringComparison.Ordinal);
        var blazor = html.IndexOf("_framework/blazor.webview.js", StringComparison.Ordinal);
        Assert.True(bridge > 0 && blazor > bridge);
        Assert.Contains("autostart=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("<div id=\"app\"></div>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"_content/Lib/theme.css\"", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("_content/Lib/lib.js", StringComparison.Ordinal) > blazor);
        Assert.Contains("x&quot; onload=&quot;alert(1)", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".styles.css", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_page_includes_the_scoped_css_bundle_when_the_app_has_one()
    {
        var (_, host) = CreateHost();
        var html = host.BuildHostPage(file => file.EndsWith(".styles.css", StringComparison.Ordinal));
        Assert.Contains($"{typeof(HybridHostTests).Assembly.GetName().Name}.styles.css", html, StringComparison.Ordinal);
    }

    [Fact]
    public void File_provider_serves_the_host_page_and_axe_and_delegates_everything_else()
    {
        File.WriteAllText(Path.Combine(_temp.FullName, "app.css"), "body{}");
        using var inner = new PhysicalFileProvider(_temp.FullName);
        var (_, host) = CreateHost();
        var provider = new WorkbenchHybridFileProvider(inner, host);

        var page = provider.GetFileInfo(WorkbenchHybridHost.HostPageName);
        Assert.True(page.Exists);
        using (var reader = new StreamReader(page.CreateReadStream()))
            Assert.Contains("blazor.webview.js", reader.ReadToEnd(), StringComparison.Ordinal);

        var axe = provider.GetFileInfo("/_workbench/axe.min.js");
        Assert.True(axe.Exists);
        Assert.True(axe.Length > 100_000);
        using (var reader = new StreamReader(axe.CreateReadStream()))
            Assert.Contains("axe", reader.ReadLine(), StringComparison.Ordinal);

        Assert.NotNull(provider.GetFileInfo("app.css").PhysicalPath);
        Assert.False(provider.GetFileInfo("missing.js").Exists);
    }

    [Fact]
    public void Automation_api_reports_starting_until_the_WebView_exists()
    {
        var (services, host) = CreateHost(o => o.DevToolsPort = 9555);
        var catalogs = services.GetRequiredService<WorkbenchCatalogAccessor>();

        var starting = HybridAutomationApi.Handle("GET", "/_workbench/api/health", catalogs, host);
        Assert.Equal(503, starting.Status);

        host.MarkWebViewReady();
        var health = HybridAutomationApi.Handle("GET", "/_workbench/api/health", catalogs, host);
        Assert.Equal(200, health.Status);
        using var json = JsonDocument.Parse(health.Body);
        Assert.Equal("Hybrid", json.RootElement.GetProperty("hostingModel").GetString());
        Assert.Equal("https://0.0.0.1/", json.RootElement.GetProperty("appOrigin").GetString());
        Assert.Equal("http://127.0.0.1:9555/", json.RootElement.GetProperty("devToolsEndpoint").GetString());
    }

    [Fact]
    public void Automation_api_serves_catalog_validation_and_scenarios()
    {
        var (services, host) = CreateHost();
        var catalogs = services.GetRequiredService<WorkbenchCatalogAccessor>();

        var catalog = JsonSerializer.Deserialize<CatalogDto>(HybridAutomationApi.Handle("GET", "/_workbench/api/catalog", catalogs, host).Body, WorkbenchJson.Options)!;
        Assert.Equal("Hybrid", catalog.HostingModel);
        var id = catalog.Components.SelectMany(c => c.Scenarios).First(s => s.Name == "Unsupported").Id;
        // .Device(...) provides every simulated Essentials service, so the catalog lists them as scenario services.
        Assert.Contains(catalog.Components.SelectMany(c => c.Scenarios).First(s => s.Id == id).Services, s => s.Contains("IShare", StringComparison.Ordinal));

        Assert.Equal(200, HybridAutomationApi.Handle("GET", $"/_workbench/api/scenarios/{id}", catalogs, host).Status);
        Assert.Equal(200, HybridAutomationApi.Handle("GET", "/_workbench/api/validate?x=1", catalogs, host).Status);
        Assert.Equal(404, HybridAutomationApi.Handle("GET", "/_workbench/api/scenarios/nope", catalogs, host).Status);
        Assert.Equal(404, HybridAutomationApi.Handle("GET", "/other", catalogs, host).Status);
        Assert.Equal(405, HybridAutomationApi.Handle("POST", "/_workbench/api/catalog", catalogs, host).Status);
    }

    [Fact]
    public async Task Automation_server_answers_loopback_clients_and_rejects_foreign_host_names()
    {
        await using var server = HybridAutomationServer.Start(new Uri("http://127.0.0.1:0"), (method, target) => new HybridAutomationResponse(200, $"{{\"method\":\"{method}\",\"target\":\"{target}\"}}"));
        using var http = new HttpClient { BaseAddress = server.Url };

        using var ok = await http.GetAsync("_workbench/api/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", ok.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"target\":\"/_workbench/api/health\"", await ok.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.False(ok.Headers.Contains("Access-Control-Allow-Origin"));

        using var rebinding = new HttpRequestMessage(HttpMethod.Get, "_workbench/api/health");
        rebinding.Headers.Host = "attacker.example";
        using var forbidden = await http.SendAsync(rebinding, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Theory]
    [InlineData("http://0.0.0.0:0")]
    [InlineData("http://192.168.1.10:0")]
    [InlineData("https://127.0.0.1:0")]
    public void Automation_server_listens_on_loopback_http_only(string url) =>
        Assert.Throws<ArgumentException>(() => HybridAutomationServer.Start(new Uri(url), (_, _) => new HybridAutomationResponse(200, "{}")));

    [Fact]
    public void Options_read_the_automation_url_from_the_command_line()
    {
        var options = new WorkbenchHybridOptions();
        options.ApplyEnvironment(["app.exe", "--urls", "http://127.0.0.1:5123;http://[::1]:5123"]);
        Assert.Equal(new Uri("http://127.0.0.1:5123"), options.AutomationUrl);
    }

    [Fact]
    public void Hybrid_sessions_apply_the_scenario_culture_process_wide_and_restore_it()
    {
        var original = CultureInfo.CurrentCulture;
        var (services, _) = CreateHost();
        Assert.True(ScenarioSessionContext.UseProcessWideSession);
        var catalog = services.GetRequiredService<WorkbenchCatalogAccessor>().Catalog;
        try
        {
            using (var scope = services.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<ScenarioSession>();
                session.Activate(catalog.Scenarios.First(s => s.Name == "French"), services: scope.ServiceProvider);
                Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
                Assert.Equal("fr-FR", CultureInfo.DefaultThreadCurrentCulture?.Name);
                Assert.Same(session, ScenarioSessionContext.Current);
            }

            // Disposing the page scope (a reload) releases the process-wide session.
            Assert.Null(ScenarioSessionContext.Current);

            using (var scope = services.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Name == "Default"), services: scope.ServiceProvider);
                Assert.Equal(original.Name, CultureInfo.CurrentCulture.Name);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.DefaultThreadCurrentCulture = null;
            CultureInfo.DefaultThreadCurrentUICulture = null;
        }
    }

    [Fact]
    public void Scenario_overrides_replace_platform_services_registered_by_the_app()
    {
        var (services, _) = CreateHost();
        var catalog = services.GetRequiredService<WorkbenchCatalogAccessor>().Catalog;
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ScenarioSession>().Activate(catalog.Scenarios.First(s => s.Name == "Unsupported"), services: scope.ServiceProvider);
        var share = scope.ServiceProvider.GetRequiredService<Microsoft.Maui.ApplicationModel.DataTransfer.IShare>();
        Assert.IsType<BlazorWorkbench.Maui.Essentials.FakeShare>(share);
    }
}
