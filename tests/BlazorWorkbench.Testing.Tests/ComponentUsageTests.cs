using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing.Tests;

/// <summary><c>workbench usage</c>: tags of catalog components in .razor files, with the parameters they set.</summary>
public sealed class ComponentUsageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wb-usage").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static ParameterDescriptor P(string name, ParameterKind kind = ParameterKind.Value) =>
        new() { Name = name, TypeName = "string", Kind = kind };

    private static CatalogDto Catalog() => new()
    {
        Components =
        [
            new ComponentDto
            {
                Id = "inputs-button", Title = "Inputs/Button", Name = "Button", FullName = "Lib.Inputs.Button", AssemblyName = "Lib",
                Parameters = [P("Variant"), P("Disabled"), P("OnClick", ParameterKind.EventCallback), P("ChildContent", ParameterKind.ChildContent), P("Attributes", ParameterKind.CapturedAttributes)],
            },
            new ComponentDto
            {
                Id = "data-grid", Title = "Data/Grid", Name = "Grid<TItem>", FullName = "Lib.Data.Grid<TItem>", AssemblyName = "Lib", IsGeneric = true,
                Parameters = [P("Items"), P("Header", ParameterKind.RenderFragment), P("Row", ParameterKind.Template), P("Value"), P("ValueChanged", ParameterKind.EventCallback)],
            },
            new ComponentDto
            {
                Id = "legacy-banner", Title = "Legacy/Banner", Name = "Banner", FullName = "Lib.Legacy.Banner", AssemblyName = "Lib", Status = ComponentStatus.Deprecated,
            },
            new ComponentDto { Id = "display-badge", Title = "Display/Badge", Name = "Badge", FullName = "Lib.Display.Badge", AssemblyName = "Lib" },
        ],
    };

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Counts_tags_files_and_parameters()
    {
        Write("App/Pages/Home.razor", """
            @page "/"
            <button class="native">Not a component</button>
            <Button Variant="primary" OnClick="Save" @onclick:preventDefault="true" class="wide">Save</Button>
            <Button Disabled="@(_count > 3 ? "yes" : "no")" @ref="_button" />
            <!-- <Button Variant="commented" /> -->
            <Banner />
            """);
        Write("App/Shared/List.razor", """
            <Grid TItem="Product" Items="products" @bind-Value="selected" @bind-Value:after="Changed">
                <Header><Button>Inside a fragment</Button></Header>
                <Row Context="p"><td>@p.Name</td></Row>
            </Grid>
            <Lib.Inputs.Button
                Variant="ghost">Qualified</Lib.Inputs.Button>
            """);
        Write("App/obj/Debug/Generated.razor", "<Button Variant=\"skipped\" />");

        var report = ComponentUsage.Scan(Catalog(), [_root]);

        Assert.Equal(2, report.ScannedFiles);
        var button = report.Components.Single(c => c.Id == "inputs-button");
        Assert.Equal(4, button.Uses);
        Assert.Equal(2, button.Files);
        Assert.Equal(3, button.Locations[0].Line);
        Assert.Equal("App/Pages/Home.razor", button.Locations[0].File);
        Assert.Equal(5, button.Locations[^1].Line); // the qualified tag spans two lines and starts on line 5
        Assert.Equal(2, button.Parameters["Variant"]);
        Assert.Equal(1, button.Parameters["Disabled"]);
        Assert.Equal(1, button.Parameters["OnClick"]);
        Assert.Equal(3, button.Parameters["ChildContent"]);
        Assert.False(button.Parameters.ContainsKey("Attributes"));
        Assert.Equal(1, button.OtherAttributes["class"]);
        Assert.False(button.OtherAttributes.ContainsKey("_count")); // the quoted expression is skipped as a whole

        var grid = report.Components.Single(c => c.Id == "data-grid");
        Assert.Equal(1, grid.Uses);
        Assert.Equal(1, grid.Parameters["Items"]);
        Assert.Equal(1, grid.Parameters["Value"]);
        Assert.Equal(1, grid.Parameters["Header"]);
        Assert.Equal(1, grid.Parameters["Row"]);
        Assert.Equal(0, grid.Parameters["ValueChanged"]);
        Assert.Empty(grid.OtherAttributes); // TItem is a type argument

        Assert.Equal(1, report.Components.Single(c => c.Id == "legacy-banner").Uses);
        Assert.Equal(["display-badge"], report.Unused.Select(u => u.Id));
        Assert.Equal("inputs-button", report.Components[0].Id); // most used first
    }

    [Fact]
    public void Excludes_globs_and_marks_ambiguous_tag_names()
    {
        Write("App/Home.razor", "<Badge />");
        Write("Tests/Fixture.razor", "<Badge /><Badge />");
        var catalog = Catalog();
        catalog = new CatalogDto
        {
            Components = [.. catalog.Components, new ComponentDto { Id = "other-badge", Title = "Other/Badge", Name = "Badge", FullName = "Other.Badge", AssemblyName = "Other" }],
        };

        var report = ComponentUsage.Scan(catalog, [_root], ["Tests/*"]);

        Assert.Equal(1, report.ScannedFiles);
        var badges = report.Components.Where(c => c.Name == "Badge").ToList();
        Assert.All(badges, b => Assert.True(b.Ambiguous));
        Assert.All(badges, b => Assert.Equal(1, b.Uses));
    }

    [Fact]
    public void Survives_unterminated_markup()
    {
        Write("Broken.razor", "<Button Variant=\"open");
        var report = ComponentUsage.Scan(Catalog(), [_root]);
        Assert.Equal(1, report.ScannedFiles);
        Assert.Equal(0, report.Components.Single(c => c.Id == "inputs-button").Uses);
    }
}
