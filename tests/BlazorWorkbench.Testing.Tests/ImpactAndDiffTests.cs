using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing.Imaging;

namespace BlazorWorkbench.Testing.Tests;

public class AntiAliasingTests
{
    private static PngImage Line(byte edgeGray)
    {
        // White image with a black vertical line at x=5 and an anti-aliased (gray) column at x=4.
        var image = new PngImage(10, 10);
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var o = (y * 10 + x) * 4;
            var v = x == 5 ? (byte)0 : x == 4 ? edgeGray : (byte)255;
            image.Pixels[o] = image.Pixels[o + 1] = image.Pixels[o + 2] = v;
            image.Pixels[o + 3] = 255;
        }

        return image;
    }

    [Fact]
    public void Anti_aliased_edge_changes_are_ignored_by_default()
    {
        var result = ImageComparer.Compare(Line(160), Line(60), 0.1);
        Assert.Equal(0, result.DiffPixels);
        Assert.Equal(10, result.AntiAliasedPixels);
        Assert.Equal(200, result.Diff.Pixels[(0 * 10 + 4) * 4 + 1]); // drawn yellow
    }

    [Fact]
    public void Anti_aliasing_can_be_included()
    {
        var result = ImageComparer.Compare(Line(160), Line(60), 0.1, ignoreAntiAliasing: false);
        Assert.Equal(10, result.DiffPixels);
    }

    [Fact]
    public void Real_changes_are_not_mistaken_for_anti_aliasing()
    {
        var baseline = Line(160);
        var actual = Line(160);
        for (var y = 2; y < 5; y++)
        for (var x = 7; x < 9; x++)
        {
            var o = (y * 10 + x) * 4;
            actual.Pixels[o + 1] = actual.Pixels[o + 2] = 0; // red block in a flat white area
        }

        var result = ImageComparer.Compare(baseline, actual, 0.1);
        Assert.Equal(6, result.DiffPixels);
        Assert.Equal(0, result.AntiAliasedPixels);
    }
}

public class ChangeImpactTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wb-impact").FullName;

    public void Dispose()
    {
        // git marks object files read-only.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private CatalogDto Catalog() => new()
    {
        Components =
        [
            new ComponentDto
            {
                Id = "inputs-button", Title = "Inputs/Button", Name = "Button", FullName = "Button", AssemblyName = "Lib", SourcePath = "lib/Inputs/Button.razor",
                Scenarios =
                [
                    new ScenarioDto { Id = "inputs-button--primary", ComponentId = "inputs-button", Key = "primary", Name = "Primary", SourcePath = "host/Scenarios/ButtonScenarios.cs" },
                    new ScenarioDto { Id = "inputs-button--danger", ComponentId = "inputs-button", Key = "danger", Name = "Danger", SourcePath = "host/Scenarios/ButtonScenarios.cs" },
                ],
            },
            new ComponentDto
            {
                Id = "data-table", Title = "Data/Table", Name = "Table", FullName = "Table", AssemblyName = "Lib", SourcePath = "lib/Data/Table.razor",
                Scenarios = [new ScenarioDto { Id = "data-table--empty", ComponentId = "data-table", Key = "empty", Name = "Empty", SourcePath = "host/Scenarios/TableScenarios.cs" }],
            },
        ],
    };

    private ChangeSelection Select(params string[] relative) =>
        ChangeImpact.Select(Catalog(), relative.Select(r => Path.Combine(_root, r)), _root, Path.Combine(_root, "baselines"));

    [Theory]
    [InlineData("lib/Inputs/Button.razor")]
    [InlineData("lib/Inputs/Button.razor.cs")]
    [InlineData("lib/Inputs/Button.razor.css")]
    [InlineData("host/Scenarios/ButtonScenarios.cs")]
    public void Component_and_scenario_files_select_their_scenarios(string file)
    {
        var selection = Select(file);
        Assert.False(selection.RunAll);
        Assert.Equal(["inputs-button--danger", "inputs-button--primary"], selection.ScenarioIds!.Order());
    }

    [Fact]
    public void Baseline_changes_select_their_scenario() =>
        Assert.Equal(["data-table--empty"], Select("baselines/linux/data-table--empty/laptop-light.png").ScenarioIds!);

    [Fact]
    public void Ignored_files_select_nothing()
    {
        var selection = Select("README.md", "docs/guide.md", ".github/workflows/ci.yml", "baselines/manifest.json");
        Assert.False(selection.RunAll);
        Assert.Empty(selection.ScenarioIds!);
    }

    [Fact]
    public void Unattributable_changes_run_everything()
    {
        var selection = Select("lib/Inputs/Button.razor", "lib/Shared/Theme.css");
        Assert.True(selection.RunAll);
        Assert.Equal(["lib/Shared/Theme.css"], selection.UnmappedFiles);
    }

    [Fact]
    public void Git_changes_include_committed_staged_and_untracked_files()
    {
        static void Git(string dir, params string[] args)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var p = System.Diagnostics.Process.Start(start)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
        }

        Git(_root, "init", "-q");
        Git(_root, "config", "user.email", "test@example.com");
        Git(_root, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "1");
        Git(_root, "add", ".");
        Git(_root, "commit", "-qm", "initial");
        Git(_root, "tag", "base");

        File.WriteAllText(Path.Combine(_root, "a.txt"), "2");                 // modified
        File.WriteAllText(Path.Combine(_root, "b.txt"), "new");               // staged
        Git(_root, "add", "b.txt");
        Directory.CreateDirectory(Path.Combine(_root, "lib"));
        File.WriteAllText(Path.Combine(_root, "lib", "C.razor"), "<p />");    // untracked

        var changed = ChangeImpact.GitChangedFiles(_root, "base").Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/')).ToList();
        Assert.Equal(["a.txt", "b.txt", "lib/C.razor"], changed);
    }

    [Fact]
    public void Files_outside_the_root_are_ignored() =>
        Assert.Empty(ChangeImpact.Select(Catalog(), [Path.Combine(Path.GetTempPath(), "elsewhere.cs")], _root).ScenarioIds!);
}
