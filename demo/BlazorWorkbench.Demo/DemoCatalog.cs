using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Editors;

namespace BlazorWorkbench.Demo;

/// <summary>
/// Catalog configuration shared by every demo host (Interactive Server, WebAssembly and .NET MAUI Blazor Hybrid), so the
/// three hosts show the same components, editors and themes. Each host adds its own title and stylesheets.
/// </summary>
public static class DemoCatalog
{
    public static void Configure(WorkbenchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Components come from the library; scenarios come from the host's own assembly (discovered automatically).
        options.AddAssemblyOf<Button>();

        // A custom editor in the Controls panel for every HexColor parameter.
        options.AddParameterEditor<HexColor, HexColorEditor>();

        // A theme of the component library besides the built-in light and dark ones. It appears in the toolbar and sets
        // data-theme="high-contrast" on the preview, which demo.css styles.
        options.Themes.Add(new ThemeDefinition("high-contrast", ColorScheme.Dark));
    }
}
