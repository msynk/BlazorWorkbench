using BlazorWorkbench.Components.Shell;

namespace BlazorWorkbench.Components.Tests;

/// <summary>Breakpoint marks on the canvas ruler, the range label of the viewport bar and the Compare view's widths.</summary>
public class ResponsiveLayoutTests
{
    private static readonly IReadOnlyList<BreakpointMark> Marks = ResponsiveLayout.Merge(
        [new BreakpointPreset("md", 768), new BreakpointPreset("xl", 1280)],
        [new DetectedBreakpoint(768, 3, 1), new DetectedBreakpoint(641, 0, 2)]);

    [Fact]
    public void Declared_breakpoints_name_detected_ones()
    {
        Assert.Equal([641, 768, 1280], Marks.Select(m => m.Width));
        Assert.Equal(new BreakpointMark(768, "md", 3, 1), Marks[1]);
        Assert.Equal("md 768", Marks[1].Label);
        Assert.Equal("641", Marks[0].Label);
        Assert.False(Marks[2].Detected);
        Assert.Equal("3 min-width queries, 1 max-width query, declared in options.Breakpoints", Marks[1].Source);
    }

    [Theory]
    [InlineData(320, "< 641")]
    [InlineData(700, "641–767")]
    [InlineData(768, "md · 768–1279")]
    [InlineData(1279, "md · 768–1279")]
    [InlineData(1440, "xl · ≥ 1280")]
    public void The_range_of_a_width_is_named_after_its_breakpoint(int width, string expected) =>
        Assert.Equal(expected, ResponsiveLayout.Describe(Marks, width));

    [Fact]
    public void Without_breakpoints_there_is_no_range() => Assert.Null(ResponsiveLayout.Describe([], 500));

    [Fact]
    public void Compare_shows_the_reflow_width_and_both_edges_of_every_breakpoint()
    {
        var widths = ResponsiveLayout.CompareWidths(Marks);
        Assert.Equal(["320", "640", "641", "767", "768", "1279", "1280"], widths.Select(w => w.Key));
        Assert.Equal("320 px (reflow)", widths[0].Label);
        Assert.Equal("768 px · md", widths[4].Label);
        Assert.Equal("767 px", widths[3].Label);
        Assert.Equal(3, ResponsiveLayout.CompareWidths(Marks, max: 3).Count);
    }

    [Fact]
    public void Only_overflow_from_elements_that_are_not_allowed_counts()
    {
        Assert.True(new LayoutReport(320, 640, 400, 80, false, []).Overflows);
        Assert.False(new LayoutReport(320, 640, 400, 80, true, []).Overflows);
        Assert.False(new LayoutReport(320, 640, 320, 0, false, null).Overflows);
    }
}
