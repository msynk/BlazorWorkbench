using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Editors;

namespace BlazorWorkbench.Demo;

/// <summary>
/// Catalog configuration shared by every demo host (Interactive Server, WebAssembly and .NET MAUI Blazor Hybrid), so the
/// three hosts show the same components, editors, themes and tour. Each host adds its stylesheets and a title suffix.
/// </summary>
public static class DemoCatalog
{
    /// <summary>The catalog title. Hosts other than Interactive Server append their hosting model.</summary>
    public const string Title = "Trailhead UI";

    public static void Configure(WorkbenchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Title = Title;

        // The overview page opens with a guided tour of what Workbench does, with links into the scenarios.
        options.Introduction = DemoTour.Markdown;

        // Components come from the library; scenarios come from the host's own assembly (discovered automatically).
        options.AddAssemblyOf<Button>();

        // A custom editor in the Controls panel for every HexColor parameter.
        options.AddParameterEditor<HexColor, HexColorEditor>();

        // A theme of the component library besides the built-in light and dark ones. It appears in the toolbar and sets
        // data-theme="high-contrast" on the preview, which demo.css styles.
        options.Themes.Add(new ThemeDefinition("high-contrast", ColorScheme.Dark));

        // Users for the toolbar's User menu: preview any scenario as one of them (?user=admin in links).
        options.AddUser("Viewer", u => u.Name("Ada Lovelace").Role("Viewer"));
        options.AddUser("Editor", u => u.Name("Linus Torvalds").Role("Editor"));
        options.AddUser("Admin", u => u.Name("Margaret Hamilton").Role("Admin"));
        options.AddUser("Owner on Pro", u => u.Name("Grace Hopper").Role("Owner").Claim("plan", "pro"));

        // A brand background for the canvas toolbar, next to white, gray, dark and transparent.
        options.Backgrounds.Add(new BackgroundPreset("Trail sand", "#f3ede2"));

        // Documentation pages listed in the explorer, with live scenarios embedded in their Markdown.
        options.AddPage("Guidelines/Buttons and actions", DemoPages.Buttons);
        options.AddPage("Guidelines/Forms", DemoPages.Forms);
        options.AddPage("Foundations/Design tokens", DemoPages.Tokens);
        options.AddPage("Foundations/Component status", DemoPages.Statuses);
    }
}
