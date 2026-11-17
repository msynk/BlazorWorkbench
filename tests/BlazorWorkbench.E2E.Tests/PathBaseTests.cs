using BlazorWorkbench.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Starts a second host, so it runs alone rather than competing with the parallel tests for the machine.</summary>
[CollectionDefinition(nameof(SeparateHost), DisableParallelization = true)]
public sealed class SeparateHost;

/// <summary>A Workbench behind a reverse proxy under a path (<c>app.UsePathBase("/wb")</c>).</summary>
[Collection(nameof(SeparateHost))]
public class PathBaseTests(WorkbenchFixture fixture)
{
    private static readonly Lock s_environment = new();

    [Fact]
    public async Task The_shell_previews_and_axe_work_under_a_path_base()
    {
        var project = Path.Combine(WorkbenchFixture.RepositoryRoot, "demo", "BlazorWorkbench.Demo", "BlazorWorkbench.Demo.csproj");
        WorkbenchHost host;
        // The host process inherits this process's environment; the variable is removed again right after the start.
        lock (s_environment)
            Environment.SetEnvironmentVariable("Workbench__PathBase", "/wb");
        try
        {
            host = await WorkbenchHost.StartAsync(project, noBuild: true, configuration: Configuration, cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            lock (s_environment)
                Environment.SetEnvironmentVariable("Workbench__PathBase", null);
        }

        await using (host)
        {
            var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });
            var page = await context.NewPageAsync();
            var log = new List<string>();
            page.Console += (_, m) => log.Add($"console {m.Type}: {m.Text}");
            page.RequestFailed += (_, r) => log.Add($"failed: {r.Url}");
            page.Response += (_, r) => { if (r.Status >= 400) log.Add($"{r.Status}: {r.Url}"); };
            await page.GotoAsync(new Uri(host.BaseAddress, "/wb/scenario/inputs-button--primary").ToString());
            Assert.Equal("/wb/", await page.Locator("base").GetAttributeAsync("href"));
            try
            {
                await Expect(page.Locator(".wb-state-ready")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            }
            catch (PlaywrightException ex)
            {
                throw new InvalidOperationException($"{ex.Message}\n{string.Join('\n', log.Take(30))}", ex);
            }
            Assert.StartsWith("/wb/_workbench/preview/", await page.EvaluateAsync<string>("() => document.getElementById('wb-preview-frame').contentWindow.location.pathname"), StringComparison.Ordinal);

            // axe-core loads relative to the path base.
            await page.GetByRole(AriaRole.Tab, new() { Name = "Accessibility" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Run scan" }).ClickAsync();
            await Expect(page.Locator(".wb-a11y")).Not.ToContainTextAsync("Scanning", new() { Timeout = 30_000 });
            await Expect(page.Locator(".wb-a11y .wb-error")).ToHaveCountAsync(0);
            await context.CloseAsync();
        }
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
