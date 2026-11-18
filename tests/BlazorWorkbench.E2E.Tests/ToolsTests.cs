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

    [Fact]
    public async Task Gallery_previews_keep_their_own_storage_seeds()
    {
        // Every scenario of the banner loads at once; each must see its own seed, not another frame's (or a cleared one).
        var page = await OpenAsync("scenario/feedback-whats-new-banner--first-visit?mode=gallery");
        var first = page.FrameLocator("iframe[title='Preview of First visit']");
        var dismissed = page.FrameLocator("iframe[title='Preview of Already dismissed']");
        var older = page.FrameLocator("iframe[title='Preview of Dismissed an older release']");
        await Expect(first.Locator(".whats-new-host[data-checked]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(dismissed.Locator(".whats-new-host[data-checked]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(older.Locator(".whats-new-host[data-checked]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(first.GetByRole(AriaRole.Region, new() { Name = "What's new" })).ToBeVisibleAsync();
        await Expect(dismissed.GetByRole(AriaRole.Region, new() { Name = "What's new" })).ToHaveCountAsync(0);
        await Expect(older.GetByText("New in 2026.3")).ToBeVisibleAsync();
        Assert.Equal("2026.3", await dismissed.Locator("html").EvaluateAsync<string>("() => localStorage.getItem('trailhead:whats-new-dismissed')"));
        Assert.Null(await first.Locator("html").EvaluateAsync<string?>("() => localStorage.getItem('trailhead:whats-new-dismissed')"));
    }

    [Fact]
    public async Task Panel_tabs_follow_the_aria_tabs_pattern_and_single_key_shortcuts_can_be_turned_off()
    {
        var page = await OpenAsync("scenario/inputs-button--primary");
        await WaitReadyAsync(page);
        var controls = page.GetByRole(AriaRole.Tab, new() { Name = "Controls" });
        await Expect(controls).ToHaveAttributeAsync("tabindex", "0");
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Environment" })).ToHaveAttributeAsync("tabindex", "-1");
        await controls.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Environment" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page).ToHaveURLAsync(new Regex("panel=environment"));
        await page.Keyboard.PressAsync("Home");
        await Expect(controls).ToHaveAttributeAsync("aria-selected", "true");

        // The shortcuts dialog takes focus, keeps it, and turns single-key shortcuts off.
        await page.Locator(".wb-brand").FocusAsync();
        await page.Keyboard.PressAsync("Shift+Slash");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts" });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(page.Locator("#wb-shortcuts-close")).ToBeFocusedAsync();
        await dialog.GetByRole(AriaRole.Checkbox, new() { Name = "Single-key shortcuts" }).UncheckAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await page.Locator(".wb-brand").FocusAsync();
        await page.Keyboard.PressAsync("g");
        await Expect(page).Not.ToHaveURLAsync(new Regex("grid=1"));
        Assert.Equal("off", await page.EvaluateAsync<string>("() => localStorage.getItem('blazor-workbench:single-key-shortcuts')"));
    }

    [Fact]
    public async Task Fixing_a_failing_scenario_in_controls_leaves_the_error_state()
    {
        var page = await OpenAsync("scenario/feedback-flaky-widget--throws-while-rendering");
        await Expect(page.Locator(".wb-state-error")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Turning the failure off recovers the error boundary; the preview checks readiness again instead of staying "Error".
        await page.Locator("#wb-param-ThrowOnRender").UncheckAsync();
        await WaitReadyAsync(page);
        await Expect(Preview(page).Locator("[data-workbench-error]")).ToHaveCountAsync(0);

        // An accessibility scan started now finishes (it used to wait for a readiness that never came).
        await page.GetByRole(AriaRole.Tab, new() { Name = "Accessibility" }).ClickAsync();
        await page.Locator(".wb-a11y").GetByRole(AriaRole.Button).First.ClickAsync();
        await Expect(page.Locator(".wb-a11y")).Not.ToContainTextAsync("Scanning", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task The_accessibility_outline_lists_headings_and_landmarks()
    {
        var page = await OpenAsync("scenario/layout-app-header--desktop?panel=accessibility");
        await WaitReadyAsync(page);
        var outline = page.Locator(".wb-outline");
        await Expect(outline.Locator(".wb-outline-list .wb-badge", new() { HasText = "banner" })).ToHaveCountAsync(1);
        await Expect(outline.GetByRole(AriaRole.Button, new() { Name = "navigation Main" })).ToBeVisibleAsync();

        // Clicking an entry outlines the element in the preview.
        await outline.GetByRole(AriaRole.Button, new() { Name = "navigation Main" }).ClickAsync();
        await Expect(Preview(page).Locator("[data-wb-part=highlight] div").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Toolbar_globals_reach_the_preview_and_the_url()
    {
        var page = await OpenAsync("scenario/inputs-button--primary?g.corners=square");
        await WaitReadyAsync(page);
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-corners", "square");
        await Expect(page.Locator("#wb-global-corners")).ToHaveValueAsync("square");

        await page.Locator("#wb-global-corners").SelectOptionAsync("rounded");
        await WaitReadyAsync(page);
        await Expect(Preview(page).Locator("html")).ToHaveAttributeAsync("data-corners", "rounded");
        await Expect(page).ToHaveURLAsync(new Regex(@"g\.corners=rounded"));
    }

    [Fact]
    public async Task A_panel_of_your_own_reads_from_the_live_preview()
    {
        // The demo's Styles panel (options.AddPanel) evaluates script in the preview; the Corners global squares the button.
        var page = await OpenAsync("scenario/inputs-button--primary?panel=x-styles&g.corners=square");
        await WaitReadyAsync(page);
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Styles" })).ToHaveAttributeAsync("aria-selected", "true");
        var radius = page.Locator(".wb-tabpanel tr", new() { HasText = "border-radius" });
        await Expect(radius).ToContainTextAsync("0px", new() { Timeout = 20_000 });

        await page.Locator("#wb-global-corners").SelectOptionAsync("rounded");
        await WaitReadyAsync(page);
        await Expect(radius).Not.ToContainTextAsync("0px", new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Complex_parameters_can_be_edited_as_json()
    {
        var page = await OpenAsync("scenario/data-data-table--few-rows");
        await WaitReadyAsync(page);
        var row = page.Locator("tr", new() { Has = page.Locator("#wb-param-Items") });
        await row.GetByText("Edit as JSON").ClickAsync();
        await row.Locator("textarea.wb-json-editor").FillAsync("[{\"name\":\"Grace Hopper\",\"email\":\"grace@example.com\",\"age\":85,\"country\":\"US\"}]");
        await row.Locator("textarea.wb-json-editor").BlurAsync();
        await Expect(Preview(page).Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(Preview(page).Locator("tbody")).ToContainTextAsync("Grace Hopper");
        await Expect(page).ToHaveURLAsync(new Regex("args="));
    }

    [Fact]
    public async Task Synchronized_compare_frames_repeat_interactions()
    {
        var page = await OpenAsync("scenario/inputs-counter--default?mode=compare");
        var frames = page.Locator(".wb-compare-cell iframe");
        await Expect(frames.Nth(1)).ToBeAttachedAsync(); // one frame per theme: at least two
        var first = page.FrameLocator(".wb-compare-cell:nth-child(1) iframe");
        var second = page.FrameLocator(".wb-compare-cell:nth-child(2) iframe");
        await Expect(first.Locator("html[data-workbench-state=ready]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(second.Locator("html[data-workbench-state=ready]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        var before = await second.Locator(".counter").InnerTextAsync();

        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Sync interactions" }).CheckAsync();
        await first.GetByRole(AriaRole.Button, new() { Name = "Increase" }).ClickAsync();
        await Expect(second.Locator(".counter")).Not.ToHaveTextAsync(before, new() { Timeout = 10_000 });
        Assert.Equal(await first.Locator(".counter").InnerTextAsync(), await second.Locator(".counter").InnerTextAsync());
    }

    [Fact]
    public async Task Referenced_catalogs_appear_in_the_explorer_search_and_main_area()
    {
        var page = await OpenAsync("");
        var group = page.Locator("details.wb-ref", new() { HasText = "Trail icons" });
        await Expect(group).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await group.Locator("summary").ClickAsync();
        await group.GetByRole(AriaRole.Link, new() { Name = "Icons / Trail marker" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("refs/trail-icons/icons-trail-marker"));
        await Expect(page.Locator(".wb-ref-page h1")).ToContainTextAsync("Trail marker");
        await Expect(page.FrameLocator(".wb-ref-frame").Locator("h1")).ToHaveTextAsync("Trail marker");

        await page.Locator("#wb-search").FillAsync("summit");
        await Expect(page.Locator(".wb-results a", new() { HasText = "Icons/Summit" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Documentation_pages_render_source_facts_and_do_dont_callouts()
    {
        var page = await OpenAsync("pages/guidelines-buttons-and-actions");
        await Expect(page.Locator(".wb-embed-source code")).ToContainTextAsync("<Button");
        await Expect(page.Locator(".wb-callout-do")).ToContainTextAsync("Write the label as a verb");
        await Expect(page.Locator(".wb-callout-dont .wb-callout-title")).ToHaveTextAsync("Don't");
    }
}
