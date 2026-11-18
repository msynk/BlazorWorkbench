using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo;
using BlazorWorkbench.Demo.Scenarios;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Components.Tests;

/// <summary>The review page (<c>/review</c>) steps through the failed scenarios of the last run.</summary>
public class ReviewTests : BunitContext
{
    private static readonly Lazy<WorkbenchCatalog> Demo = new(() =>
    {
        var options = new WorkbenchOptions();
        DemoCatalog.Configure(options);
        options.ScenarioAssemblies.Add(typeof(CheckoutScenarios).Assembly);
        return CatalogBuilder.Build(options);
    });

    public ReviewTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static TestRunSummary Run() => new(DateTimeOffset.UtcNow, new Dictionary<string, ScenarioRunStatus>
    {
        ["inputs-button--primary"] = new("Failed", "laptop-light: 2.1% of pixels differ", false, ["Visual"]),
        ["inputs-button--danger"] = new("Failed", "color-contrast (serious)", false, ["Accessibility"]),
        ["inputs-button--secondary"] = new("Passed", null, false),
        ["removed--scenario"] = new("Failed", "gone", false, ["Visual"]),
    });

    [Fact]
    public void Lists_failed_scenarios_and_steps_through_them()
    {
        Services.AddSingleton<ITestResultsProvider>(new FixedResults(Run()));
        Services.AddSingleton<IVisualResultsProvider>(new FakeVisual());
        var cut = Render<ReviewView>(p => p.Add(r => r.Catalog, Demo.Value));

        // Passed scenarios and scenarios the catalog no longer has are not in the queue.
        var entries = cut.FindAll(".wb-review-entry");
        Assert.Equal(2, entries.Count);
        Assert.Contains("Primary", entries[0].TextContent);
        Assert.Equal("true", entries[0].GetAttribute("aria-current"));
        Assert.Empty(cut.FindAll(".wb-review-message")); // the Visual panel shows the screenshot failure itself
        Assert.Single(cut.FindAll(".wb-visual-result"));
        Assert.Equal("scenario/inputs-button--primary?panel=visual", cut.Find(".wb-review-item-header a").GetAttribute("href"));

        cut.Find("#wb-review-next").Click();
        Assert.Contains("color-contrast", cut.Find(".wb-review-message").TextContent);
        Assert.Empty(cut.FindAll(".wb-visual-result"));
        Assert.Equal("scenario/inputs-button--danger?panel=accessibility", cut.Find(".wb-review-item-header a").GetAttribute("href"));

        cut.Find("#wb-review-next").Click(); // wraps around
        Assert.Equal("Primary", cut.Find("#wb-review-item-title").TextContent);

        cut.Find("input[value='accessibility']").Change(true);
        Assert.Single(cut.FindAll(".wb-review-entry"));
        Assert.Contains("color-contrast", cut.Find(".wb-review-message").TextContent);
    }

    [Fact]
    public void Approving_every_screenshot_marks_the_scenario_and_moves_on()
    {
        Services.AddSingleton<ITestResultsProvider>(new FixedResults(Run()));
        var visual = new FakeVisual();
        Services.AddSingleton<IVisualResultsProvider>(visual);
        var cut = Render<ReviewView>(p => p.Add(r => r.Catalog, Demo.Value));

        cut.Find(".wb-visual-approve").Click();

        cut.WaitForAssertion(() => Assert.Contains("Approved", cut.FindAll(".wb-review-entry")[0].TextContent));
        Assert.Equal(["inputs-button--primary"], visual.ApprovedScenarios);
        Assert.Equal("true", cut.FindAll(".wb-review-entry")[1].GetAttribute("aria-current"));
        Assert.Contains("1 of 1 visual change approved", cut.Find("#wb-review-progress").TextContent);
    }

    [Fact]
    public void Explains_what_to_do_without_a_run_or_failures()
    {
        Services.AddSingleton<ITestResultsProvider>(new FixedResults(TestRunSummary.Empty));
        var cut = Render<ReviewView>(p => p.Add(r => r.Catalog, Demo.Value));
        Assert.Contains("workbench test --visual --a11y", cut.Markup);

        var passed = new TestRunSummary(DateTimeOffset.UtcNow, new Dictionary<string, ScenarioRunStatus> { ["inputs-button--primary"] = new("Passed", null, false) });
        using var other = new BunitContext();
        other.Services.AddSingleton<ITestResultsProvider>(new FixedResults(passed));
        var done = other.Render<ReviewView>(p => p.Add(r => r.Catalog, Demo.Value));
        Assert.Contains("Nothing to review", done.Markup);
    }

    [Fact]
    public void Explorer_links_to_the_review_when_the_last_run_failed()
    {
        Services.AddSingleton<ITestResultsProvider>(new FixedResults(Run()));
        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, Demo.Value));
        Assert.Equal("review", cut.Find("#wb-review-link").GetAttribute("href"));
    }

    private sealed class FixedResults(TestRunSummary summary) : ITestResultsProvider
    {
        public Task<TestRunSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(summary);
    }

    private sealed class FakeVisual : IVisualResultsProvider
    {
        private readonly HashSet<string> _approved = [];
        public List<string> ApprovedScenarios { get; } = [];

        public Task<IReadOnlyList<VisualComparisonView>> GetAsync(string scenarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VisualComparisonView>>(scenarioId == "inputs-button--primary"
                ? [new("laptop-light", _approved.Contains(scenarioId) ? "Approved" : "Failed", "b.png", "a.png", "d.png", 0.021, null, null)]
                : []);

        public string Describe() => "fake results";

        public ValueTask<bool> CanApproveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public Task<VisualApprovalResult> ApproveAsync(string scenarioId, string? variant = null, CancellationToken cancellationToken = default)
        {
            _approved.Add(scenarioId);
            ApprovedScenarios.Add(scenarioId);
            return Task.FromResult(new VisualApprovalResult([$"windows/{scenarioId}/laptop-light.png"], "Approved 1 baseline(s)."));
        }
    }
}
