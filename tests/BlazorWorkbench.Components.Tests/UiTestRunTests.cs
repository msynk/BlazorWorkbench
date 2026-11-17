using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo;
using BlazorWorkbench.Demo.Scenarios;
using Bunit;

namespace BlazorWorkbench.Components.Tests;

public class UiTestRunTests : BunitContext
{
    private static readonly Lazy<WorkbenchCatalog> Demo = new(() =>
    {
        var options = new WorkbenchOptions();
        DemoCatalog.Configure(options);
        options.ScenarioAssemblies.Add(typeof(CheckoutScenarios).Assembly);
        return CatalogBuilder.Build(options);
    });

    [Fact]
    public void Scenarios_pass_only_when_every_check_came_back_and_fail_at_the_first_failure()
    {
        var run = new UiTestRun();
        run.Start(new UiTestRequest(["a", "b"], false, "All"), new Dictionary<string, int> { ["a"] = 2, ["b"] = 1 });
        run.Add(new UiTestResult("a", "render", null, "passed", null, 10, null));
        Assert.False(run.Summary.Scenarios.ContainsKey("a")); // one interaction still pending

        run.Add(new UiTestResult("a", "interaction", "submits", "failed", "Step 2, Click: timeout", 10, null));
        Assert.Equal("Failed", run.Summary.Scenarios["a"].Status);
        Assert.Equal("submits: Step 2, Click: timeout", run.Summary.Scenarios["a"].Message);

        run.Add(new UiTestResult("b", "render", null, "warning", "2 minor accessibility violation(s).", 10, null));
        Assert.Equal("Passed", run.Summary.Scenarios["b"].Status);
        Assert.Equal((1, 1, 1), (run.Passed, run.Failed, run.Warnings));
        Assert.True(run.Running);

        run.Finish();
        Assert.False(run.Running);
        Assert.Equal(3, run.Done);
        Assert.Equal(3, run.Total);
    }

    [Fact]
    public void Infrastructure_errors_and_skips_are_reported_as_such()
    {
        var run = new UiTestRun();
        run.Start(new UiTestRequest(["a", "b"], false, "All"), new Dictionary<string, int> { ["a"] = 1 });
        run.Skip("b", "broken on purpose");
        run.Add(new UiTestResult("a", "render", null, "error", "The preview was not ready", 10, null));
        Assert.Equal("Error", run.Summary.Scenarios["a"].Status);
        Assert.Equal("Skipped", run.Summary.Scenarios["b"].Status);
    }

    [Fact]
    public void Runner_bar_runs_the_chosen_scope()
    {
        var catalog = Demo.Value;
        var component = catalog.FindComponent("inputs-button")!;
        UiTestRequest? requested = null;
        var run = new UiTestRun();
        var cut = Render<TestRunnerBar>(p => p
            .Add(b => b.Catalog, catalog)
            .Add(b => b.Run, run)
            .Add(b => b.Component, component)
            .Add(b => b.OnRun, r => requested = r));

        cut.Find("#wb-runner-scope").Change("component");
        cut.Find("#wb-runner-a11y").Change(true);
        cut.Find("#wb-runner-run").Click();
        Assert.NotNull(requested);
        Assert.Equal(component.Scenarios.Select(s => s.Id), requested.ScenarioIds);
        Assert.True(requested.Accessibility);

        // Nothing failed yet: the "Failing" scope says so instead of running everything.
        requested = null;
        cut.Find("#wb-runner-scope").Change("failing");
        cut.Find("#wb-runner-run").Click();
        Assert.Null(requested);
        Assert.Contains("No failing scenarios", cut.Find(".wb-runner-summary").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_bar_lists_failures_with_links_to_the_right_panel()
    {
        var catalog = Demo.Value;
        var run = new UiTestRun();
        run.Start(new UiTestRequest(["inputs-button--primary"], true, "All scenarios"), new Dictionary<string, int> { ["inputs-button--primary"] = 2 });
        run.Add(new UiTestResult("inputs-button--primary", "interaction", "click raises OnClick", "failed", "Step 1, Click: not found", 10, null));
        var cut = Render<TestRunnerBar>(p => p.Add(b => b.Catalog, catalog).Add(b => b.Run, run));

        Assert.NotNull(cut.Find("#wb-runner-stop"));
        Assert.Contains("✖ 1", cut.Find(".wb-runner-summary").TextContent, StringComparison.Ordinal);
        var link = cut.Find(".wb-runner-failures a");
        Assert.Equal("scenario/inputs-button--primary?panel=interactions", link.GetAttribute("href"));
    }

    [Fact]
    public void Explorer_prefers_this_sessions_results_over_the_last_cli_run()
    {
        var run = new UiTestRun();
        run.Start(new UiTestRequest(["inputs-button--danger"], false, "All"), new Dictionary<string, int> { ["inputs-button--danger"] = 1 });
        run.Add(new UiTestResult("inputs-button--danger", "render", null, "failed", "1 error diagnostic(s)", 10, null));
        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, Demo.Value).Add(e => e.UiRun, run).Add(e => e.SelectedScenarioId, "inputs-button--danger"));

        Assert.NotNull(cut.Find(".wb-runner"));
        cut.Find("#wb-search").Input("is:failing");
        var result = Assert.Single(cut.FindAll(".wb-results a"));
        Assert.Equal("scenario/inputs-button--danger", result.GetAttribute("href"));
    }
}
