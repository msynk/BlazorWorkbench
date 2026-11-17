using BlazorWorkbench.Catalog;
using BlazorWorkbench.Core.Tests.Components;

namespace BlazorWorkbench.Core.Tests;

public class ExplorerOrderTests
{
    private sealed class TitledSet<T>(string title) : ScenarioSet<T> where T : Microsoft.AspNetCore.Components.IComponent
    {
        protected override void Configure(ScenarioSetBuilder<T> set)
        {
            set.Title(title);
            set.Add("First").Add("Second");
        }
    }

    private static WorkbenchCatalog Catalog(params string[] order)
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddScenarios(new TitledSet<TestButton>("Inputs/Button"));
        options.AddScenarios(new TitledSet<PlainComponent>("Inputs/Forms/Text field"));
        options.AddScenarios(new TitledSet<Generic<int>>("Data/Grid"));
        options.AddScenarios(new TitledSet<Generic<string>>("Avatar"));
        foreach (var entry in order)
            options.ExplorerOrder.Add(entry);
        return CatalogBuilder.Build(options);
    }

    [Fact]
    public void Default_order_is_groups_then_components_alphabetically_depth_first()
    {
        var titles = ExplorerOrder.Components(Catalog()).Select(c => c.Title).ToArray();
        // Groups before components at each level, as the explorer tree shows them (not the catalog's title order).
        Assert.Equal(["Data/Grid", "Inputs/Forms/Text field", "Inputs/Button", "Avatar"], titles);
    }

    [Fact]
    public void Configured_paths_come_first_among_their_siblings()
    {
        var titles = ExplorerOrder.Components(Catalog("Avatar", "Inputs", "Inputs/Button")).Select(c => c.Title).ToArray();
        Assert.Equal(["Avatar", "Inputs/Button", "Inputs/Forms/Text field", "Data/Grid"], titles);
    }

    [Fact]
    public void Ranking_ignores_case_and_spaces_around_slashes()
    {
        Assert.Equal(1, ExplorerOrder.Rank(["Guides", "inputs / button"], "Inputs/Button"));
        Assert.Equal(int.MaxValue, ExplorerOrder.Rank(["Guides"], "Inputs"));
    }

    [Fact]
    public void Sort_orders_groups_pages_and_components()
    {
        var entries = new[]
        {
            ("Zeta", ExplorerOrder.EntryKind.Component),
            ("Alpha", ExplorerOrder.EntryKind.Component),
            ("Guide", ExplorerOrder.EntryKind.Page),
            ("Group", ExplorerOrder.EntryKind.Group),
        };
        var sorted = ExplorerOrder.Sort([], entries, e => (e.Item1, e.Item1, e.Item2)).Select(e => e.Item1).ToArray();
        Assert.Equal(["Group", "Guide", "Alpha", "Zeta"], sorted);

        var configured = ExplorerOrder.Sort(["Zeta"], entries, e => (e.Item1, e.Item1, e.Item2)).Select(e => e.Item1).ToArray();
        Assert.Equal("Zeta", configured[0]);
    }
}

public class EditorLinkTests
{
    [Fact]
    public void Scenarios_record_where_they_are_declared()
    {
        var options = new WorkbenchOptions { IncludeComponentsWithoutScenarios = false };
        options.AddScenarios(new DeclaredHere());
        var scenario = CatalogBuilder.Build(options).Scenarios.Single(s => s.Key == "located");
        Assert.NotNull(scenario.Source);
        Assert.EndsWith("ExplorerOrderTests.cs", scenario.Source!.Path, StringComparison.Ordinal);
        Assert.True(scenario.Source.Line > 0);
    }

    [Theory]
    [InlineData(@"C:\src\My App\Button.razor", 12, "vscode://file/{path}:{line}", "vscode://file/C:/src/My%20App/Button.razor:12")]
    [InlineData("/home/ada/ui/Card#1.razor", 3, "vscode://file/{path}:{line}", "vscode://file//home/ada/ui/Card%231.razor:3")]
    [InlineData("/repo/A.cs", 0, "rider://open?file={path}&line={line}", "rider://open?file=/repo/A.cs&line=1")]
    public void Editor_links_encode_the_path(string path, int line, string template, string expected)
    {
        var options = new WorkbenchOptions { EditorUrlTemplate = template };
        Assert.Equal(expected, options.EditorUrl(new SourceLocation(path, line)));
    }

    [Fact]
    public void No_template_means_no_links()
    {
        Assert.Null(new WorkbenchOptions().EditorUrl(new SourceLocation("/a.cs", 1)));
        Assert.Null(new WorkbenchOptions { EditorUrlTemplate = "" }.EditorUrl(new SourceLocation("/a.cs", 1)));
    }

    private sealed class DeclaredHere : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set) => set.Add("Located");
    }
}

public class EditorHintTests
{
    [Fact]
    public void Standard_attributes_shape_the_parameter_editors()
    {
        var metadata = new ComponentMetadataReader(_ => false, readDefaults: false, resolveSources: false).Read(typeof(AnnotatedSlider));
        var volume = metadata.FindParameter("Volume")!;
        Assert.Equal(("Volume level", "Sound", 2), (volume.DisplayName, volume.Group, volume.Order));
        Assert.Equal((0d, 100d), (volume.Minimum!.Value, volume.Maximum!.Value));

        var balance = metadata.FindParameter("Balance")!;
        Assert.Equal("Sound", balance.Group);
        Assert.Equal(1d, balance.Maximum);

        var caption = metadata.FindParameter("Caption")!;
        Assert.Equal(EditorKind.MultilineText, caption.Editor);
        Assert.Equal("Shown under the slider.", caption.Summary);
        Assert.Equal(140, caption.MaxLength);

        Assert.Equal(EditorKind.Color, metadata.FindParameter("Accent")!.Editor);
        Assert.True(metadata.FindParameter("InternalId")!.IsHidden);
        Assert.False(volume.IsHidden);
    }
}
