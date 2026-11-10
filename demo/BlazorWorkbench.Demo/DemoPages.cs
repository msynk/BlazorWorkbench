namespace BlazorWorkbench.Demo;

/// <summary>
/// Standalone documentation pages of the demo library (<see cref="WorkbenchOptions.AddPage"/>). Directive lines such as
/// <c>{{scenario:id}}</c> embed live previews; a test fails if one of them points at a scenario or component that does not exist.
/// </summary>
public static class DemoPages
{
    public const string Buttons = """
        Trailhead has one primary action per view. Everything else is secondary, and destructive actions are red so they are
        never clicked by habit.

        ## The primary action

        Use it for the step that moves the customer forward: *Place order*, *Save changes*, *Continue*.

        {{scenario:inputs-button--primary}}

        ## Destructive actions

        Pair the danger variant with a confirmation (see the dialog) and a verb that says what is lost.

        {{scenario:inputs-button--danger}}

        ## Every variant in every size

        The matrix below is the *Variants* matrix of the button: one scenario per combination, each one screenshotted and scanned
        in CI. Open any of them in the **Gallery** view to see it as a table.

        {{scenarios:inputs-button}}

        ## Parameters

        The table lists which values the scenarios cover; a struck-through value is never shown by any scenario.

        {{parameters:inputs-button}}
        """;

    public const string Forms = """
        Forms validate on the client first and show the server's answer next to the field it concerns. Labels are always visible;
        placeholders show an example, never the label.

        ## A field and its states

        {{scenario:inputs-text-field--invalid}}

        {{scenario:inputs-text-field--keyboard-focus}}

        ## A complete form

        The sign-up form uses data annotations. Its interaction test fills it in and submits it; record your own in
        **Interactions → Record** and paste the generated C# into the scenario.

        {{scenario:forms-signup-form--empty}}
        """;

    public const string Tokens = """
        The tokens below are read from the demo's stylesheet (`demo.css`) and resolved in the selected theme. They are not declared
        anywhere for Workbench: any CSS custom property on the root element shows up, grouped as colors, typography, spacing,
        radii and shadows.

        {{tokens}}
        """;

    public const string Statuses = """
        Every component carries a lifecycle status, shown next to its name in the explorer and on its documentation page. Filter
        the explorer with `status:beta` (or the quick filters under the search box).

        | Status | Meaning |
        | --- | --- |
        | Draft | An idea or work in progress. Not ready for use. |
        | Experimental | Usable, but the API or the design may still change. |
        | Beta | Feature complete and in review. |
        | Stable | Ready for production; changes follow semantic versioning. |
        | Deprecated | Kept for compatibility. Do not use in new code. |

        Declare it with `set.Status(ComponentStatus.Beta)` in the scenario set. A component marked `.Deprecated("...")` is
        deprecated automatically.
        """;
}
