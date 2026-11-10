using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Device mode: resizing the viewport, breakpoints, touch emulation, overflow and the responsive sweep of the runner.</summary>
public class ResponsiveTests(WorkbenchFixture fixture) : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("wb-e2e-responsive");

    public void Dispose() => _temp.Delete(true);

    private async Task<IPage> OpenAsync(string path)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(path);
        await Expect(page.Locator(".wb-state-ready")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // The shell module (drag, keys, messages) is loaded after the first render.
        await Expect(page.Locator("html[data-workbench-shell=ready]")).ToHaveCountAsync(1);
        return page;
    }

    private static IFrameLocator Preview(IPage page) => page.FrameLocator("#wb-preview-frame");

    private static async Task ScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(WorkbenchFixture.RepositoryRoot, "artifacts", "e2e");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png") });
    }

    private static Task<double> InnerWidthAsync(IPage page) => Preview(page).Locator("html").EvaluateAsync<double>("() => window.innerWidth");

    [Fact]
    public async Task Dragging_the_edge_resizes_the_viewport_under_the_pointer()
    {
        var page = await OpenAsync("scenario/layout-app-header--desktop");
        var handle = page.Locator(".wb-resize-e");
        var box = (await handle.BoundingBoxAsync())!;
        var (x, y) = (box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        for (var i = 1; i <= 10; i++)
            await page.Mouse.MoveAsync(x - i * 30, y);
        await page.Mouse.UpAsync();

        await Expect(page).ToHaveURLAsync(new Regex(@"viewport=\d+x\d+"));
        await ScreenshotAsync(page, "device-mode-resized");
        var width = int.Parse(await page.Locator("#wb-viewport-width").InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(width, await InnerWidthAsync(page), 1);
        // The handle followed the pointer, although the centered preview grows on both sides.
        var after = (await handle.BoundingBoxAsync())!;
        Assert.Equal(x - 300, after.X + after.Width / 2, 3);
        Assert.Equal("custom", await page.Locator("#wb-viewport").InputValueAsync());
    }

    [Fact]
    public async Task Arrow_keys_on_a_handle_resize_by_one_or_ten_pixels()
    {
        var page = await OpenAsync("scenario/layout-app-header--desktop?viewport=400x700");
        await page.Locator(".wb-resize-e").FocusAsync();
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page).ToHaveURLAsync(new Regex("viewport=411x700"));
        await page.Locator(".wb-resize-s").FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await Expect(page).ToHaveURLAsync(new Regex("viewport=411x699"));
        Assert.Equal(411, await InnerWidthAsync(page), 1);
    }

    [Fact]
    public async Task Breakpoints_are_read_from_the_stylesheets_and_set_the_width()
    {
        var page = await OpenAsync("scenario/layout-app-header--desktop?viewport=1000x700");
        // demo.css: (max-width: 600px), (max-width: 640px), (max-width: 720px) and (max-width: 900px).
        await Expect(page.Locator(".wb-ruler-band")).ToHaveCountAsync(4);
        await Expect(page.Locator("#wb-breakpoint-range")).ToHaveTextAsync("≥ 901");

        await page.Locator("#wb-breakpoints summary").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Set the width to 720 px" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("viewport=720x700"));
        await Expect(Preview(page).GetByRole(AriaRole.Button, new() { Name = "Menu" })).ToBeVisibleAsync();
        await Expect(page.Locator("#wb-breakpoint-range")).ToHaveTextAsync("641–720");
        await ScreenshotAsync(page, "device-mode-breakpoints");

        await page.GetByRole(AriaRole.Button, new() { Name = "Set the width to 721 px" }).ClickAsync();
        await Expect(Preview(page).GetByRole(AriaRole.Button, new() { Name = "Menu" })).ToBeHiddenAsync();

        // The ruler: a click on a band sets its width, like the media query bar of browser developer tools.
        await page.Locator(".wb-ruler-band", new() { HasText = "601" }).ClickAsync(new() { Position = new() { X = 4, Y = 9 } });
        await Expect(page).ToHaveURLAsync(new Regex("viewport=601x700"));
        Assert.Equal(601, await InnerWidthAsync(page), 1);
    }

    [Fact]
    public async Task Mobile_presets_emulate_touch_and_the_switch_overrides_it()
    {
        var page = await OpenAsync("scenario/inputs-button--primary");
        var button = Preview(page).GetByRole(AriaRole.Button);
        Assert.NotEqual("44px", await button.EvaluateAsync<string>("b => getComputedStyle(b).minHeight"));

        await page.Locator("#wb-viewport").SelectOptionAsync("mobile");
        await Expect(page.Locator("#wb-touch")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Preview(page).Locator("html[data-wb-touch]")).ToHaveCountAsync(1);
        Assert.Equal("44px", await button.EvaluateAsync<string>("b => getComputedStyle(b).minHeight"));
        Assert.True(await button.EvaluateAsync<bool>("() => matchMedia('(hover: none)').matches && matchMedia('(pointer: coarse)').matches"));

        await page.Locator("#wb-touch").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("touch=0"));
        await Expect(Preview(page).Locator("html[data-wb-touch]")).ToHaveCountAsync(0);
        Assert.NotEqual("44px", await button.EvaluateAsync<string>("b => getComputedStyle(b).minHeight"));

        // Breakpoint detection reads the original media queries, not the rewritten ones.
        var widths = await Preview(page).Locator("html").EvaluateAsync<int[]>("() => window.__workbench.breakpoints().map(b => b.width)");
        Assert.Equal([601, 641, 721, 901], widths);
    }

    [Fact]
    public async Task Sideways_scrolling_is_flagged_and_its_cause_highlighted()
    {
        var page = await OpenAsync("scenario/display-card--dont-unbreakable-link");
        await Expect(page.Locator("#wb-overflow")).ToContainTextAsync("too wide");
        await page.Locator("#wb-overflow").ClickAsync();
        await Expect(Preview(page).Locator("[data-wb-part=highlight] > div")).Not.ToHaveCountAsync(0);
        await ScreenshotAsync(page, "device-mode-overflow");

        var layout = await Preview(page).Locator("html").EvaluateAsync<JsonElement>("() => window.__workbench.layout()");
        Assert.True(layout.GetProperty("overflow").GetDouble() > 0);
        Assert.Contains("card", layout.GetProperty("offenders")[0].GetProperty("selector").GetString(), StringComparison.Ordinal);

        // A layout that fits shows no flag.
        var fits = await OpenAsync("scenario/display-card--text-content?viewport=mobile");
        await Expect(Preview(fits).Locator("article, section, div").First).ToBeVisibleAsync();
        await Expect(fits.Locator("#wb-overflow")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Fit_zooms_a_large_viewport_into_the_canvas()
    {
        var page = await OpenAsync("scenario/layout-app-header--desktop?viewport=desktop");
        await page.Locator("#wb-zoom-fit").ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"zoom=0\.\d+"));
        var zoomBox = (await page.Locator(".wb-zoom").BoundingBoxAsync())!;
        var area = (await page.Locator(".wb-frame-area").BoundingBoxAsync())!;
        Assert.True(zoomBox.Width <= area.Width && zoomBox.Height <= area.Height, $"{zoomBox.Width}×{zoomBox.Height} does not fit {area.Width}×{area.Height}.");
        Assert.Equal(1440, await InnerWidthAsync(page), 1);
    }

    [Fact]
    public async Task Compare_shows_both_sides_of_every_breakpoint()
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync("scenario/layout-app-header--desktop?mode=compare&compare=width");
        var captions = page.Locator(".wb-compare-cell figcaption");
        await Expect(captions).ToContainTextAsync(["320 px (reflow)", "600 px", "601 px", "640 px", "641 px", "720 px", "721 px", "900 px", "901 px"], new() { Timeout = 30_000 });
        var frame = (await page.Locator(".wb-compare-cell iframe").Nth(6).BoundingBoxAsync())!;
        await ScreenshotAsync(page, "compare-breakpoints");
        Assert.Equal(721 * 0.25, frame.Width, 1);
    }

    [Fact]
    public async Task Runner_sweeps_breakpoint_edges_and_honors_allowed_overflow()
    {
        var options = new TestRunOptions
        {
            Filter = "layout-toolbar--left-to-right,data-data-table--few-rows,display-card--dont-unbreakable-link",
            OutputDirectory = Path.Combine(_temp.FullName, "out"),
            Interactions = false,
            Responsive = true,
            Viewports = ["laptop"],
            Workers = 2,
        };
        var report = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        CheckResult Responsive(string id) => report.Scenarios.Single(s => s.ScenarioId == id).Checks.Single(c => c.Kind == CheckKind.Responsive);

        var toolbar = Responsive("layout-toolbar--left-to-right");
        Assert.Equal(CheckStatus.Passed, toolbar.Status);
        Assert.Equal(["320 px", "600 px", "601 px", "640 px", "641 px", "720 px", "721 px", "900 px", "901 px"], toolbar.Steps!.Select(s => s.Description));

        var table = Responsive("data-data-table--few-rows");
        Assert.Equal(CheckStatus.Passed, table.Status);
        Assert.Equal("320 px (allowed overflow)", table.Steps![0].Description);

        Assert.Equal(CheckStatus.Skipped, Responsive("display-card--dont-unbreakable-link").Status);
        Assert.True(report.Settings.Responsive);
    }

    [Fact]
    public async Task Runner_fails_a_width_that_scrolls_sideways_and_names_the_culprit()
    {
        var options = new TestRunOptions
        {
            Filter = "layout-app-header--desktop",
            OutputDirectory = Path.Combine(_temp.FullName, "out"),
            Interactions = false,
            Responsive = true,
            ResponsiveWidths = [120, 1280],
            Viewports = ["laptop"],
        };
        var report = await new WorkbenchTestRunner(fixture.Host, options).RunAsync();
        var check = report.Scenarios.Single().Checks.Single(c => c.Kind == CheckKind.Responsive);
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("120 px", check.Message, StringComparison.Ordinal);
        Assert.Contains("a.app-brand", check.Steps![0].Error, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Passed, check.Steps[1].Status);
        Assert.Equal(1, report.Summary.Failed);
    }
}
