using BlazorWorkbench;
using Microsoft.AspNetCore.Components;
using SampleComponents;
using SampleComponents.Display;
using SampleComponents.Feedback;
using SampleComponents.Inputs;
using SampleComponents.Layout;

namespace SampleWorkbench.Scenarios;

public sealed class BadgeScenarios : ScenarioSet<Badge>
{
    protected override void Configure(ScenarioSetBuilder<Badge> set)
    {
        set.Title("Display/Badge");
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
    protected override void Configure(ScenarioSetBuilder<Card> set)
    {
        set.Title("Display/Card").Tags("layout");
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
        set.Title("Feedback/Alert").Tags("feedback", "live-region");
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
           .Description("Demonstrates how Workbench reports render and event-handler exceptions.");
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
        set.Title("Layout/Toolbar").Tags("rtl", "navigation");
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
