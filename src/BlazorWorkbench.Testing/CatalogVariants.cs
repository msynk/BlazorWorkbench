using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing;

/// <summary>
/// The viewports and themes of a catalog, falling back to the Workbench defaults when a host clears its lists, so runs and
/// publishing never index into an empty list.
/// </summary>
internal static class CatalogVariants
{
    public static IReadOnlyList<ViewportPreset> Viewports(CatalogDto catalog) =>
        catalog.Viewports.Count > 0 ? catalog.Viewports : ViewportPreset.Defaults;

    public static IReadOnlyList<ThemeDto> Themes(CatalogDto catalog) =>
        catalog.Themes.Count > 0 ? catalog.Themes : ThemeDefinition.Defaults.Select(t => new ThemeDto(t.Name, t.ColorScheme, t.CssClass)).ToArray();

    /// <summary>The viewport named <paramref name="preferred"/> (case-insensitive), else the first one.</summary>
    public static ViewportPreset DefaultViewport(CatalogDto catalog, string? preferred = "laptop")
    {
        var viewports = Viewports(catalog);
        return viewports.FirstOrDefault(v => string.Equals(v.Name, preferred, StringComparison.OrdinalIgnoreCase)) ?? viewports[0];
    }

    public static ThemeDto DefaultTheme(CatalogDto catalog) => Themes(catalog)[0];
}
