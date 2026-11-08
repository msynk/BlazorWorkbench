using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Inputs;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class ButtonScenarios : ScenarioSet<Button>
{
    protected override void Configure(ScenarioSetBuilder<Button> set)
    {
        set.Title("Inputs/Button")
           .Tags("inputs")
           .Documentation("""
               Buttons trigger actions. Use **one** primary button per view.

               | Variant | Use for |
               | --- | --- |
               | `Primary` | The main action |
               | `Secondary` | Alternative actions |
               | `Danger` | Destructive actions |
               """);

        set.Add("Primary", s => s
            .ChildContent("Save changes")
            .Budget(readyWithin: TimeSpan.FromSeconds(3), maxDomUpdates: 20)
            .Interaction("click raises OnClick", i => i
                .Click(By.Role("button", "Save changes"))
                .ExpectCallback("OnClick", times: 1)
                .ExpectNoErrors()));

        set.Add("Secondary", s => s.Parameter(b => b.Variant, ButtonVariant.Secondary).ChildContent("Cancel"));
        set.Add("Danger", s => s.Parameter(b => b.Variant, ButtonVariant.Danger).Parameter(b => b.Icon, "🗑").ChildContent("Delete"));
        set.Add("Disabled", s => s
            .Parameter(b => b.Disabled, true)
            .ChildContent("Unavailable")
            .Interaction("disabled button ignores clicks", i => i
                .ExpectAttribute(By.Role("button", "Unavailable"), "disabled", "")
                .ExpectCallback("OnClick", times: 0)));
        set.Add("Loading", s => s
            .Parameter(b => b.Loading, true)
            .ChildContent("Saving…")
            .Description("The spinner animation is disabled during visual capture."));
        set.Add("Large icon-only", s => s
            .Parameter(b => b.Size, Size.Large)
            .Parameter(b => b.Icon, "★")
            .Parameter("aria-label", "Add to favorites")
            .Description("Icon-only buttons need an accessible name (aria-label)."));
    }
}

public sealed class CounterScenarios : ScenarioSet<Counter>
{
    protected override void Configure(ScenarioSetBuilder<Counter> set)
    {
        set.Title("Inputs/Counter").Description("A numeric stepper with optional bounds and arrow-key support.").Tags("inputs", "keyboard");

        set.Add("Default", s => s
            .Interaction("increment and decrement", i => i
                .Click(By.Role("button", "Increase"))
                .Click(By.Role("button", "Increase"))
                .ExpectText(By.TestId("count"), "2")
                .Click(By.Role("button", "Decrease"))
                .ExpectText(By.TestId("count"), "1")
                .ExpectCallback("OnChanged", times: 3))
            .Interaction("keyboard arrows", i => i
                .Focus(By.TestId("count"))
                .Press("ArrowUp")
                .Press("ArrowUp")
                .ExpectText(By.TestId("count"), "2")
                .ExpectFocused(By.TestId("count"))));

        set.Add("Bounded", s => s
            .Parameter(c => c.Start, 9)
            .Parameter(c => c.Max, 10)
            .Parameter(c => c.Min, 0)
            .Interaction("stops at the maximum", i => i
                .Click(By.Role("button", "Increase"))
                .ExpectText(By.TestId("count"), "10")
                .ExpectAttribute(By.Role("button", "Increase"), "disabled", "")));

        set.Add("Step of five", s => s.Parameter(c => c.Step, 5).Parameter(c => c.Label, "Seats"));
    }
}

public sealed class TextFieldScenarios : ScenarioSet<TextField>
{
    protected override void Configure(ScenarioSetBuilder<TextField> set)
    {
        set.Title("Inputs/Text field").Description("A labeled text input with helper text, validation styling and two-way binding.").Tags("inputs", "forms");
        set.Setup(s => s.Parameter(t => t.Label, "Email address").Parameter(t => t.InputType, "email"));

        set.Add("Empty", s => s
            .Parameter(t => t.Placeholder, "you@example.com")
            .Interaction("typing updates the bound value", i => i
                .Fill(By.Label("Email address"), "ada@example.com")
                .ExpectValue(By.Label("Email address"), "ada@example.com")
                .ExpectCallback("ValueChanged")));
        set.Add("Filled", s => s.Parameter(t => t.Value, "ada@example.com"));
        set.Add("With help", s => s.Parameter(t => t.HelperText, "We never share your email.").Parameter(t => t.Required, true));
        set.Add("Invalid", s => s
            .Parameter(t => t.Value, "not-an-email")
            .Parameter(t => t.Invalid, true)
            .Parameter(t => t.HelperText, "Enter a valid email address."));
        set.Add("Disabled", s => s.Parameter(t => t.Value, "locked@example.com").Parameter(t => t.Disabled, true));
    }
}
