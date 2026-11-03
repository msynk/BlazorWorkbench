using BlazorWorkbench.Testing;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(BlazorWorkbench.E2E.Tests.WorkbenchFixture))]

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Starts the sample Workbench host and one browser for all end-to-end tests.</summary>
public sealed class WorkbenchFixture : IAsyncLifetime
{
    public static string RepositoryRoot { get; } = FindRoot();

    public WorkbenchHost Host { get; private set; } = default!;
    public IPlaywright Playwright { get; private set; } = default!;
    public IBrowser Browser { get; private set; } = default!;

    public Uri Url(string relative) => new(Host.BaseAddress, relative);

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(RepositoryRoot, "samples", "SampleWorkbench", "SampleWorkbench.csproj");
        Host = await WorkbenchHost.StartAsync(project, noBuild: true, configuration: Configuration);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        // Generous timeouts: the suite runs many browser contexts in parallel against one host.
        Assertions.SetDefaultExpectTimeout(15_000);
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task<IPage> NewPageAsync(int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Host.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            TimezoneId = "UTC",
        });
        context.SetDefaultTimeout(15_000);
        return await context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
        if (Host is not null) await Host.DisposeAsync();
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BlazorWorkbench.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
