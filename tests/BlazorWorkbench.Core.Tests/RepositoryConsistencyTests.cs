using System.Text.Json;
using System.Xml.Linq;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Guards facts that live in several files and must change together.</summary>
public class RepositoryConsistencyTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BlazorWorkbench.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Template_defaults_to_the_current_package_version()
    {
        var root = Root();
        var version = XDocument.Load(Path.Combine(root, "Directory.Build.props")).Descendants("Version").First().Value;
        using var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "BlazorWorkbench.Templates", "content", "BlazorWorkbenchHost", ".template.config", "template.json")));
        var templateVersion = template.RootElement.GetProperty("symbols").GetProperty("WorkbenchVersion").GetProperty("defaultValue").GetString();
        Assert.Equal(version, templateVersion);
    }

    [Fact]
    public void Every_shipping_project_is_in_the_solution()
    {
        var root = Root();
        var solution = File.ReadAllText(Path.Combine(root, "BlazorWorkbench.slnx"));
        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}content{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var relative = Path.GetRelativePath(root, project).Replace('\\', '/');
            Assert.Contains(relative, solution);
        }
    }
}
