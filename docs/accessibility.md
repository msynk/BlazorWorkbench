# Accessibility testing

Workbench runs [axe-core](https://github.com/dequelabs/axe-core) (version 4.14, from Deque's MPL-2.0 package, unmodified) against the preview root of each scenario. The same engine runs in the UI and in the CLI, so the results agree.

> **Automated checks are not a conformance claim.** axe-core detects only a part of the WCAG failure conditions. A scenario that passes is *not* proven accessible. Manually verify keyboard operation, focus order and visibility, screen reader output (names, roles, states, live announcements), zoom and reflow, and motion preferences.

## In the UI

Open **Accessibility** and select **Run scan**. The panel lists violations and *needs review* items (`incomplete` in axe terms) by impact. Each item includes the rule, the help text, a link to remediation guidance, WCAG tags and the affected elements.

The canvas toolbar's **Accessibility** menu simulates color vision deficiencies and blurred vision, scales text to 200%,
applies the WCAG 1.4.12 text spacing overrides and numbers the tab order. **Highlight** next to a finding outlines the
element in the preview. See [tools](tools.md#accessibility-simulations).

## In the CLI

```bash
workbench a11y                         # accessibility only
workbench test --a11y --fail-on moderate
```

- Rules run by WCAG tag. The default set is `wcag2a, wcag2aa, wcag21a, wcag21aa, wcag22aa, best-practice`; change it with `accessibility.tags` in `workbench.json`.
- A violation fails the check when its impact is at or above `failOn` (`minor` < `moderate` < `serious` < `critical`, default `serious`). Lower-impact violations are reported but do not fail the check. *Needs review* items never fail it.
- Findings in `report.json` include the rule id, impact, help URL, WCAG tags and target selectors. Element HTML is truncated and redacted.
- axe-core does not check reflow. `workbench test --responsive` does: it fails a scenario whose page scrolls sideways at
  320 px (WCAG 1.4.10) or on either side of a breakpoint. See [the responsive check](testing.md#responsive-check).

## Adopting checks gradually

`.Accessibility(a => a.Todo("Fix tracked in #456"))` keeps scanning a scenario but reports its violations without failing
the run, like Storybook's `todo` mode. Use it while you fix an existing component; `Skip` hides the scenario from scans
entirely and should be reserved for deliberate misuse examples.

## Disabling rules

Disable rules per scenario and give the reason:

```csharp
s.Accessibility(a => a.DisableRule("color-contrast", "Brand colors approved in design review #42"));
s.Accessibility(a => a.Skip("Third-party widget audited separately"));
```

A disabled rule without a justification is reported as `WB1021`. The justification appears in the UI and in reports.

## Keyboard and focus tests

Keyboard behavior goes into interaction tests:

```csharp
s.Interaction("dialog traps and restores focus", i => i
    .ExpectFocused(By.Role("dialog"))
    .Tab()
    .ExpectFocused(By.Role("button", "Cancel"))
    .Press("Escape")
    .ExpectHidden(By.Role("dialog")));
```

**Diagnostics** traces focus changes (`Focus → button "Save"`, Trace severity) so you can inspect focus order while you interact with the component.

## Live regions

Workbench does not run a screen reader. You can still assert the markup that makes announcements work, for example `ExpectAttribute(By.TestId("count"), "aria-live", "polite")` or `ExpectVisible(By.Role("status"))`. Then verify the actual announcements manually with NVDA, JAWS or VoiceOver.

## Example from the demo

The demo `ProductList` gave a sold-out button the accessible name "Add 4K monitor to cart" while its visible text was "Sold out". axe-core flagged this as `label-content-name-mismatch` (WCAG 2.5.3, *Label in Name*). The fix is in [ProductList.razor](../demo/BlazorWorkbench.Demo.Components/Data/ProductList.razor).
