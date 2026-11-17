using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Demo;
using BlazorWorkbench.Demo.Scenarios;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.Components.Tests;

public class ChangeDetectionTests : BunitContext
{
    private static readonly Lazy<WorkbenchCatalog> Demo = new(() =>
    {
        var options = new WorkbenchOptions();
        DemoCatalog.Configure(options);
        options.ScenarioAssemblies.Add(typeof(CheckoutScenarios).Assembly);
        return CatalogBuilder.Build(options);
    });

    private static ComponentEntry Button => Demo.Value.FindComponent("inputs-button")!;

    [Fact]
    public void A_changed_component_marks_its_scenarios_and_the_components_that_render_it()
    {
        var catalog = Demo.Value;
        var used = catalog.Components.First(c => c.UsedBy.Count > 0 && c.Metadata.Source is not null);
        var summary = CatalogChanges.Map(catalog, [new ChangedFile(used.Metadata.Source!.Path, IsNew: false)]);

        Assert.All(used.Scenarios, s => Assert.Equal(ScenarioChangeKind.Modified, summary.Scenarios[s.Id]));
        foreach (var user in used.UsedBy.Select(catalog.FindComponent).OfType<ComponentEntry>())
            Assert.All(user.Scenarios, s => Assert.Equal(ScenarioChangeKind.Related, summary.Scenarios[s.Id]));
    }

    [Fact]
    public void Isolated_css_and_code_behind_map_to_their_component_and_new_files_are_new()
    {
        var source = Button.Metadata.Source!.Path;
        var css = CatalogChanges.Map(Demo.Value, [new ChangedFile(source + ".css", IsNew: true)]);
        Assert.All(Button.Scenarios, s => Assert.Equal(ScenarioChangeKind.New, css.Scenarios[s.Id]));

        // A modification wins over "new" when both are reported for the same component.
        var both = CatalogChanges.Map(Demo.Value, [new ChangedFile(source + ".cs", IsNew: true), new ChangedFile(source, IsNew: false)]);
        Assert.All(Button.Scenarios, s => Assert.Equal(ScenarioChangeKind.Modified, both.Scenarios[s.Id]));
    }

    [Fact]
    public void A_changed_scenario_set_marks_only_its_scenarios()
    {
        var set = SourceLocator.Find(typeof(CheckoutScenarios))!.Path;
        var root = Path.GetDirectoryName(set)!;
        var summary = CatalogChanges.Map(Demo.Value, [new ChangedFile(set, false), new ChangedFile(Path.Combine(root, "notes.md"), false)], root);
        Assert.NotEmpty(summary.Scenarios);
        Assert.All(summary.Scenarios, s => Assert.Equal(ScenarioChangeKind.Modified, s.Value));
        Assert.Contains(summary.Scenarios.Keys, id => id.StartsWith("commerce-checkout--", StringComparison.Ordinal));
        Assert.Contains("notes.md", summary.Files);
    }

    [Fact]
    public void Git_status_output_is_parsed()
    {
        var output = " M src/A.razor\0?? src/New.razor\0R  src/Renamed.razor\0src/Old.razor\0D  src/Gone.razor\0 D src/Removed.razor\0A  src/Added.cs\0";
        var entries = GitWorkingCopy.ParseStatus(output);
        Assert.Equal([("src/A.razor", false), ("src/New.razor", true), ("src/Renamed.razor", false), ("src/Added.cs", true)], entries);
    }

    [Fact]
    public async Task Git_reading_reports_the_repository_state_or_why_not()
    {
        var (files, description) = await GitWorkingCopy.ReadAsync(Path.GetTempPath(), cancellationToken: Xunit.TestContext.Current.CancellationToken);
        // The temp directory is usually not a repository; either way the call never throws.
        Assert.False(string.IsNullOrWhiteSpace(description));
        _ = files;
    }

    [Fact]
    public void Explorer_marks_and_filters_changed_scenarios()
    {
        var catalog = Demo.Value;
        var summary = CatalogChanges.Map(catalog, [new ChangedFile(Button.Metadata.Source!.Path, false)], description: "Uncommitted changes");
        Services.AddSingleton<IChangesProvider>(new FixedChanges(summary));

        var cut = Render<Explorer>(p => p.Add(e => e.Catalog, catalog).Add(e => e.SelectedScenarioId, "inputs-button--primary"));
        Assert.Contains($"{Button.Scenarios.Count} changed", cut.Find(".wb-chip-changed").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll(".wb-change-modified"));

        cut.Find("#wb-search").Input("is:changed");
        Assert.Equal(Button.Scenarios.Count, cut.FindAll(".wb-results a").Count);
        cut.Find("#wb-search").Input("is:related");
        Assert.Equal(summary.Scenarios.Count(s => s.Value == ScenarioChangeKind.Related), cut.FindAll(".wb-results a").Count);
        cut.Find("#wb-search").Input("is:affected");
        Assert.Equal(summary.Scenarios.Count, cut.FindAll(".wb-results a").Count);
    }

    private sealed class FixedChanges(CatalogChangeSummary summary) : IChangesProvider
    {
        public Task<CatalogChangeSummary> GetChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(summary);
    }
}
