using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Starts the .NET MAUI Blazor Hybrid demo (Windows only: the runner drives WebView2 over the DevTools Protocol).</summary>
public sealed class HybridFixture : IAsyncLifetime
{
    public WorkbenchHost? Host { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var project = Path.Combine(WorkbenchFixture.RepositoryRoot, "demo", "BlazorWorkbench.Demo.Maui", "BlazorWorkbench.Demo.Maui.csproj");
#if DEBUG
        Host = await WorkbenchHost.StartAsync(project, noBuild: true, configuration: "Debug");
#else
        Host = await WorkbenchHost.StartAsync(project, noBuild: true, configuration: "Release");
#endif
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
            await Host.DisposeAsync();
    }
}

public class HybridTests(HybridFixture hybrid) : IClassFixture<HybridFixture>, IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("wb-hybrid");

    public void Dispose() => _temp.Delete(true);

    private WorkbenchHost Host
    {
        get
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Blazor Hybrid automation needs WebView2 (Windows).");
            return hybrid.Host!;
        }
    }

    [Fact]
    public async Task Health_and_catalog_describe_the_hybrid_host()
    {
        var host = Host;
        Assert.True(host.Info.IsHybrid);
        Assert.Equal(new Uri("https://0.0.0.1/"), host.Info.AppOrigin);
        Assert.NotNull(host.Info.DevToolsEndpoint);
        Assert.True(host.Info.DevToolsEndpoint!.IsLoopback);

        var catalog = await host.GetCatalogAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hybrid", catalog.HostingModel);
        Assert.Equal("Demo Components (MAUI Blazor Hybrid)", catalog.Title);
        Assert.Contains(catalog.Components, c => c.Title == "Device/Share card");
        Assert.Equal(0, (await host.ValidateAsync(TestContext.Current.CancellationToken)).Errors);
    }

    [Fact]
    public async Task Scenarios_run_inside_the_app_WebView()
    {
        var report = await new WorkbenchTestRunner(Host, new TestRunOptions
        {
            Filter = "device-*,data-weather-forecast--*,account-*,forms-*,inputs-counter--*,display-date-label--*,overlays-dialog--*",
            OutputDirectory = Path.Combine(_temp.FullName, "out"),
            Accessibility = true,
            Viewports = ["laptop"],
            Themes = ["light"],
        }).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(BaselineStore.CurrentPlatform + "-hybrid", report.Environment.Platform);
        Assert.Equal("webview", report.Environment.Browser);
        Assert.Equal(1, report.Settings.Workers);
        Assert.True(report.Summary.Total >= 30);
        Assert.Equal(0, report.Summary.Errors);
        Assert.True(report.Summary.Failed == 0, string.Join("\n", report.Scenarios.Where(s => s.Status != CheckStatus.Passed).Select(s => $"{s.ScenarioId}: {s.Message} {string.Join("; ", s.Checks.Where(c => c.Status != CheckStatus.Passed).Select(c => c.Message))}")));
        Assert.Contains(report.Scenarios.SelectMany(s => s.Checks), c => c.Kind == CheckKind.Interaction && c.Status == CheckStatus.Passed);
    }

    [Fact]
    public async Task Scenario_list_in_the_WebView_opens_previews_in_a_fresh_document()
    {
        var host = Host;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(host.Info.DevToolsEndpoint!.ToString());
        var page = browser.Contexts.SelectMany(c => c.Pages).First(p => p.Url.StartsWith("https://0.0.0.1/", StringComparison.Ordinal));

        await page.GotoAsync(new Uri(host.Info.AppOrigin!, "_workbench/scenarios").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Device/Share card" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Sharing unavailable" }).ClickAsync();

        await page.WaitForURLAsync("**/_workbench/preview/device-share-card--sharing-unavailable");
        await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-workbench-state') === 'ready'");
        await page.GetByRole(AriaRole.Button, new() { Name = "Share" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Sharing is not available on this device.");

        // The simulated platform service reported the request to the preview diagnostics.
        var diagnostics = await BlazorWorkbench.Testing.LoadState.DiagnosticsAsync(page);
        Assert.Contains(diagnostics, d => d.Source == Diagnostics.DiagnosticSource.Platform && d.Message.Contains("not supported", StringComparison.Ordinal));
    }
}
