using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Drives the Workbench UI itself in a real browser.</summary>
public class ShellTests(WorkbenchFixture fixture)
{
    private static readonly string Screens = Path.Combine(WorkbenchFixture.RepositoryRoot, "artifacts", "e2e");

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(path);
        await Expect(page.Locator("html[data-workbench-shell=ready], html[data-workbench-state]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        return page;
    }

    private static async Task WaitReadyAsync(IPage page) =>
        await Expect(page.Locator(".wb-state-ready")).ToBeVisibleAsync(new() { Timeout = 20_000 });

    private static IFrameLocator Preview(IPage page) => page.FrameLocator("#wb-preview-frame");

    [Fact]
    public async Task Overview_lists_catalog_and_explorer()
    {
        var page = await OpenAsync("");
        await Expect(page.Locator(".wb-overview h1")).ToHaveTextAsync("Demo Components");
        var catalog = await fixture.Host.GetCatalogAsync();
        await Expect(page.Locator(".wb-explorer-footer")).ToContainTextAsync($"{catalog.Components.Count} components · {catalog.Scenarios.Count()} scenarios");
        await Expect(page.Locator(".wb-node-component", new() { HasText = "Button" })).ToBeVisibleAsync();
        Directory.CreateDirectory(Screens);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screens, "overview.png") });
    }

    [Fact]
    public async Task Search_opens_the_first_result_with_keyboard()
    {
        var page = await OpenAsync("");
        await page.Keyboard.PressAsync("/");
        await Expect(page.Locator("#wb-search")).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("weather offline");
        await Expect(page.Locator(".wb-results a")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Enter");
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/scenario/data-weather-forecast--offline"));
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Alert)).ToContainTextAsync("Simulated offline mode");
    }

    [Fact]
    public async Task Controls_update_the_preview_live_and_the_url()
    {
        var page = await OpenAsync("scenario/inputs-button--primary");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Button)).ToHaveTextAsync("Save changes");
        Directory.CreateDirectory(Screens);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screens, "scenario.png") });

        // Editing does not reload the preview: the marker survives.
        await Preview(page).Locator("body").EvaluateAsync("b => b.dataset.marker = 'kept'");
        await page.Locator("#wb-param-ChildContent").FillAsync("Publish now");
        await page.Locator("#wb-param-ChildContent").BlurAsync();
        await Expect(Preview(page).GetByRole(AriaRole.Button)).ToHaveTextAsync("Publish now");
        await page.Locator("#wb-param-Variant").SelectOptionAsync("Danger");
        await Expect(Preview(page).GetByRole(AriaRole.Button)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("btn-danger"));
        Assert.Equal("kept", await Preview(page).Locator("body").GetAttributeAsync("data-marker"));
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("args="));

        // The URL is a deep link that reproduces the state.
        var link = page.Url;
        var other = await fixture.NewPageAsync();
        await other.GotoAsync(link);
        await WaitReadyAsync(other);
        await Expect(Preview(other).GetByRole(AriaRole.Button)).ToHaveTextAsync("Publish now");

        // Reset restores the declared scenario.
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true }).First.ClickAsync();
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Button)).ToHaveTextAsync("Save changes");
    }

    [Fact]
    public async Task Invalid_input_shows_a_validation_error()
    {
        var page = await OpenAsync("scenario/inputs-counter--default");
        await WaitReadyAsync(page);
        await page.Locator("#wb-param-Step").FillAsync("two");
        await page.Locator("#wb-param-Step").BlurAsync();
        await Expect(page.Locator(".wb-params [role=alert]")).ToContainTextAsync("is not a valid int");
    }

    [Fact]
    public async Task Actions_panel_logs_callbacks_from_the_preview()
    {
        var page = await OpenAsync("scenario/inputs-counter--default");
        await WaitReadyAsync(page);
        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Increase" }).ClickAsync();
        // The tab name gains the call count ("Actions 1") once the callback arrives; "Interactions" must not match.
        await page.GetByRole(AriaRole.Tab, new() { NameRegex = new Regex(@"^Actions\b") }).ClickAsync();
        var entry = page.Locator(".wb-log li").Last; // oldest entry (the log lists newest first)
        await Expect(entry).ToContainTextAsync("OnChanged");
        await Expect(entry).ToContainTextAsync("#1");
    }

    [Fact]
    public async Task Gates_can_be_released_from_the_environment_panel()
    {
        var page = await OpenAsync("scenario/data-weather-forecast--loading");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByText("Loading forecast")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Environment" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Release" }).ClickAsync();
        await Expect(Preview(page).GetByTestId("temperature")).ToContainTextAsync("31.5");
        await Expect(page.Locator(".wb-env")).ToContainTextAsync("Released");
    }

    [Fact]
    public async Task Diagnostics_capture_exceptions_with_scenario_context()
    {
        var page = await OpenAsync("scenario/feedback-flaky-widget--healthy");
        await WaitReadyAsync(page);
        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Throw from handler" }).ClickAsync();
        await Expect(Preview(page).Locator("[data-workbench-error]")).ToContainTextAsync("simulated defect");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Diagnostics" }).ClickAsync();
        await Expect(page.Locator(".wb-diag-table")).ToContainTextAsync("InvalidOperationException");
        await Expect(page.Locator(".wb-diag-table")).ToContainTextAsync("Render");
    }

    [Fact]
    public async Task Render_failures_are_reported_as_error_state()
    {
        var page = await OpenAsync("scenario/feedback-flaky-widget--throws-while-rendering");
        await Expect(page.Locator(".wb-state-error")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Accessibility_scan_runs_in_the_ui()
    {
        var page = await OpenAsync("scenario/inputs-button--primary?panel=accessibility");
        await WaitReadyAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Run scan" }).ClickAsync();
        await Expect(page.Locator(".wb-a11y [role=status]")).ToContainTextAsync("0 violation(s)", new() { Timeout = 20_000 });
        await Expect(page.Locator(".wb-a11y")).ToContainTextAsync("axe-core");
    }

    [Fact]
    public async Task Interaction_tests_run_in_the_ui()
    {
        var page = await OpenAsync("scenario/inputs-counter--default?panel=interactions");
        await WaitReadyAsync(page);
        await page.Locator(".wb-interaction").First.GetByRole(AriaRole.Button, new() { Name = "Run" }).ClickAsync();
        await Expect(page.Locator(".wb-interaction").First).ToContainTextAsync("passed", new() { Timeout = 20_000 });
        Directory.CreateDirectory(Screens);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screens, "interactions.png") });
    }

    [Fact]
    public async Task Viewport_presets_resize_the_preview()
    {
        var page = await OpenAsync("scenario/layout-toolbar--left-to-right");
        await WaitReadyAsync(page);
        await page.Locator("#wb-viewport").SelectOptionAsync("mobile");
        var box = await page.Locator("#wb-preview-frame").BoundingBoxAsync();
        Assert.Equal(375, box!.Width, 1);
    }

    [Fact]
    public async Task Culture_and_direction_apply_to_the_preview()
    {
        var page = await OpenAsync("scenario/display-date-label--german");
        await WaitReadyAsync(page);
        await Expect(Preview(page).Locator("time")).ToContainTextAsync("März 2026");
        await Expect(Preview(page).Locator("time")).ToContainTextAsync("3 h ago");

        var rtl = await OpenAsync("scenario/layout-toolbar--right-to-left");
        await WaitReadyAsync(rtl);
        Assert.Equal("rtl", await Preview(rtl).Locator("html").GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task Theme_switch_applies_without_reload()
    {
        var page = await OpenAsync("scenario/display-card--text-content");
        await WaitReadyAsync(page);
        await page.Locator("#wb-theme").SelectOptionAsync("dark");
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
    }

    [Fact]
    public async Task Preview_follows_the_workbench_theme_until_a_theme_is_chosen()
    {
        // A dark operating system theme makes the Workbench dark, and the preview follows it.
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.Host.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            ColorScheme = Microsoft.Playwright.ColorScheme.Dark,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("scenario/display-card--text-content");
        await WaitReadyAsync(page);
        await Expect(page.Locator(".wb-shell")).ToHaveAttributeAsync("data-shell-theme", "dark");
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        // Toggling the Workbench theme switches the preview live.
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle Workbench theme" }).ClickAsync();
        await Expect(page.Locator(".wb-shell")).ToHaveAttributeAsync("data-shell-theme", "light");
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        // An explicit choice sticks, and the deep link records it.
        await page.Locator("#wb-theme").SelectOptionAsync("dark");
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle Workbench theme" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Toggle Workbench theme" }).ClickAsync();
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await Expect(page).ToHaveURLAsync(new Regex("theme=dark"));
        await context.CloseAsync();
    }

    [Fact]
    public async Task Shell_fills_the_window_without_gutters_or_page_scrolling()
    {
        var page = await OpenAsync("");
        var box = await page.Locator(".wb-shell").BoundingBoxAsync();
        Assert.Equal(0, box!.X);
        Assert.Equal(0, box.Y);
        Assert.Equal(1440, box.Width, 1);
        Assert.Equal(900, box.Height, 1);
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollHeight > innerHeight || document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task Code_panel_exports_razor_and_scenario_code()
    {
        var page = await OpenAsync("scenario/inputs-button--danger?panel=code");
        await WaitReadyAsync(page);
        await Expect(page.Locator(".wb-code pre").First).ToHaveTextAsync("<Button Icon=\"🗑\" Variant=\"ButtonVariant.Danger\">Delete</Button>");
        await Expect(page.Locator(".wb-code")).ToContainTextAsync("set.Add(\"Danger (edited)\"");
    }

    [Fact]
    public async Task Documentation_page_shows_parameters_and_examples()
    {
        var page = await OpenAsync("docs/inputs-button");
        await Expect(page.Locator(".wb-docs h1")).ToHaveTextAsync("Button");
        await Expect(page.Locator("h2#parameters + table")).ToContainTextAsync("Visual emphasis of the button.");
        await Expect(page.Locator(".wb-markdown table")).ToContainTextAsync("Destructive actions");
        await Expect(page.Locator(".wb-doc-scenario")).ToHaveCountAsync(6);
        Directory.CreateDirectory(Screens);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screens, "docs.png"), FullPage = false });
    }

    [Fact]
    public async Task Unknown_scenarios_are_explained()
    {
        var page = await OpenAsync("_workbench/preview/inputs-button--nope");
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-workbench-state", "error");
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("was not found");
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("inputs-button--primary");
    }

    [Fact]
    public async Task Automation_surface_exposes_state_and_diagnostics()
    {
        var page = await OpenAsync("_workbench/preview/data-weather-forecast--loaded");
        await page.WaitForFunctionAsync("() => window.__workbench && window.__workbench.ready");
        var metrics = await page.EvaluateAsync<JsonElement>("() => window.__workbench.metrics()");
        Assert.True(metrics.GetProperty("readyMs").GetDouble() > 0);
        var diagnostics = await page.EvaluateAsync<JsonElement>("() => window.__workbench.diagnostics");
        Assert.Contains(diagnostics.EnumerateArray(), d => d.GetProperty("source").GetString() == "Http" && d.GetProperty("message").GetString()!.Contains("200", StringComparison.Ordinal));
    }
}

public class HostSecurityTests(WorkbenchFixture fixture)
{
    [Fact]
    public async Task Responses_carry_defensive_headers()
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(fixture.Url("_workbench/api/health"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("_workbench/visual/baselines/..%2F..%2Fappsettings.json")]
    [InlineData("_workbench/visual/baselines/../../Program.cs")]
    [InlineData("_workbench/visual/secrets/x.png")]
    public async Task Visual_file_endpoint_rejects_traversal(string path)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(fixture.Url(path));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_never_exposes_absolute_paths()
    {
        var catalog = await fixture.Host.GetCatalogAsync();
        Assert.All(catalog.Components.Where(c => c.SourcePath is not null), c => Assert.False(Path.IsPathRooted(c.SourcePath!), c.SourcePath));
        Assert.Contains(catalog.Components, c => c.SourcePath == "demo/BlazorWorkbench.Demo.Components/Inputs/Button.razor");
    }
}
