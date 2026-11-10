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
           .Status(ComponentStatus.Stable)
           .Documentation("""
               Buttons trigger actions. Use **one** primary button per view.

               | Variant | Use for |
               | --- | --- |
               | `Primary` | The main action |
               | `Secondary` | Alternative actions |
               | `Danger` | Destructive actions |

               **Workbench features here:** *Primary* has a performance budget (`.Budget(readyWithin: 3 s, maxDomUpdates: 20)`)
               that `workbench test` enforces, and an interaction test that asserts the `OnClick` callback. *Large icon-only*
               sets `aria-label`, an attribute captured by `CaptureUnmatchedValues`, with `.Parameter("aria-label", ...)`.
               Open **Code** on any scenario to get the Razor markup for its current state.

               **New in this tour:** the *Variants* scenarios are a matrix (`set.Matrix(...)`): one scenario per variant and
               size, laid out as a table in the **Gallery** view. *Hover* and *Keyboard focus* force `:hover` and
               `:focus-visible` with `.ForceState(...)`, so those states are screenshotted and scanned like any other.
               *Primary* links a design mockup: open **Design** and overlay it on the preview.

               {{scenario:inputs-button--keyboard-focus}}
               """);

        set.Add("Primary", s => s
            .ChildContent("Save changes")
            .Design("_content/BlazorWorkbench.Demo.Components/designs/button-primary.svg", "Primary button (design file)")
            .Notes("""
                **Design review.** Open **Design** and press **Overlay on preview**, then tick **Difference**: matching pixels
                turn black. The mockup has 8 px corners and 18 px side padding; the component uses 6 px and 14 px, so the
                edges light up. Decide which one is right before the next release.
                """)
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
        set.Add("Hover", s => s
            .ChildContent("Save changes")
            .ForceState(PseudoState.Hover)
            .Description("The :hover style, forced without a mouse: the screenshot and the accessibility scan cover it."));
        set.Add("Keyboard focus", s => s
            .ChildContent("Save changes")
            .ForceState(PseudoState.FocusVisible, ".btn")
            .Description("The :focus-visible ring on the button only, as a keyboard user sees it."));

        set.Matrix("Variants", m => m.Vary(b => b.Variant).Vary(b => b.Size), s => s.ChildContent("Button"));

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
        set.Documentation("""
            The interaction tests click, focus and press keys (`Press("ArrowUp")`), then assert text, focus and how often
            `OnChanged` was raised. Click the buttons yourself and watch **Actions** count the callbacks; change **Start** or
            **Step** in **Controls** and the URL keeps the new state, ready to share.
            """);

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
        set.Documentation("""
            `Value` is two-way bound. Type into the preview: Workbench records `ValueChanged` in **Actions** and copies the new
            value back into **Controls** and the URL, so the state you reached by typing survives a reload.
            """);
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
        set.Add("Keyboard focus", s => s
            .Parameter(t => t.Placeholder, "you@example.com")
            .ForceState(PseudoState.FocusVisible, "input")
            .Description("The input's focus ring, forced with .ForceState(PseudoState.FocusVisible, \"input\")."));
    }
}
