using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace BlazorWorkbench.E2E.Tests;

/// <summary>Media emulation, animation controls, pins, JavaScript interop simulation and the sidebar test runner, in a real browser.</summary>
public class WorkflowTests(WorkbenchFixture fixture)
{
    private async Task<IPage> OpenAsync(string path)
    {
        var page = await fixture.NewPageAsync();
        await page.GotoAsync(path);
        await Expect(page.Locator("html[data-workbench-shell=ready]")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        return page;
    }

    private static async Task WaitReadyAsync(IPage page) =>
        await Expect(page.Locator(".wb-state-ready")).ToBeVisibleAsync(new() { Timeout = 20_000 });

    private static IFrameLocator Preview(IPage page) => page.FrameLocator("#wb-preview-frame");

    private static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> done, int timeoutMs = 10_000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var value = await read();
            if (done(value))
                return value;
            if (watch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"Still {value} after {timeoutMs} ms.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Media_emulation_rewrites_media_queries_and_match_media()
    {
        var page = await OpenAsync("scenario/data-weather-forecast--loading");
        await WaitReadyAsync(page);
        var spinner = Preview(page).Locator(".spinner");
        Assert.NotEqual("none", await spinner.EvaluateAsync<string>("s => getComputedStyle(s).animationName"));
        Assert.False(await spinner.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));

        await page.Locator("#wb-tool-media summary").ClickAsync();
        await page.Locator("#wb-tool-media-reduced-motion").CheckAsync();
        await Expect(page).ToHaveURLAsync(new Regex("media=reduced-motion"));
        await PollAsync(() => spinner.EvaluateAsync<string>("s => getComputedStyle(s).animationName"), v => v == "none");
        Assert.True(await spinner.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));
        Assert.False(await spinner.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: no-preference)').matches"));

        await page.Locator("#wb-tool-media-reduced-motion").UncheckAsync();
        await PollAsync(() => spinner.EvaluateAsync<string>("s => getComputedStyle(s).animationName"), v => v != "none");
    }

    [Fact]
    public async Task Print_emulation_applies_print_rules_from_the_url()
    {
        var page = await OpenAsync("scenario/display-card--with-footer?media=print");
        await WaitReadyAsync(page);
        Assert.Equal("none", await Preview(page).Locator(".card-footer").EvaluateAsync<string>("f => getComputedStyle(f).display"));
        Assert.True(await Preview(page).Locator("html").EvaluateAsync<bool>("() => matchMedia('print').matches"));
    }

    [Fact]
    public async Task Animations_can_be_paused_at_a_position_and_resumed()
    {
        var page = await OpenAsync("scenario/data-weather-forecast--loading?anim=paused&animAt=0.5");
        await WaitReadyAsync(page);
        var spinner = Preview(page).Locator(".spinner");
        await PollAsync(() => spinner.EvaluateAsync<string>("s => s.getAnimations()[0]?.playState ?? 'none'"), v => v == "paused");
        // Half of the 0.8 s rotation.
        Assert.Equal(400, await spinner.EvaluateAsync<double>("s => s.getAnimations()[0].currentTime"), 1);

        await page.Locator("#wb-tool-media summary").ClickAsync();
        await page.Locator("#wb-tool-anim-run").CheckAsync();
        await PollAsync(() => spinner.EvaluateAsync<string>("s => s.getAnimations()[0]?.playState ?? 'none'"), v => v == "running");
    }

    [Fact]
    public async Task Pinned_scenarios_stay_at_the_top_of_the_explorer()
    {
        var page = await OpenAsync("scenario/inputs-button--danger");
        await WaitReadyAsync(page);
        await page.Locator(".wb-pin").ClickAsync();
        await Expect(page.Locator(".wb-pin")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator(".wb-pinned a[href='scenario/inputs-button--danger']")).ToHaveCountAsync(1);

        // Pins are kept in the browser.
        await page.GotoAsync("scenario/inputs-button--primary");
        await Expect(page.Locator(".wb-pinned a[href='scenario/inputs-button--danger']")).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await page.Keyboard.PressAsync("b");
        await Expect(page.Locator(".wb-pinned a")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task Planned_javascript_calls_are_answered_and_listed()
    {
        var page = await OpenAsync("scenario/feedback-whats-new-banner--storage-blocked?panel=environment");
        await WaitReadyAsync(page);
        await Expect(Preview(page).GetByRole(AriaRole.Region, new() { Name = "What's new" })).ToBeVisibleAsync();
        var calls = page.GetByRole(AriaRole.Table, new() { Name = "JavaScript calls", Exact = true });
        await Expect(calls).ToContainTextAsync("localStorage.getItem", new() { Timeout = 10_000 });
        await Expect(calls).ToContainTextAsync("planned error");

        await Preview(page).GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        await Expect(calls).ToContainTextAsync("localStorage.setItem", new() { Timeout = 10_000 });
        await Expect(page.GetByRole(AriaRole.Table, new() { Name = "Planned JavaScript calls" })).ToContainTextAsync("SecurityError");
    }

    [Fact]
    public async Task The_sidebar_runner_tests_a_component_and_marks_the_explorer()
    {
        var page = await OpenAsync("scenario/feedback-whats-new-banner--first-visit");
        await WaitReadyAsync(page);
        await page.Locator("#wb-runner-scope").SelectOptionAsync("component");
        await page.Locator("#wb-runner-a11y").CheckAsync();
        await page.Locator("#wb-runner-run").ClickAsync();

        var summary = page.Locator(".wb-runner-summary");
        await Expect(summary).ToContainTextAsync(new Regex(@"in \d"), new() { Timeout = 60_000 });
        await Expect(summary).ToContainTextAsync("✔");
        await Expect(page.Locator(".wb-runner-failures")).ToHaveCountAsync(0);
        // Every scenario of the component got a result dot.
        var scenarios = page.Locator("details[open] a[href^='scenario/feedback-whats-new-banner--']");
        var count = await scenarios.CountAsync();
        Assert.True(count >= 5);
        await Expect(page.Locator("details[open] a[href^='scenario/feedback-whats-new-banner--'] .wb-run-passed")).ToHaveCountAsync(count);

        // The interaction results of the scenario on screen show in its panel.
        await page.Locator("#wb-tab-interactions").ClickAsync();
        await Expect(page.Locator(".wb-tabpanel")).ToContainTextAsync("dismissing hides the banner");
    }
}
