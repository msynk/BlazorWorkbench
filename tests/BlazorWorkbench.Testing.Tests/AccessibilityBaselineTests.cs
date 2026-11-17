using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing.Tests;

public sealed class AccessibilityBaselineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wb-a11y-" + Guid.NewGuid().ToString("N"));

    private static AccessibilityFinding Finding(string rule, bool review = false, params string[] targets) =>
        new(rule, "serious", rule, null, [], targets, Blocking: !review, NeedsReview: review);

    [Fact]
    public void Approved_violations_cover_the_same_elements_only()
    {
        var count = AccessibilityBaseline.Save(_dir, "inputs-button--icon", [Finding("button-name", false, "#a", "#b"), Finding("color-contrast", review: true, "#c")]);
        Assert.Equal(2, count); // needs-review items are not approved

        var baseline = AccessibilityBaseline.Load(_dir, "inputs-button--icon")!;
        Assert.True(baseline.Covers(Finding("button-name", false, "#a")));
        Assert.True(baseline.Covers(Finding("button-name", false, "#b", "#a")));
        Assert.False(baseline.Covers(Finding("button-name", false, "#a", "#new")));
        Assert.False(baseline.Covers(Finding("image-alt", false, "#a")));
    }

    [Fact]
    public void Fixed_violations_are_reported_and_an_empty_baseline_is_removed()
    {
        AccessibilityBaseline.Save(_dir, "s", [Finding("button-name", false, "#a", "#b")]);
        var baseline = AccessibilityBaseline.Load(_dir, "s")!;
        Assert.Equal([new AccessibilityBaseline.KnownViolation("button-name", "#b")], baseline.Fixed([Finding("button-name", false, "#a")]));

        Assert.Equal(0, AccessibilityBaseline.Save(_dir, "s", []));
        Assert.False(File.Exists(AccessibilityBaseline.PathFor(_dir, "s")));
        Assert.Null(AccessibilityBaseline.Load(_dir, "s"));
    }

    [Fact]
    public void Applying_a_baseline_unblocks_known_violations_and_keeps_new_ones_blocking()
    {
        AccessibilityBaseline.Save(_dir, "s", [Finding("button-name", false, "#a"), Finding("label", false, "#gone")]);
        var findings = new List<AccessibilityFinding> { Finding("button-name", false, "#a"), Finding("image-alt", false, "#img"), Finding("color-contrast", review: true, "#c") };

        var (known, gone) = AccessibilityBaseline.Apply(AccessibilityBaseline.Load(_dir, "s"), findings);

        Assert.Equal((1, 1), (known, gone));
        Assert.True(findings[0].Baselined);
        Assert.False(findings[0].Blocking);
        Assert.True(findings[1].Blocking);
        Assert.False(findings[2].Baselined);
        Assert.Equal((0, 0), AccessibilityBaseline.Apply(null, findings));
    }

    [Fact]
    public void An_unreadable_baseline_counts_as_none()
    {
        var path = AccessibilityBaseline.PathFor(_dir, "broken");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Null(AccessibilityBaseline.Load(_dir, "broken"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
