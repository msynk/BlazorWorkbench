using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo;
using BlazorWorkbench.Demo.Scenarios;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Components.Tests;

public class PreviewToolsTests
{
    [Fact]
    public void Tools_survive_a_round_trip_through_the_url()
    {
        var tools = new PreviewTools
        {
            Zoom = 0.5, Background = "dark", Grid = true, Outline = true, Measure = true, Pseudo = ["hover", "focus-visible"],
            TextScale = 200, TextSpacing = true, TabOrder = true, Updates = true, Vision = "deuteranopia", Rotate = true,
            Overlay = "designs/a.png", OverlayOpacity = 30, OverlayBlend = "difference",
        };

        var query = tools.ToQuery().Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var parsed = PreviewTools.Parse(query.GetValueOrDefault);

        Assert.Equal(tools.ToQuery(), parsed.ToQuery());
        Assert.Equal(12, parsed.ActiveCount); // two forced states count separately
        Assert.Equal("url(#wb-vision-deuteranopia)", parsed.VisionFilter);
    }

    [Fact]
    public void Defaults_produce_no_query_and_invalid_values_are_ignored()
    {
        Assert.Empty(PreviewTools.None.ToQuery());
        var query = new Dictionary<string, string>
        {
            ["zoom"] = "40", ["text"] = "9000", ["vision"] = "x-ray", ["pseudo"] = "hover,visited", ["overlay"] = "javascript:alert(1)", ["overlayBlend"] = "screen",
        };

        var parsed = PreviewTools.Parse(query.GetValueOrDefault);

        Assert.Equal(1, parsed.Zoom);
        Assert.Equal(100, parsed.TextScale);
        Assert.Null(parsed.Vision);
        Assert.Equal(["hover"], parsed.Pseudo);
        Assert.Null(parsed.Overlay);
        Assert.Equal("normal", parsed.OverlayBlend);
    }

    [Fact]
    public void The_preview_query_carries_the_background_value_not_its_id()
    {
        var tools = new PreviewTools { Background = "transparent", Grid = true };
        Assert.Equal(["bg=%23123456", "grid=1"], tools.PreviewQuery("#123456"));
        Assert.DoesNotContain(tools.PreviewQuery(null), p => p.StartsWith("bg=", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_vision_simulation_has_a_filter()
    {
        foreach (var (key, _) in PreviewTools.VisionModes)
            Assert.NotNull((PreviewTools.None with { Vision = key }).VisionFilter);
    }
}

public class DocumentationViewTests : BunitContext
{
    private static readonly Lazy<WorkbenchCatalog> Demo = new(() =>
    {
        var options = new WorkbenchOptions();
        DemoCatalog.Configure(options);
        options.ScenarioAssemblies.Add(typeof(CheckoutScenarios).Assembly);
        return CatalogBuilder.Build(options);
    });

    [Fact]
    public void Markdown_directives_become_live_previews_and_tables()
    {
        var cut = Render<MarkdownView>(p => p
            .Add(m => m.Markdown, "Intro **bold**\n\n{{scenario:inputs-button--primary}}\n\n{{parameters:inputs-button}}\n\n{{scenario:missing--nope}}")
            .Add(m => m.Catalog, Demo.Value)
            .Add(m => m.PreviewTheme, "dark"));

        Assert.Contains("<strong>bold</strong>", cut.Find(".wb-markdown").InnerHtml);
        Assert.Equal("_workbench/preview/inputs-button--primary?theme=dark", cut.Find(".wb-embed iframe").GetAttribute("src"));
        Assert.Contains("Variant", cut.Find("table").TextContent);
        Assert.Contains("missing--nope", cut.Find(".wb-callout-warning").TextContent);
    }

    [Fact]
    public void Parameter_table_marks_values_no_scenario_covers()
    {
        var cut = Render<ParameterTable>(p => p.Add(t => t.Component, Demo.Value.FindComponent("inputs-button")!));
        Assert.NotEmpty(cut.FindAll(".wb-cov-yes"));
        Assert.Contains(cut.FindAll(".wb-cov-yes"), c => c.TextContent == "Ghost");
    }

    [Fact]
    public void Docs_list_status_and_relationships()
    {
        var catalog = Demo.Value;
        var cut = Render<ComponentDocs>(p => p.Add(d => d.Component, catalog.FindComponent("analytics-sales-dashboard")!).Add(d => d.Catalog, catalog));
        Assert.Equal("beta", cut.Find(".wb-status").TextContent);
        Assert.Contains("KPI card", cut.Find("#relationships + dl").TextContent);
    }

    [Fact]
    public void Gallery_lays_out_a_two_axis_matrix_as_a_table()
    {
        var cut = Render<GalleryView>(p => p.Add(g => g.Component, Demo.Value.FindComponent("inputs-button")!));
        var table = cut.Find("table.wb-matrix");
        Assert.Equal(["Small", "Medium", "Large"], table.QuerySelectorAll("thead th").Skip(1).Select(t => t.TextContent));
        Assert.Equal(4, table.QuerySelectorAll("tbody tr").Length);
        Assert.Equal(12, table.QuerySelectorAll("iframe").Length);
    }

    [Fact]
    public void Explorer_filters_with_status_and_kind_tokens_and_lists_pages()
    {
        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, Demo.Value));
        Assert.Contains(cut.FindAll(".wb-node-page"), a => a.TextContent.Contains("Buttons and actions", StringComparison.Ordinal));

        cut.Find("#wb-search").Input("status:experimental");
        var components = cut.FindAll(".wb-result-component").Select(c => c.TextContent).Distinct().ToList();
        Assert.NotEmpty(components);
        Assert.All(components, c => Assert.Contains("experimental", c, StringComparison.OrdinalIgnoreCase));

        cut.Find("#wb-search").Input("is:matrix button");
        Assert.Equal(12, cut.FindAll(".wb-results a").Count);

        cut.Find("#wb-search").Input("tokens");
        Assert.Contains(cut.FindAll(".wb-results a"), a => a.GetAttribute("href") == "pages/foundations-design-tokens");
    }

    [Fact]
    public void Explorer_shows_the_last_test_run()
    {
        var run = new TestRunSummary(DateTimeOffset.UtcNow, new Dictionary<string, ScenarioRunStatus>
        {
            ["inputs-button--primary"] = new("Failed", "visual diff", false),
            ["inputs-button--secondary"] = new("Passed", null, true),
        });
        Services.AddSingleton<ITestResultsProvider>(new FixedResults(run));

        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, Demo.Value).Add(e => e.SelectedScenarioId, "inputs-button--danger"));

        Assert.Contains("1 failing", cut.Find(".wb-chip-failed").TextContent);
        Assert.Single(cut.FindAll(".wb-run-failed[title*='visual diff']"));
        Assert.Single(cut.FindAll(".wb-run-flaky"));
        cut.Find("#wb-search").Input("is:failing");
        Assert.Single(cut.FindAll(".wb-results a"));
    }

    private sealed class FixedResults(TestRunSummary summary) : ITestResultsProvider
    {
        public Task<TestRunSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(summary);
    }
}
