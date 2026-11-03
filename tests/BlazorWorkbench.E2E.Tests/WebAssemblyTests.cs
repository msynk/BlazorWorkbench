using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Starts the Interactive WebAssembly sample host (same scenarios as the Server sample).</summary>
public sealed class WebAssemblyFixture : IAsyncLifetime
{
    public WorkbenchHost Host { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(WorkbenchFixture.RepositoryRoot, "samples", "SampleWorkbench.Wasm", "SampleWorkbench.Wasm.csproj");
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

public class WebAssemblyTests(WorkbenchFixture browser, WebAssemblyFixture wasm) : IClassFixture<WebAssemblyFixture>, IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("wb-wasm");

    public void Dispose() => _temp.Delete(true);

    [Fact]
    public async Task Catalog_reports_the_hosting_model()
    {
        var catalog = await wasm.Host.GetCatalogAsync();
        Assert.Equal("WebAssembly", catalog.HostingModel);
        Assert.Equal("Sample Components (WebAssembly)", catalog.Title);
        Assert.True(catalog.Scenarios.Count() >= 60);
    }

    [Fact]
    public async Task Scenarios_with_environment_simulation_pass_in_the_browser_runtime()
    {
        var report = await new WorkbenchTestRunner(wasm.Host, new TestRunOptions
        {
            Filter = "data-weather-forecast--*,account-*,display-date-label--*,data-product-list--*,forms-*,layout-sortable-list--*,inputs-counter--*",
            OutputDirectory = Path.Combine(_temp.FullName, "out"),
            Workers = 3,
            Viewports = ["laptop"],
            Themes = ["light"],
        }).RunAsync();

        Assert.Equal("windows-webassembly".Replace("windows", BaselineStore.CurrentPlatform, StringComparison.Ordinal), report.Environment.Platform);
        Assert.Equal(0, report.Summary.Errors);
        Assert.True(report.Summary.Failed == 0, string.Join("\n", report.Scenarios.Where(s => s.Status != CheckStatus.Passed).Select(s => $"{s.ScenarioId}: {s.Message}")));
    }

    [Fact]
    public async Task Shell_runs_in_webassembly_with_live_controls_and_documentation()
    {
        var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = wasm.Host.BaseAddress.ToString(), Locale = "en-US" });
        var page = await context.NewPageAsync();
        await page.GotoAsync("scenario/inputs-button--primary");

        await Expect(page.Locator(".wb-state-ready")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var preview = page.FrameLocator("#wb-preview-frame");
        await Expect(preview.GetByRole(AriaRole.Button)).ToHaveTextAsync("Save changes");

        // XML documentation is fetched from the host because WebAssembly cannot read it from disk.
        await Expect(page.Locator(".wb-params")).ToContainTextAsync("Visual emphasis of the button.");

        await page.Locator("#wb-param-ChildContent").FillAsync("From WebAssembly");
        await page.Locator("#wb-param-ChildContent").BlurAsync();
        await Expect(preview.GetByRole(AriaRole.Button)).ToHaveTextAsync("From WebAssembly");
        await context.CloseAsync();
    }
}
