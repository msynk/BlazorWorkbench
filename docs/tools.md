# Workbench tools

This guide covers the tools for looking at a component closely: the canvas toolbar, device mode for responsive design, the
accessibility simulations, forced states, the Compare and Gallery views, the Design, HTML and Storage panels, the interaction recorder and debugger, the
explorer's filters and shortcuts, documentation pages and design tokens. Every tool works in Interactive Server and
Interactive WebAssembly hosts. The .NET MAUI app has no shell, so these tools are not available there.

Tool state is part of the URL. A link such as `scenario/inputs-button--primary?zoom=2&grid=1&vision=deuteranopia` opens the
scenario with the same view, and **Copy link** in the toolbar copies the current state.

## Canvas toolbar

The canvas toolbar sits above the preview.

| Tool | What it does | URL | Key |
| --- | --- | --- | --- |
| Zoom | 25% to 300%. The frame scales; the component still lays out at the viewport size. | `zoom=1.5` | Alt+= / Alt+- / Alt+0 |
| Fit | Zooms out until a fixed viewport fits the canvas (never above 100%). | `zoom=0.63` | Alt+9 |
| Background | The theme's background, or one of `options.Backgrounds`: white, light gray, dark, black, transparent (checkerboard) and your own. | `bg=dark` | |
| Grid | A layout grid with `options.GridSize` cells (8 px by default) and a major line every 8 cells. | `grid=1` | G |
| Outline | Outlines every element, with a different color per nesting level. | `outline=1` | O |
| Measure | Hover an element to see its size, padding (green) and margin (orange). | `measure=1` | M |
| Updates | Flashes every element whose DOM changes, which exposes unexpected re-rendering. | `updates=1` | |
| States | Forces `:hover`, `:focus`, `:focus-visible`, `:focus-within` or `:active` on every element (see below). | `pseudo=hover,focus` | |
| Accessibility | Vision simulation, text size, text spacing and tab order (see below). | | |
| Clear tools | Turns every tool off. | | |
| Panel position | Moves the panel to the right or back to the bottom. The choice is remembered. | | Alt+D |
| Focus mode | Hides the explorer and the panel. | | Alt+F |

Tools draw in a layer outside the preview root, so readiness, locators, accessibility scans and screenshots ignore them.
Changing a tool never reloads the preview: the component keeps its state.

The header toolbar also has two session switches. They reload the preview, because they change the session:

- **User** previews any scenario as one of the users in `options.Users` (`options.AddUser("Admin", u => u.Role("Admin"))`) or
  signed out (`?user=admin`, `?user=anonymous`).
- **Network** applies a network profile to the fake backend: *Fast 4G*, *Fast 3G* and *Slow 3G* add latency to every request,
  *Offline* fails them (`?network=slow-3g`). Add your own to `options.NetworkProfiles`.

## Device mode and responsive design

The viewport bar above the preview works like the device mode of browser developer tools. Everything in it is in the URL.

| Control | What it does | URL |
| --- | --- | --- |
| Viewport | **Responsive** fills the canvas; a preset (`options.Viewports`) fixes the size; **Custom** keeps the size you set. | `viewport=mobile`, `viewport=390x844` |
| Width × height | The size in CSS pixels, from 120 to 3840. A responsive preview shows its current size; typing a size fixes it. | |
| Resize handles | Drag the right edge, the bottom edge or the corner of the preview. The size follows the pointer and is shown next to it. With a handle focused, the arrow keys change the size by 1 px, or 10 px with Shift. | `viewport=WxH` |
| Rotate | Swaps the width and the height of a fixed viewport. | `rotate=1` |
| Touch | Media queries on `hover` and `pointer` answer like a phone: `(hover: none)` and `(pointer: coarse)` match, and `matchMedia()` agrees. On for mobile presets, as in automated runs; the switch overrides it for any size. | `touch=1`, `touch=0` |
| Breakpoints | Every width at which a media query of the preview's stylesheets starts or stops applying, named after `options.Breakpoints`. Each one offers its two edges: the last width before it and the first width at it. | |
| Range | The breakpoint range of the current width, such as `md · 768–1023`. | |
| Sideways scrolling | Shown when the page is wider than the viewport, so it scrolls horizontally (WCAG 1.4.10 Reflow). Click it to highlight the elements that stick out. Elements allowed with `.Responsive(r => r.AllowOverflow(...))` show *Allowed overflow*. | |

The **ruler** above the preview has one band per breakpoint, centered on the preview and as wide as the viewport at that
breakpoint, like the media query bar of browser developer tools: the preview's edges line up with a band when the width
is that breakpoint, and the bands inside the edges are the ones that apply. Click a band to set its width, or Alt+click
for one pixel less. Without breakpoints the ruler shows the viewport presets.

Breakpoints are read from the media queries of every same-origin stylesheet of the preview, including `@import`ed ones
and theme stylesheets: `(min-width: 768px)`, `(max-width: 767.98px)`, `(width >= 48em)` and `(600px < width)` are all
understood, in `px`, `em` and `rem`. Container queries depend on the container, not the viewport, and are not listed;
resizing the viewport still resizes containers that follow it. Declare your design system's breakpoints to name them:

```csharp
options.Breakpoints.Add(new BreakpointPreset("md", 768));
```

**Compare → Breakpoints** renders the scenario at 320 px and on both sides of every breakpoint, side by side. Layouts
most often break on one side of a breakpoint, which this view shows at a glance.

The same sweep runs in CI: `workbench test --responsive` fails a scenario when its page scrolls sideways at any of
these widths. See [the responsive check](testing.md#responsive-check).

What the canvas cannot emulate: the device pixel ratio (`devicePixelRatio`, `resolution` media queries, `srcset`
choices), touch events, a mobile browser's viewport behavior and its user agent. Automated runs emulate all of them
for presets with `DeviceScaleFactor` and `IsMobile`, so screenshots of a mobile preset are taken like on a phone.

## Accessibility simulations

The **Accessibility** menu in the canvas toolbar:

| Simulation | What it shows | URL |
| --- | --- | --- |
| Vision | Protanopia, deuteranopia, tritanopia and their weaker forms, achromatopsia and achromatomaly (color matrices), blurred vision and reduced contrast. The filter applies to the frame, so nothing in the component changes. | `vision=deuteranopia` |
| Text size | 125%, 150% or 200% of the root font size, like a browser's text-only zoom. Text sized in `px` does not grow, which is what WCAG 1.4.4 (resize text) asks you to find. | `text=200` |
| Text spacing | The WCAG 1.4.12 text spacing overrides: line height 1.5, letter spacing 0.12em, word spacing 0.16em, paragraph spacing 2em. Content must not be cut off or overlap. | `spacing=1` |
| Tab order | Numbers every element reachable with Tab, in order, and connects them with a line. | `tabs=1` |

In the **Accessibility** panel, **Highlight** next to each finding outlines the element in the preview.

## Forced states

**States** in the canvas toolbar forces pseudo-classes on every element of the preview. Workbench copies every stylesheet
rule that uses `:hover`, `:focus`, `:focus-visible`, `:focus-within` or `:active` with the pseudo-class replaced by an
attribute, so the state applies without a pointer or a keyboard. Cross-origin stylesheets cannot be read and are left out.

A scenario can force a state on chosen elements, so screenshots and accessibility scans cover it:

```csharp
set.Add("Keyboard focus", s => s.ChildContent("Save").ForceState(PseudoState.FocusVisible, ".btn"));
set.Add("Hover", s => s.ChildContent("Save").ForceState(PseudoState.Hover));   // every element
```

## Compare and Gallery

The **Canvas / Compare / Gallery** switch next to the scenario title changes the stage:

- **Compare** renders the scenario once per value of an axis, side by side: themes, viewports, breakpoints (both sides of
  each), cultures, directions, users, forced states, vision simulations or network profiles. Untick values to hide them; **Frame zoom** sizes the frames. Every
  frame is its own preview session with the current controls. Up to 12 frames are shown (`?mode=compare`).
- **Gallery** shows every scenario of the component. A matrix with two axes (see [scenarios](scenarios.md#matrices)) is laid
  out as a table, rows by the first parameter and columns by the second (`?mode=gallery`).

Frames load lazily. In an Interactive Server host each frame is a circuit, so large comparisons cost server memory.

## Panels

| Panel | What it shows |
| --- | --- |
| **Design** | The design references of the scenario (`.Design(url)`): images, Figma files (embedded) and links. **Overlay on preview** draws an image over the component with adjustable opacity; **Difference** blends it so identical pixels turn black. Shown only when the scenario has references. |
| **HTML** | The rendered markup of the preview root, formatted, without Blazor's comments, `_bl_` and scoped-CSS (`b-xxxxxxxxxx`) attributes. Copy it into a bug report or a snapshot. |
| **Storage** | `localStorage`, `sessionStorage` and script-visible cookies of the preview. Remove items, add or change one, or clear everything; reload to see how the component starts with it. |

## Recording and debugging interactions

In **Interactions**:

- **Record** listens to the preview and writes steps: clicks, typing (as `Fill`), selections, checkboxes and keys such as
  Enter, Escape, Tab and the arrows. Locators prefer what users perceive: `data-testid`, then role and accessible name,
  then label or placeholder, then text, then a CSS path. **Add assertion** switches to checks: a click on an element adds
  `ExpectText` (or `ExpectValue` for inputs, `ExpectVisible` otherwise) instead of clicking it. The panel shows the steps
  and the C# to paste into a scenario.
- **Debug** runs a test against a fresh session and pauses before every step, with its target outlined in the preview.
  **Step** runs one step, **Continue** runs the rest, **Stop** skips them. A failed step stays highlighted.

## Explorer

The search box understands filters, and the chips under it toggle the common ones:

| Filter | Matches |
| --- | --- |
| `tag:forms` | Scenarios or components with the tag |
| `status:beta` | Components with that status (see [scenarios](scenarios.md#status)) |
| `is:matrix` | Scenarios generated by a matrix |
| `is:tested` | Scenarios with interaction tests |
| `is:failing`, `is:passing`, `is:flaky` | Results of the last `workbench test` run |
| `is:generated`, `is:skipped`, `is:deprecated`, `is:documented` | What the names say |

Filters combine with text: `is:matrix button`. Documentation pages appear in search results and in the tree.

When the host has results from `workbench test` (`report.json` in the results folder), every scenario shows a dot for its
last result (passed, failed, flaky or skipped), components show their number of failures, and the footer shows the run
summary; click it to list the failing scenarios. The **Recent** group lists the last scenarios you opened.

### Keyboard shortcuts

Press **?** for the list. Shortcuts work while the Workbench, not the preview, has focus.

| Keys | Action |
| --- | --- |
| `/` | Search |
| Alt+← / Alt+→ | Previous or next scenario |
| Alt+↑ / Alt+↓ | Previous or next component |
| R / Shift+R | Reload / reset the preview |
| G, O, M | Grid, outline, measure |
| Alt+= / Alt+- / Alt+0 | Zoom in, out, reset |
| Alt+9 | Zoom a fixed viewport to fit the canvas |
| ← → ↑ ↓ (Shift) | On a resize handle: resize the preview by 1 px (10 px) |
| Alt+F / Alt+D | Focus mode / panel position |

## Documentation pages

`options.AddPage(title, markdown)` adds a standalone page (a guideline, a pattern, a foundation) to the explorer, at the
path of its title (`Guidelines/Buttons`). Its Markdown uses the safe subset, and lines of their own can embed live content:

| Directive | Renders |
| --- | --- |
| `{{scenario:inputs-button--primary}}` | A live preview of the scenario with a link to open it |
| `{{scenarios:inputs-button}}` | Live previews of every scenario of a component (up to 12) |
| `{{parameters:inputs-button}}` | The component's parameter table, with value coverage |
| `{{tokens}}` | The design tokens |

The same directives work in a component's `set.Documentation(...)`, in scenario notes and in `options.Introduction`.
Directives inside fenced code blocks are left alone. An embed that names a missing scenario or component is reported as
`WB1025`.

## Design tokens

**Tokens** (in the explorer footer, or `/tokens`) lists every CSS custom property that the preview stylesheets define and
that resolves on the root element, with its value in the chosen theme. Colors get swatches, typography a sample, spacing a
bar, radii and shadows a box. Nothing is declared for Workbench: add tokens to your stylesheets as custom properties
(`:root { --color-primary: #3f37c9 }`) and they appear.

## Documentation additions

Component documentation pages also show:

- The lifecycle **status** of the component.
- **Value coverage** in the parameter table: for `bool` and enum parameters, which values the declared scenarios use; a
  struck-through value is never shown by any scenario. Other parameters show how many scenarios set them.
- **Relationships**: the catalog components this component renders and the ones that render it, read from the compiled
  render code (Razor's `OpenComponent<T>` calls, including render fragments and inferred generic types).
- **Design** references, scenario **notes**, and the Razor snippet of each scenario behind **Show code**.
