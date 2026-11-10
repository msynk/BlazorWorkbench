using BlazorWorkbench;
using Microsoft.AspNetCore.Components;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Display;
using BlazorWorkbench.Demo.Components.Feedback;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Components.Layout;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class BadgeScenarios : ScenarioSet<Badge>
{
    protected override void Configure(ScenarioSetBuilder<Badge> set)
    {
        set.Title("Display/Badge").Description("A small status label. Pair the tone with text: color alone never carries meaning.");
        set.Documentation("""
            - *Custom accent*: `Accent` is a `HexColor`, and the host registers a color picker for that type with
              `options.AddParameterEditor<HexColor, HexColorEditor>()`. Open **Controls** to use it.
            - The *Tone ...* scenarios are not written by hand: `ToneMatrixProvider`, an `IScenarioProvider`, generates one per
              value of the `Tone` enum, so a new tone is previewed and screenshotted the day it is added.
            """);
        set.Add("Neutral", s => s.Parameter(b => b.Text, "Draft"));
        set.Add("Success", s => s.Parameter(b => b.Text, "Published").Parameter(b => b.Tone, Tone.Success));
        set.Add("With count", s => s.Parameter(b => b.Text, "Inbox").Parameter(b => b.Tone, Tone.Info).Parameter(b => b.Count, 7));
        set.Add("Overflowing count", s => s.Parameter(b => b.Text, "Notifications").Parameter(b => b.Tone, Tone.Danger).Parameter(b => b.Count, 1250));
        set.Add("Custom accent", s => s
            .Parameter(b => b.Text, "Beta")
            .Parameter(b => b.Accent, new HexColor("#a21caf"))
            .Description("Accent uses a custom parameter editor (a color picker) registered by the Workbench host."));
    }
}

public sealed class CardScenarios : ScenarioSet<Card>
{
    /// <summary>Two icon-only buttons; with <paramref name="labeled"/> false they have no accessible name.</summary>
    private static RenderFragment IconActions(bool labeled) => builder =>
    {
        builder.OpenComponent<Button>(0);
        builder.AddComponentParameter(1, nameof(Button.Variant), ButtonVariant.Secondary);
        builder.AddComponentParameter(2, nameof(Button.Icon), "✎");
        if (labeled) builder.AddComponentParameter(3, "aria-label", "Edit report");
        builder.CloseComponent();
        builder.OpenComponent<Button>(4);
        builder.AddComponentParameter(5, nameof(Button.Variant), ButtonVariant.Danger);
        builder.AddComponentParameter(6, nameof(Button.Icon), "🗑");
        if (labeled) builder.AddComponentParameter(7, "aria-label", "Delete report");
        builder.CloseComponent();
    };

    protected override void Configure(ScenarioSetBuilder<Card> set)
    {
        set.Title("Display/Card").Description("A surface that groups related content, with an optional heading and footer.").Tags("layout", "accessibility");
        set.Documentation("""
            `Footer` and `ChildContent` are render fragments, so scenarios pass real components into them, here the library's
            own `Button`.

            **Do and don't.** Scenarios can also document misuse. *Icon actions, labeled* and *Don't: icon actions without
            names* render the same card; only the second forgets the `aria-label` on its icon-only buttons. Open it, then
            **Accessibility → Run scan**: axe-core reports `button-name` (critical) for both buttons. Automated runs skip the
            scan for this one scenario with `.Accessibility(a => a.Skip(...))`, so the deliberate mistake does not fail CI, and
            the reason shows up in every report.
            """);
        set.Add("Text content", s => s.Parameter(c => c.Title, "Project Apollo").ChildContent("Launch window opens in three days. All systems nominal."));
        set.Add("With footer", s => s
            .Parameter(c => c.Title, "Delete project?")
            .Parameter(c => c.Elevated, true)
            .ChildContent("This permanently removes the project and its history.")
            .Fragment(c => c.Footer, builder =>
            {
                builder.OpenComponent<Button>(0);
                builder.AddComponentParameter(1, nameof(Button.Variant), ButtonVariant.Secondary);
                builder.AddComponentParameter(2, nameof(Button.ChildContent), (RenderFragment)(b => b.AddContent(0, "Cancel")));
                builder.CloseComponent();
                builder.OpenComponent<Button>(3);
                builder.AddComponentParameter(4, nameof(Button.Variant), ButtonVariant.Danger);
                builder.AddComponentParameter(5, nameof(Button.ChildContent), (RenderFragment)(b => b.AddContent(0, "Delete")));
                builder.CloseComponent();
            }, "Cancel + Delete buttons"));
        set.Add("Without title", s => s.ChildContent("A card without a heading is not a labelled region."));

        set.Add("Icon actions, labeled", s => s
            .Parameter(c => c.Title, "Lake loop trail report")
            .ChildContent("Muddy after the rain, but the bridge is open again.")
            .Fragment(c => c.Footer, IconActions(labeled: true), "Edit + Delete icon buttons with aria-label")
            .Description("Icon-only buttons with an aria-label: screen readers announce Edit report and Delete report.")
            .Interaction("icon buttons have accessible names", i => i
                .ExpectVisible(By.Role("button", "Edit report"))
                .ExpectVisible(By.Role("button", "Delete report"))));

        set.Add("Don't: icon actions without names", s => s
            .Key("dont-icon-actions-without-names")
            .Tags("dont")
            .Parameter(c => c.Title, "Lake loop trail report")
            .ChildContent("Muddy after the rain, but the bridge is open again.")
            .Fragment(c => c.Footer, IconActions(labeled: false), "Edit + Delete icon buttons without aria-label")
            .Description("Deliberate misuse: the icon-only buttons have no accessible name. Open Accessibility and press Run scan.")
            .Accessibility(a => a.Skip("Deliberately inaccessible example of misuse; run the scan in the Accessibility panel to see the findings.")));
    }
}

public sealed class DateLabelScenarios : ScenarioSet<DateLabel>
{
    private static readonly DateTimeOffset Now = new(2026, 3, 20, 9, 30, 0, TimeSpan.Zero);

    protected override void Configure(ScenarioSetBuilder<DateLabel> set)
    {
        set.Title("Display/Date label").Tags("localization");
        set.Documentation("Relative text uses the injected `TimeProvider`, so scenarios freeze the clock for deterministic output.");
        set.Setup(s => s.Clock(Now).Parameter(d => d.Value, Now.AddHours(-3)));

        set.Add("English", s => s.Culture("en-US"));
        set.Add("German", s => s.Culture("de-DE"));
        set.Add("Persian (RTL)", s => s.Culture("fa-IR").Parameter(d => d.Value, Now.AddDays(-2)));
        set.Add("Japanese long format", s => s.Culture("ja-JP").Parameter(d => d.Format, "F"));
    }
}

public sealed class AlertScenarios : ScenarioSet<Alert>
{
    protected override void Configure(ScenarioSetBuilder<Alert> set)
    {
        set.Title("Feedback/Alert").Description("An inline message. Errors and warnings are announced immediately, other messages politely.").Tags("feedback", "live-region");
        set.Documentation("Run the scan in **Accessibility**: errors and warnings use `role=\"alert\"`, other messages `role=\"status\"`. *Error dismissible* tests that **Dismiss** removes the alert and raises `OnDismiss`.");
        set.Add("Info", s => s.Parameter(a => a.Message, "A new version is available."));
        set.Add("Success", s => s.Parameter(a => a.Severity, AlertSeverity.Success).Parameter(a => a.Message, "Your changes were saved."));
        set.Add("Error dismissible", s => s
            .Parameter(a => a.Severity, AlertSeverity.Error)
            .Parameter(a => a.Message, "Payment failed. Check your card details.")
            .Parameter(a => a.Dismissible, true)
            .Interaction("dismiss removes the alert", i => i
                .ExpectVisible(By.Role("alert"))
                .Click(By.Role("button", "Dismiss"))
                .ExpectHidden(By.Role("alert"))
                .ExpectCallback("OnDismiss", times: 1)));
    }
}

public sealed class FlakyWidgetScenarios : ScenarioSet<FlakyWidget>
{
    protected override void Configure(ScenarioSetBuilder<FlakyWidget> set)
    {
        set.Title("Feedback/Flaky widget")
           .Tags("diagnostics")
           .Description("Demonstrates how Workbench reports render and event-handler exceptions.")
           .Documentation("""
               - *Throws while rendering*: instead of a blank page, the preview shows the exception and the scenario is marked
                 **Error**. **Diagnostics** records it with the scenario id and a timestamp, with secrets redacted.
               - *Healthy*: click the button to throw from an event handler; the error lands in **Diagnostics** too.
               - The broken scenario uses `.SkipTests(...)`: it stays in the catalog for demonstration, and test runs report it
                 as skipped with the reason.
               """);
        set.Add("Healthy");
        set.Add("Throws while rendering", s => s
            .Parameter(f => f.ThrowOnRender, true)
            .SkipTests("Intentionally fails; used to demonstrate error diagnostics."));
    }
}

public sealed class ToolbarScenarios : ScenarioSet<Toolbar>
{
    protected override void Configure(ScenarioSetBuilder<Toolbar> set)
    {
        set.Title("Layout/Toolbar").Description("A horizontal toolbar that mirrors correctly in right-to-left layouts.").Tags("rtl", "navigation");
        set.Documentation("*Right to left* sets `.Culture(\"ar-SA\")`, and Workbench derives `dir=\"rtl\"` from it. Any scenario can be flipped with **Direction** in the toolbar, and the RTL scenario is screenshotted at two viewports.");
        set.Setup(s => s
            .Parameter(t => t.Items, (IReadOnlyList<string>)["Overview", "Activity", "Settings"])
            .Parameter(t => t.Active, "Activity")
            .Fragment(t => t.EndContent, "3 unread"));

        set.Add("Left to right", s => s.Interaction("selecting an item", i => i
            .Click(By.Role("link", "Settings"))
            .ExpectCallback("OnSelect", times: 1)));
        set.Add("Right to left", s => s
            .Culture("ar-SA")
            .Parameter(t => t.Items, (IReadOnlyList<string>)["نظرة عامة", "النشاط", "الإعدادات"])
            .Parameter(t => t.Active, "النشاط")
            .Fragment(t => t.EndContent, "٣ غير مقروءة")
            .Visual(v => v.Viewports("mobile", "laptop")));
    }
}

public sealed class SortableListScenarios : ScenarioSet<SortableList>
{
    private static readonly IReadOnlyList<string> Tasks = ["Write spec", "Build prototype", "Ship it"];

    protected override void Configure(ScenarioSetBuilder<SortableList> set)
    {
        set.Title("Layout/Sortable list").Description("A list the user reorders with drag and drop or with Alt+Arrow keys.").Tags("drag-and-drop", "keyboard");
        set.Documentation("Interaction tests can drag (`DragTo(...)`) as well as click and type. The keyboard test checks the accessible alternative to dragging.");
        set.Add("Default", s => s
            .Parameter(l => l.Items, Tasks)
            .Interaction("drag to reorder", i => i
                .DragTo(By.Text("Ship it"), By.Text("Write spec"))
                .ExpectText(By.Css(".sortable-item:first-child"), "Ship it")
                .ExpectCallback("OnReordered", times: 1))
            .Interaction("keyboard reorder", i => i
                .Focus(By.Text("Write spec"))
                .Press("Alt+ArrowDown")
                .ExpectText(By.Css(".sortable-item:first-child"), "Build prototype")));
    }
}
