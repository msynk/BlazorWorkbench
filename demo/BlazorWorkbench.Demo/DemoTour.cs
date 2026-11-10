namespace BlazorWorkbench.Demo;

/// <summary>
/// The guided tour shown on the Workbench overview page (<see cref="WorkbenchOptions.Introduction"/>). Every stop links to a
/// scenario with the panel that matters already open (<c>?panel=...</c>), or to a component's documentation page. The links
/// are checked by a test, so renaming a scenario cannot silently break the tour.
/// </summary>
public static class DemoTour
{
    public const string Markdown = """
        **Trailhead UI** is the component library of a fictional outdoor store. You are looking at it in **Blazor Workbench**,
        which renders every component in isolation so you can develop, review, document and test it without running the app.

        Every entry in the explorer is a **scenario**: a C# definition of one state of a component. A scenario sets the
        component's parameters *and the world around it*: the HTTP backend, the signed-in user, the clock, services, the
        device, the culture and the viewport. That is why a declined payment, a session about to expire or a dashboard in a
        partial outage is one click away here, and why every scenario is also a test.

        > **Reading a scenario page.** The chips under the title list what the scenario **simulates** and what it **verifies**; click one to open the panel behind it. The toolbar switches viewport, theme, culture and text direction. Below the preview, **Controls** edits parameters live, **Environment** shows the fakes, gates and the clock, **Actions** logs callbacks, **Diagnostics** collects exceptions, requests and navigation, **Interactions** runs tests, **Accessibility** runs axe-core, **Visual** compares screenshots and **Code** exports the current state as Razor and C#.

        ## Tour, part 1: control the world around a component

        | Stop | Try this |
        | --- | --- |
        | [A payment API that declines](scenario/commerce-checkout--card-declined?panel=diagnostics) | Click **Place order**. The fake backend answers `402`, and **Diagnostics** shows the request and the rule that answered it. The other checkout scenarios answer `409`, `422`, a timeout and a flaky network. No server is involved. |
        | [A spinner you can hold](scenario/commerce-checkout--placing-order-gated?panel=environment) | Click **Place order**, look at the progress state as long as you like, then **Release** the `payment` gate in **Environment**. Tests release the same gate, so loading states are tested and screenshotted without sleeps. |
        | [Fifteen minutes in one click](scenario/account-session-timeout--active?panel=environment) | The clock is frozen. Press **+1 min** until the expiry warning appears, then **+1 s** to watch the countdown. |
        | [Six users, no test accounts](docs/account-workspace-settings) | One settings page seen by a viewer, an editor, an admin, an owner, a billing contact and a signed-out visitor, checked against the library's real authorization policies. |
        | [A partial outage](scenario/analytics-sales-dashboard--partial-outage?panel=environment) | One of three endpoints answers `500`, and only its widget shows an error. Its neighbors: *Loading (gated)*, *Slow network* and *Offline*. |
        | [A fake API you can explore](scenario/commerce-product-search--start-typing?panel=diagnostics) | Type `jacket` and sort by price. A custom handler filters a fixed catalog, so the component behaves as it would against a real search API. |
        | [Services swapped per scenario](scenario/layout-app-header--rewards-enabled?panel=environment) | The feature flag service is replaced for this scenario only. **Environment** lists each service and where it came from. |
        | [Phones, themes and right to left](scenario/layout-app-header--phone) | Open the menu. Then change **Viewport**, **Theme** (try `high-contrast`) and **Culture** (`ar-SA` mirrors the layout) in the toolbar. |
        | [Four stores, one price tag](docs/commerce-price-tag) | Currency and tax come from a cascading value; separators and symbols from the culture. |
        | [A phone in the browser](scenario/device-location-card--permission-denied?panel=environment) | Location, network, battery and share (.NET MAUI Essentials) are simulated, so device states render on any host. |

        ## Tour, part 2: what every scenario gives you

        | Stop | Try this |
        | --- | --- |
        | [Interaction tests](scenario/forms-onboarding-wizard--start?panel=interactions) | Press **Run** on *completes every step*: it fills the form, checks that focus moves to each step's heading and records visual checkpoints. CI runs the same steps. |
        | [Accessibility scans](scenario/display-card--dont-icon-actions-without-names?panel=accessibility) | Press **Run scan**. axe-core reports the two icon-only buttons that have no accessible name. Compare with *Icon actions, labeled*. |
        | [Live controls and code](scenario/display-badge--custom-accent?panel=controls) | Change any parameter; the accent uses a custom color editor. The URL keeps your changes, and **Code** turns them into Razor and C#. |
        | [Errors with context](scenario/feedback-flaky-widget--throws-while-rendering?panel=diagnostics) | A component that throws while rendering. The preview shows the exception, and **Diagnostics** records it with the scenario id. |
        | [Screenshots that never flake](scenario/analytics-revenue-chart--seeded-data?panel=visual) | The chart's random data comes from a seeded `Random`: the same on every run and machine, so screenshots can be compared pixel by pixel. |
        | [Documentation for free](docs/commerce-checkout) | Generated from the component and its scenarios: parameters, dependencies, and a live preview and Razor snippet per scenario. |

        ## Tour, part 3: look closely

        | Stop | Try this |
        | --- | --- |
        | [Measure, grid and outlines](scenario/layout-app-header--desktop?measure=1&grid=1) | Hover the header: the measure tool shows size, padding and margin. The canvas toolbar also zooms (**Alt+=**), switches backgrounds and flashes DOM updates. |
        | [Device mode](scenario/layout-app-header--desktop?viewport=1000x700) | Drag the right edge of the preview to the left: the links collapse behind **Menu** at 720 px. The ruler above shows the breakpoints of `demo.css`; click a band, or pick both sides of one in **Breakpoints**. **Touch** makes buttons 44 px tall, as on a phone. |
        | [Sideways on a phone](scenario/display-card--dont-unbreakable-link) | A URL that cannot wrap makes the page scroll sideways: the viewport bar says by how much, and a click highlights the culprit. `workbench test --responsive` checks every scenario at 320 px and on both sides of every breakpoint. |
        | [States without a mouse](scenario/inputs-button--keyboard-focus) | `:focus-visible` is forced with `.ForceState(...)`, so it is screenshotted like any state. In any scenario, **States** in the canvas toolbar forces `:hover`, `:focus` or `:active`. |
        | [Every variant at once](scenario/inputs-button--variants-primary-medium?mode=gallery) | `set.Matrix(...)` made one scenario per variant and size; **Gallery** lays them out as a table. *Badge* has a pairwise matrix: 15 scenarios cover every pair of 30 combinations. |
        | [One scenario, every theme](scenario/display-card--with-footer?mode=compare) | **Compare** renders the scenario once per theme, viewport, breakpoint, culture, user, forced state or vision simulation, side by side. Try [the dashboard on both sides of each breakpoint](scenario/analytics-sales-dashboard--loaded?mode=compare&compare=width). |
        | [How others see it](scenario/forms-signup-form--empty?vision=deuteranopia&text=200&tabs=1) | Deuteranopia, text at 200% and the tab order, from the toolbar's **Accessibility** menu. In **Accessibility**, **Highlight** outlines each finding in the preview. |
        | [The design next to the code](scenario/inputs-button--primary?panel=design) | Overlay the mockup on the button and tick **Difference**: the corners and padding do not match the design file. |
        | [Write tests by clicking](scenario/forms-signup-form--empty?panel=interactions) | Press **Record**, fill in the form and submit; **Add assertion** checks what you click. Copy the C#. **Debug** steps through existing tests one step at a time. |
        | [Remembered between visits](scenario/feedback-whats-new-banner--dismissed-an-older-release?panel=storage) | `localStorage` is cleared before every preview and seeded by the scenario. Edit it in **Storage**, then reload. |
        | [Another user, a slower network](scenario/account-workspace-settings--viewer?user=admin) | **User** and **Network** in the toolbar preview any scenario as another person or on Slow 3G, without a new scenario. |
        | [What it renders](scenario/display-badge--with-count?panel=html) | **HTML** shows the markup, without Blazor's comments and scoped-CSS attributes. |
        | [Guidelines with live examples](pages/guidelines-buttons-and-actions) | Documentation pages embed scenarios and parameter tables; the parameter table shows which values no scenario covers. |
        | [Design tokens](tokens) | Every CSS custom property of the stylesheets, as swatches, type samples and spacing bars, per theme. Nothing to declare. |

        Press **?** for keyboard shortcuts; **Alt+←** and **Alt+→** walk through the scenarios.

        ## Every scenario is a test

        The scenarios you just explored run unchanged as automated tests. From the repository root:

        ```bash
        dotnet run --project src/BlazorWorkbench.Cli -- test --a11y --visual   # Playwright: render, interactions, axe-core, screenshots
        dotnet run --project src/BlazorWorkbench.Cli -- approve                 # accept reviewed screenshots as baselines
        dotnet test demo/BlazorWorkbench.Demo.Components.Tests                  # bUnit: every scenario, without a browser
        ```

        The same scenario files also run with Interactive WebAssembly and inside a .NET MAUI app. They live in
        `demo/BlazorWorkbench.Demo/Scenarios`; the [demo README](https://github.com/msynk/BlazorWorkbench/tree/main/demo#readme)
        maps every feature to the code that shows it.
        """;
}
