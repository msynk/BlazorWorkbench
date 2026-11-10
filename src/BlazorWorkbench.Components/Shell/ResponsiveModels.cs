using System.Globalization;

namespace BlazorWorkbench.Components.Shell;

/// <summary>
/// A width at which media queries of the preview stylesheets change the layout, reported by the preview: the first width,
/// in CSS pixels, of the new layout, with the number of <c>min-width</c> (or <c>&gt;</c>) and <c>max-width</c> (or
/// <c>&lt;</c>) conditions that change there.
/// </summary>
public sealed record DetectedBreakpoint(int Width, int Min, int Max);

/// <summary>An element that sticks out of the preview's viewport, so the page scrolls horizontally.</summary>
/// <param name="Selector">A CSS path to the element.</param>
/// <param name="Label">The element as a person would describe it (tag, role, accessible name).</param>
/// <param name="By">How far, in CSS pixels, it sticks out.</param>
/// <param name="Allowed">True when the scenario allows it to overflow (<c>.Responsive(r =&gt; r.AllowOverflow(...))</c>).</param>
public sealed record LayoutOffender(string Selector, string Label, double By, bool Allowed);

/// <summary>The preview's live layout: its viewport size and horizontal overflow.</summary>
/// <param name="Width">Viewport width, in CSS pixels, as media queries see it.</param>
/// <param name="Height">Viewport height, in CSS pixels.</param>
/// <param name="ContentWidth">Width of the page content.</param>
/// <param name="Overflow">How much wider than the viewport the content is; 0 when it fits.</param>
/// <param name="AllowedOnly">True when only allowed elements overflow.</param>
/// <param name="Offenders">The outermost elements that stick out (at most 8).</param>
public sealed record LayoutReport(double Width, double Height, double ContentWidth, double Overflow, bool AllowedOnly, LayoutOffender[]? Offenders)
{
    /// <summary>True when the page scrolls horizontally because of an element the scenario does not allow to overflow.</summary>
    public bool Overflows => Overflow >= 1 && !AllowedOnly;
}

/// <summary>A breakpoint on the canvas ruler: detected in the preview stylesheets, declared in options, or both.</summary>
/// <param name="Width">The first width, in CSS pixels, of the layout after the breakpoint.</param>
/// <param name="Name">The declared name (<c>options.Breakpoints</c>), if any.</param>
/// <param name="MinQueries">Detected <c>min-width</c> conditions that start applying at <paramref name="Width"/>.</param>
/// <param name="MaxQueries">Detected <c>max-width</c> conditions that stop applying at <paramref name="Width"/>.</param>
public sealed record BreakpointMark(int Width, string? Name, int MinQueries, int MaxQueries)
{
    public bool Detected => MinQueries + MaxQueries > 0;

    /// <summary>Short label: <c>md 768</c> or <c>768</c>.</summary>
    public string Label => Name is null ? Px(Width) : $"{Name} {Px(Width)}";

    /// <summary>Where the breakpoint comes from, for tooltips.</summary>
    public string Source
    {
        get
        {
            var parts = new List<string>();
            if (MinQueries > 0) parts.Add(MinQueries == 1 ? "1 min-width query" : $"{MinQueries} min-width queries");
            if (MaxQueries > 0) parts.Add(MaxQueries == 1 ? "1 max-width query" : $"{MaxQueries} max-width queries");
            if (Name is not null) parts.Add("declared in options.Breakpoints");
            return string.Join(", ", parts);
        }
    }

    internal static string Px(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Breakpoint helpers for the canvas ruler, the viewport bar and the Compare view.</summary>
public static class ResponsiveLayout
{
    /// <summary>Declared and detected breakpoints by width, ascending. A detected width that is declared gets its name.</summary>
    public static IReadOnlyList<BreakpointMark> Merge(IEnumerable<BreakpointPreset> declared, IEnumerable<DetectedBreakpoint> detected)
    {
        var marks = new SortedDictionary<int, BreakpointMark>();
        foreach (var d in detected.Where(d => d.Width is >= 1 and <= BreakpointPreset.MaxViewportSize))
            marks[d.Width] = marks.TryGetValue(d.Width, out var m) ? m with { MinQueries = m.MinQueries + d.Min, MaxQueries = m.MaxQueries + d.Max } : new BreakpointMark(d.Width, null, d.Min, d.Max);
        foreach (var p in declared.Where(p => p.MinWidth is >= 1 and <= BreakpointPreset.MaxViewportSize))
            marks[p.MinWidth] = marks.TryGetValue(p.MinWidth, out var m) ? m with { Name = m.Name ?? p.Name } : new BreakpointMark(p.MinWidth, p.Name, 0, 0);
        return marks.Values.ToArray();
    }

    /// <summary>
    /// The range of <paramref name="width"/> between breakpoints, for example <c>md · 768–1023</c>, <c>&lt; 640</c> or
    /// <c>xl · ≥ 1280</c>; null without breakpoints.
    /// </summary>
    public static string? Describe(IReadOnlyList<BreakpointMark> marks, int width)
    {
        if (marks.Count == 0)
            return null;
        var below = marks.LastOrDefault(m => m.Width <= width);
        var above = marks.FirstOrDefault(m => m.Width > width);
        var range = (below, above) switch
        {
            (null, { } a) => $"< {BreakpointMark.Px(a.Width)}",
            ({ } b, null) => $"≥ {BreakpointMark.Px(b.Width)}",
            ({ } b, { } a) => $"{BreakpointMark.Px(b.Width)}–{BreakpointMark.Px(a.Width - 1)}",
            _ => null,
        };
        return below?.Name is { } name ? $"{name} · {range}" : range;
    }

    /// <summary>
    /// Widths to compare side by side: 320 (WCAG reflow) when it comes first, then both edges of every breakpoint, each with
    /// a label. Capped at <paramref name="max"/> widths.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> CompareWidths(IReadOnlyList<BreakpointMark> marks, int max = 24)
    {
        var names = marks.Where(m => m.Name is not null).ToDictionary(m => m.Width, m => m.Name!);
        return BreakpointPreset.SweepWidths(marks.Select(m => m.Width)).Take(max)
            .Select(w => (BreakpointMark.Px(w), w == BreakpointPreset.ReflowWidth && !marks.Any(m => m.Width == w || m.Width - 1 == w)
                ? "320 px (reflow)"
                : $"{BreakpointMark.Px(w)} px" + (names.TryGetValue(w, out var name) ? $" · {name}" : "")))
            .ToArray();
    }
}
