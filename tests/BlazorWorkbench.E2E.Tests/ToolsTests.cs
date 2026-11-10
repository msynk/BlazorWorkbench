using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Canvas tools, inspection panels, multi-preview modes and documentation pages, in a real browser.</summary>
public class ToolsTests(WorkbenchFixture fixture)
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

    private static async Task ScreenshotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(Screens);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screens, name + ".png") });
    }

    [Fact]
    public async Task Visual_aids_from_the_url_and_the_toolbar_apply_without_reloading()
    {
        var page = await OpenAsync("scenario/inputs-button--primary?grid=1&outline=1");
        await WaitReadyAsync(page);
        await Expect(Preview(page).Locator("[data-wb-part=grid]")).ToHaveCountAsync(1);
        await Expect(Preview(page).Locator("style[data-wb-tool=outline]")).ToHaveCountAsync(1);

        await Preview(page).Locator("body").EvaluateAsync("b => b.dataset.marker = 'kept'");
        await page.Locator("#wb-tool-measure").ClickAsync();
        await Preview(page).GetByRole(AriaRole.Button).HoverAsync();
        await Expect(Preview(page).Locator("[data-wb-part=measure] span")).ToContainTextAsync("button");
        await page.Locator("#wb-tool-grid").ClickAsync();
        await Expect(Preview(page).Locator("[data-wb-part=grid]")).ToHaveCountAsync(0);
        Assert.Equal("kept", await Preview(page).Locator("body").GetAttributeAsync("data-marker"));
        await Expect(page).ToHaveURLAsync(new Regex("measure=1"));
        await ScreenshotAsync(page, "tools-measure");

        // Tool overlays never count as scenario content.
        Assert.Equal(1, await Preview(page).GetByRole(AriaRole.Button).CountAsync());
    }

    [Fact]
    public async Task Zoom_and_rotation_resize_the_frame()
    {
        var page = await OpenAsync("scenario/layout-toolbar--left-to-right?viewport=mobile&zoom=0.5&rotate=1");
        await WaitReadyAsync(page);
        var box = await page.Locator(".wb-zoom").BoundingBoxAsync();
        Assert.Equal(667 * 0.5, box!.Width, 1);
        Assert.Equal(375 * 0.5, box.Height, 1);
        Assert.Equal(667, await Preview(page).Locator("html").EvaluateAsync<double>("h => window.innerWidth"), 1);
    }

    [Fact]
    public async Task Forced_pseudo_states_restyle_without_input()
    {
        var page = await OpenAsync("scenario/inputs-button--keyboard-focus");
        await WaitReadyAsync(page);
        var button = Preview(page).GetByRole(AriaRole.Button);
        await Expect(button).ToHaveAttributeAsync("data-wb-pseudo", "focus-visible");
        Assert.Equal("solid", await button.EvaluateAsync<string>("b => getComputedStyle(b).outlineStyle"));

        var hover = await OpenAsync("scenario/inputs-button--primary");
        await WaitReadyAsync(hover);
        var before = await Preview(hover).GetByRole(AriaRole.Button).EvaluateAsync<string>("b => getComputedStyle(b).backgroundColor");
        await hover.Locator("#wb-tool-pseudo summary").ClickAsync();
        await hover.Locator("#wb-tool-pseudo").GetByLabel(":hover").CheckAsync();
        await Expect(Preview(hover).Locator("html")).ToHaveAttributeAsync("data-wb-pseudo-all", "hover");
        var after = await Preview(hover).GetByRole(AriaRole.Button).EvaluateAsync<string>("b => getComputedStyle(b).backgroundColor");
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Accessibility_simulations_filter_scale_and_number_the_tab_order()
    {
        var page = await OpenAsync("scenario/forms-signup-form--empty?vision=deuteranopia&text=200&tabs=1");
        await WaitReadyAsync(page);
        Assert.Contains("wb-vision-deuteranopia", await page.Locator("#wb-preview-frame").GetAttributeAsync("style") ?? "");
        Assert.Equal(1, await page.Locator("filter#wb-vision-deuteranopia").CountAsync());
        Assert.True(await Preview(page).Locator("html").EvaluateAsync<double>("h => parseFloat(getComputedStyle(h).fontSize)") >= 32);
        await Expect(Preview(page).Locator("[data-wb-part=tab-order] span").First).ToHaveTextAsync("1");
        await ScreenshotAsync(page, "tools-accessibility");
    }

    [Fact]
    public async Task Html_panel_shows_clean_rendered_markup()
    {
        var page = await OpenAsync("scenario/inputs-button--danger?panel=html");
        await WaitReadyAsync(page);
        var code = page.Locator(".wb-html-code");
        await Expect(code).ToContainTextAsync("<button");
        await Expect(code).ToContainTextAsync("btn-danger");
        Assert.DoesNotContain("<!--", await code.TextContentAsync() ?? "");
    }

    [Fact]
    public async Task Storage_is_isolated_per_scenario_and_seeded_by_it()
    {
        var page = await OpenAsync("scenario/feedback-whats-new-banner--already-dismissed?panel=storage");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Region, new() { Name = "What's new" })).ToHaveCountAsync(0);
        await Expect(page.Locator(".wb-storage")).ToContainTextAsync("trailhead:whats-new-dismissed");

        // Same browser, same origin: the value written above must not leak into the next scenario.
        await page.GotoAsync("scenario/feedback-whats-new-banner--first-visit");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Region, new() { Name = "What's new" })).ToBeVisibleAsync();
        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        Assert.Equal("2026.3", await Preview(page).Locator("html").EvaluateAsync<string>("() => localStorage.getItem('trailhead:whats-new-dismissed')"));
    }

    [Fact]
    public async Task Users_and_network_profiles_switch_from_the_toolbar()
    {
        var page = await OpenAsync("scenario/account-workspace-settings--viewer");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Button, new() { Name = "Invite people" })).ToHaveCountAsync(0);
        await page.Locator("#wb-user").SelectOptionAsync("admin");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Button, new() { Name = "Invite people" })).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex("user=admin"));

        var offline = await OpenAsync("scenario/data-weather-forecast--loaded?network=offline");
        await WaitReadyAsync(offline);
        await Expect(Preview(offline).GetByRole(AriaRole.Alert)).ToContainTextAsync("Simulated offline mode");
    }

    [Fact]
    public async Task Compare_and_gallery_show_many_previews()
    {
        var page = await OpenAsync("scenario/inputs-button--primary?mode=compare");
        var options = await fixture.Host.GetCatalogAsync();
        await Expect(page.Locator(".wb-compare-cell")).ToHaveCountAsync(options.Themes.Count);
        await page.Locator("#wb-compare-axis").SelectOptionAsync("state");
        await Expect(page.Locator(".wb-compare-cell figcaption")).ToContainTextAsync([":hover"]);
        await ScreenshotAsync(page, "compare");

        var gallery = await OpenAsync("scenario/inputs-button--variants-primary-small?mode=gallery");
        await Expect(gallery.Locator("table.wb-matrix tbody tr")).ToHaveCountAsync(4);
        await Expect(gallery.Locator("table.wb-matrix thead th")).ToHaveCountAsync(4);
        await ScreenshotAsync(gallery, "gallery-matrix");
    }

    [Fact]
    public async Task Design_overlay_draws_the_mockup_over_the_component()
    {
        var page = await OpenAsync("scenario/inputs-button--primary?panel=design");
        await WaitReadyAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Overlay on preview" }).ClickAsync();
        var overlay = Preview(page).Locator("[data-wb-part=overlay] img");
        await Expect(overlay).ToHaveAttributeAsync("src", new Regex("button-primary.svg"));
        await page.GetByLabel("Difference").CheckAsync();
        await Expect(overlay).ToHaveAttributeAsync("style", new Regex("difference"));
        await ScreenshotAsync(page, "design-overlay");
    }

    [Fact]
    public async Task Recorder_turns_clicks_and_typing_into_csharp()
    {
        var page = await OpenAsync("scenario/inputs-counter--default?panel=interactions");
        await WaitReadyAsync(page);
        await page.Locator("#wb-record").ClickAsync();
        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Increase" }).ClickAsync();
        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Increase" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add assertion" }).ClickAsync();
        await Preview(page).GetByTestId("count").ClickAsync();
        var code = page.Locator(".wb-recorder pre");
        await Expect(code).ToContainTextAsync(".Click(By.Role(\"button\", \"Increase\"))");
        await Expect(code).ToContainTextAsync(".ExpectText(By.TestId(\"count\"), \"2\")");
    }

    [Fact]
    public async Task Debugger_pauses_before_each_step()
    {
        var page = await OpenAsync("scenario/inputs-counter--bounded?panel=interactions");
        await WaitReadyAsync(page);
        var test = page.Locator(".wb-interaction").First;
        await test.GetByRole(AriaRole.Button, new() { Name = "Debug" }).ClickAsync();
        await Expect(test.Locator(".wb-step-current")).ToContainTextAsync("Click");
        await Expect(Preview(page).Locator("[data-wb-part=highlight] div")).ToHaveCountAsync(1);
        await test.GetByRole(AriaRole.Button, new() { Name = "Step", Exact = true }).ClickAsync();
        await Expect(test.Locator(".wb-step-current")).ToContainTextAsync("ExpectText");
        await test.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Expect(test).ToContainTextAsync("passed", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Pages_embed_live_scenarios_and_tokens_are_read_from_the_stylesheets()
    {
        var page = await OpenAsync("pages/guidelines-buttons-and-actions");
        await Expect(page.Locator(".wb-page h1")).ToHaveTextAsync("Buttons and actions");
        await Expect(page.Locator(".wb-embed iframe").First).ToHaveAttributeAsync("src", new Regex("inputs-button--primary"));
        await Expect(page.Locator(".wb-coverage").First).ToBeVisibleAsync();

        var tokens = await OpenAsync("tokens");
        var frame = tokens.FrameLocator(".wb-tokens-frame");
        await Expect(frame.Locator(".wbt-token").Filter(new() { HasTextRegex = new Regex("--c-primary(?!-)") })).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(frame.Locator(".wbt-group h2").First).ToContainTextAsync("Colors");
        await ScreenshotAsync(tokens, "tokens");
    }

    [Fact]
    public async Task Docs_show_status_relationships_and_coverage()
    {
        var page = await OpenAsync("docs/analytics-sales-dashboard");
        await Expect(page.Locator(".wb-docs .wb-status")).ToHaveTextAsync("beta");
        await Expect(page.Locator("#relationships + dl")).ToContainTextAsync("KPI card");

        var badge = await OpenAsync("docs/display-badge");
        await Expect(badge.Locator(".wb-coverage").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Explorer_filters_by_status_and_shortcuts_navigate()
    {
        var page = await OpenAsync("scenario/inputs-button--primary");
        await WaitReadyAsync(page);
        await page.Locator("#wb-search").FillAsync("status:beta");
        await Expect(page.Locator(".wb-results a").First).ToContainTextAsync("Beta", new() { IgnoreCase = true });
        await page.Locator("#wb-search").FillAsync("");

        await page.Locator(".wb-brand").FocusAsync();
        await page.Keyboard.PressAsync("Alt+ArrowRight");
        await Expect(page).ToHaveURLAsync(new Regex("inputs-button--secondary"));
        await page.Keyboard.PressAsync("Shift+Slash");
        await Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts" })).ToBeVisibleAsync();
    }
}
