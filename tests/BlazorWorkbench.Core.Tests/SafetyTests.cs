using BlazorWorkbench.Catalog;

namespace BlazorWorkbench.Core.Tests;

public class SafetyTests
{
    [Theory]
    [InlineData("origin/main", true)]
    [InlineData("main", true)]
    [InlineData("v1.2.0", true)]
    [InlineData("HEAD~3", true)]
    [InlineData("HEAD^", true)]
    [InlineData("@{u}", true)]
    [InlineData("feature/ünïcode", true)]
    [InlineData("0123abcd", true)]
    [InlineData("", false)]
    [InlineData("-p", false)]
    [InlineData("--output=/tmp/x", false)]
    [InlineData("--upload-pack=evil", false)]
    [InlineData("main..HEAD", false)]
    [InlineData("main...HEAD", false)]
    [InlineData("main HEAD", false)]
    [InlineData("main\tx", false)]
    [InlineData("main\nx", false)]
    [InlineData("main\0x", false)]
    public void Git_revisions_are_validated(string revision, bool expected) => Assert.Equal(expected, GitWorkingCopy.IsSafeRevision(revision));

    [Fact]
    public async Task Option_like_base_refs_are_never_passed_to_git()
    {
        // The repository root is a git working copy; the base ref is rejected before git runs either way.
        var (_, description) = await GitWorkingCopy.ReadAsync(AppContext.BaseDirectory, "--output=pwned.txt", Xunit.TestContext.Current.CancellationToken);
        Assert.DoesNotContain("commits since", description, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "pwned.txt")));
        var (paths, error) = await GitWorkingCopy.ReadChangedPathsAsync(AppContext.BaseDirectory, "-p", Xunit.TestContext.Current.CancellationToken);
        Assert.Null(paths);
        Assert.NotNull(error);
    }

    [Fact]
    public void Path_containment_rejects_traversal_and_siblings()
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-root");
        Assert.True(PathSafety.IsInside(Path.Combine(root, "a", "b.png"), root));
        Assert.True(PathSafety.IsInside(Path.Combine(root, "b.png"), root + Path.DirectorySeparatorChar));
        Assert.False(PathSafety.IsInside(root, root));
        Assert.False(PathSafety.IsInside(Path.Combine(root, "..", "x.png"), root));
        Assert.False(PathSafety.IsInside(root + "-sibling" + Path.DirectorySeparatorChar + "x.png", root));
    }

    [Fact]
    public void Path_containment_follows_the_file_system_case_sensitivity()
    {
        var root = Path.Combine(Path.GetTempPath(), "wb-root");
        var differentCase = Path.Combine(Path.GetTempPath(), "WB-ROOT", "x.png");
        Assert.Equal(OperatingSystem.IsWindows(), PathSafety.IsInside(differentCase, root));
        Assert.Equal(OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, PathSafety.Comparison);
    }
}
