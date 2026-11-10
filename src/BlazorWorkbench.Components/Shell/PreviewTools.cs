using System.Globalization;

namespace BlazorWorkbench.Components.Shell;

/// <summary>
/// State of the canvas toolbar: how the preview is shown (zoom, background, rotation), visual aids (grid, outline, measure),
/// accessibility simulations (vision, text scale, text spacing, tab order), forced pseudo-states and the design overlay.
/// Kept in the Workbench URL so a link reproduces the view; the preview-side part also travels in the preview URL.
/// </summary>
public sealed record PreviewTools
{
    public static PreviewTools None { get; } = new();

    /// <summary>Zoom levels offered by the toolbar.</summary>
    public static IReadOnlyList<double> ZoomLevels { get; } = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3];

    /// <summary>Text scale percentages offered by the toolbar (WCAG 1.4.4 asks for 200% without loss of content).</summary>
    public static IReadOnlyList<int> TextScales { get; } = [100, 125, 150, 200];

    /// <summary>Pseudo-classes the toolbar can force, by key.</summary>
    public static IReadOnlyList<(string Key, string Label)> PseudoStates { get; } =
        [("hover", ":hover"), ("focus", ":focus"), ("focus-visible", ":focus-visible"), ("focus-within", ":focus-within"), ("active", ":active")];

    /// <summary>Vision simulations (color vision deficiencies and blur), by key.</summary>
    public static IReadOnlyList<(string Key, string Label)> VisionModes { get; } =
    [
        ("protanopia", "Protanopia (no red)"), ("protanomaly", "Protanomaly (weak red)"),
        ("deuteranopia", "Deuteranopia (no green)"), ("deuteranomaly", "Deuteranomaly (weak green)"),
        ("tritanopia", "Tritanopia (no blue)"), ("tritanomaly", "Tritanomaly (weak blue)"),
        ("achromatopsia", "Achromatopsia (no color)"), ("achromatomaly", "Achromatomaly (weak color)"),
        ("blurred", "Blurred vision"), ("low-contrast", "Reduced contrast"),
    ];

    public double Zoom { get; init; } = 1;

    /// <summary>Id of a <see cref="BackgroundPreset"/>, or null for the theme's background.</summary>
    public string? Background { get; init; }

    public bool Grid { get; init; }
    public bool Outline { get; init; }
    public bool Measure { get; init; }
    public IReadOnlyList<string> Pseudo { get; init; } = [];
    public int TextScale { get; init; } = 100;
    public bool TextSpacing { get; init; }
    public bool TabOrder { get; init; }
    public bool Updates { get; init; }
    public string? Vision { get; init; }

    /// <summary>Swaps the width and height of a fixed viewport (landscape).</summary>
    public bool Rotate { get; init; }

    /// <summary>URL of a design image drawn over the preview.</summary>
    public string? Overlay { get; init; }

    public int OverlayOpacity { get; init; } = 50;

    /// <summary><c>normal</c> or <c>difference</c> (identical pixels turn black).</summary>
    public string OverlayBlend { get; init; } = "normal";

    /// <summary>Number of tools that change the preview, for the toolbar badge.</summary>
    public int ActiveCount =>
        (Background is null ? 0 : 1) + (Grid ? 1 : 0) + (Outline ? 1 : 0) + (Measure ? 1 : 0) + Pseudo.Count + (TextScale != 100 ? 1 : 0) +
        (TextSpacing ? 1 : 0) + (TabOrder ? 1 : 0) + (Updates ? 1 : 0) + (Vision is null ? 0 : 1) + (Overlay is null ? 0 : 1);

    /// <summary>The CSS filter applied to the preview frame for <see cref="Vision"/>.</summary>
    public string? VisionFilter => Vision switch
    {
        null => null,
        "blurred" => "blur(2px)",
        "low-contrast" => "contrast(0.6)",
        _ when VisionModes.Any(v => v.Key == Vision) => $"url(#wb-vision-{Vision})",
        _ => null,
    };

    /// <summary>Workbench URL query parameters (only non-default values).</summary>
    public IEnumerable<string> ToQuery()
    {
        if (Zoom != 1) yield return "zoom=" + Zoom.ToString("0.##", CultureInfo.InvariantCulture);
        if (Background is not null) yield return "bg=" + Uri.EscapeDataString(Background);
        if (Rotate) yield return "rotate=1";
        if (Vision is not null) yield return "vision=" + Uri.EscapeDataString(Vision);
        foreach (var part in PreviewQuery(background: null))
            yield return part;
    }

    /// <summary>Preview URL query parameters (the tools the preview document applies itself).</summary>
    /// <param name="background">The CSS value of the selected background.</param>
    public IEnumerable<string> PreviewQuery(string? background)
    {
        if (background is not null) yield return "bg=" + Uri.EscapeDataString(background);
        if (Grid) yield return "grid=1";
        if (Outline) yield return "outline=1";
        if (Measure) yield return "measure=1";
        if (Pseudo.Count > 0) yield return "pseudo=" + Uri.EscapeDataString(string.Join(',', Pseudo));
        if (TextScale != 100) yield return "text=" + TextScale.ToString(CultureInfo.InvariantCulture);
        if (TextSpacing) yield return "spacing=1";
        if (TabOrder) yield return "tabs=1";
        if (Updates) yield return "updates=1";
        if (Overlay is not null)
        {
            yield return "overlay=" + Uri.EscapeDataString(Overlay);
            if (OverlayOpacity != 50) yield return "overlayOpacity=" + OverlayOpacity.ToString(CultureInfo.InvariantCulture);
            if (OverlayBlend != "normal") yield return "overlayBlend=" + Uri.EscapeDataString(OverlayBlend);
        }
    }

    /// <summary>The message sent to the preview document when a tool changes.</summary>
    public object ToMessage(string? background) => new
    {
        bg = background,
        grid = Grid,
        outline = Outline,
        measure = Measure,
        pseudo = Pseudo,
        text = TextScale,
        spacing = TextSpacing,
        tabs = TabOrder,
        updates = Updates,
        overlay = Overlay is null ? null : new { url = Overlay, opacity = OverlayOpacity, blend = OverlayBlend },
    };

    /// <summary>Reads the tools from Workbench URL query values. Unknown or malformed values are ignored.</summary>
    public static PreviewTools Parse(Func<string, string?> query)
    {
        static bool Flag(string? v) => v is "1" or "true";
        var zoom = double.TryParse(query("zoom"), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) && z is >= 0.1 and <= 4 ? z : 1;
        var text = int.TryParse(query("text"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t is >= 50 and <= 400 ? t : 100;
        var opacity = int.TryParse(query("overlayOpacity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) && o is >= 0 and <= 100 ? o : 50;
        var pseudo = (query("pseudo") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => PseudoStates.Any(s => s.Key == p)).Distinct().ToArray();
        var vision = query("vision");
        return new PreviewTools
        {
            Zoom = zoom,
            Background = query("bg") is { Length: > 0 } bg ? bg : null,
            Grid = Flag(query("grid")),
            Outline = Flag(query("outline")),
            Measure = Flag(query("measure")),
            Pseudo = pseudo,
            TextScale = text,
            TextSpacing = Flag(query("spacing")),
            TabOrder = Flag(query("tabs")),
            Updates = Flag(query("updates")),
            Vision = VisionModes.Any(v => v.Key == vision) ? vision : null,
            Rotate = Flag(query("rotate")),
            Overlay = query("overlay") is { Length: > 0 } overlay && Catalog.ScenarioValidator.IsSafeDesignUrl(overlay) ? overlay : null,
            OverlayOpacity = opacity,
            OverlayBlend = query("overlayBlend") == "difference" ? "difference" : "normal",
        };
    }

    /// <summary>Color matrices of the vision simulations (SVG <c>feColorMatrix</c> values).</summary>
    public static IReadOnlyDictionary<string, string> VisionMatrices { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["protanopia"] = "0.567 0.433 0 0 0  0.558 0.442 0 0 0  0 0.242 0.758 0 0  0 0 0 1 0",
        ["protanomaly"] = "0.817 0.183 0 0 0  0.333 0.667 0 0 0  0 0.125 0.875 0 0  0 0 0 1 0",
        ["deuteranopia"] = "0.625 0.375 0 0 0  0.7 0.3 0 0 0  0 0.3 0.7 0 0  0 0 0 1 0",
        ["deuteranomaly"] = "0.8 0.2 0 0 0  0.258 0.742 0 0 0  0 0.142 0.858 0 0  0 0 0 1 0",
        ["tritanopia"] = "0.95 0.05 0 0 0  0 0.433 0.567 0 0  0 0.475 0.525 0 0  0 0 0 1 0",
        ["tritanomaly"] = "0.967 0.033 0 0 0  0 0.733 0.267 0 0  0 0.183 0.817 0 0  0 0 0 1 0",
        ["achromatopsia"] = "0.299 0.587 0.114 0 0  0.299 0.587 0.114 0 0  0.299 0.587 0.114 0 0  0 0 0 1 0",
        ["achromatomaly"] = "0.618 0.32 0.062 0 0  0.163 0.775 0.062 0 0  0.163 0.32 0.516 0 0  0 0 0 1 0",
    };
}
