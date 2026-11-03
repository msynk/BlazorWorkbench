# Visual regression

`workbench test --visual` captures a screenshot of every scenario (for each configured viewport and theme) and compares it with an approved baseline.

## Workflow

```bash
workbench test --visual                # 1. capture; new screenshots are reported as "missing"
open artifacts/workbench/index.html    # 2. review current and difference images
workbench approve                      # 3. approve failed or missing screenshots as baselines
git add samples/MyWorkbench/baselines  # 4. commit them with the change that caused them
```

- Normal test runs **never** write baselines. Approval is explicit, can be filtered (`approve -f "inputs-*"`), and is recorded in `manifest.json` with the SHA-256, size, browser and version, OS, viewport, theme and approval time. Baseline changes are reviewed in pull requests like any other diff.
- `approve --all` also re-approves screenshots that passed, for example after upgrading the browser.
- `approve --prune` deletes baselines of scenarios that no longer exist. It requires an unfiltered run.

## Baseline layout

```
baselines/
  manifest.json
  windows/
    inputs-button--primary/
      laptop-light.png
      laptop-light-submitted.png     # interaction checkpoint
  linux/
    ...
```

Text rendering differs between operating systems (fonts, hinting, antialiasing), so each platform has its own baselines. A run compares only against the baselines of the platform it runs on. Approving a report from another platform, for example a Linux CI artifact approved on Windows, writes to that platform's folder.

## Variants

- Viewports: `--viewport mobile laptop`, `viewports` in `workbench.json`, or `.Visual(v => v.Viewports(...))` per scenario.
- Themes: `--theme light dark`, `themes`, or `.Visual(v => v.Themes(...))`. Automated runs set the theme's attributes **and** emulate `prefers-color-scheme`.
- Variant names are `{viewport}-{theme}`, plus `-{checkpoint}` for interaction checkpoints.

## Determinism

Before each capture, Workbench:

- waits for readiness: tracked work idle, quiet DOM, fonts and images loaded, and the `ReadyWhen` selector if one is declared;
- disables CSS animations and transitions and hides the caret (`Animations = Disabled`, `Caret = Hide`), and sets `reducedMotion = reduce`;
- uses UTC, a device scale factor of 1, the scenario culture as browser locale, and seeded data with a fixed clock when the scenario declares them;
- masks the selectors from `.Visual(v => v.Ignore(".timestamp"))`. Prefer a fixed clock and fixtures over masks.

Padded and centered scenarios capture the preview root element. Fullscreen scenarios (dialogs, overlays) and `FullPage()` capture the viewport or the page.

## Comparison

Pixels are compared in YIQ color space after blending with white, a pixelmatch-style perceptual metric:

- `threshold` (0–1, default 0.1) is the per-pixel color distance tolerated as noise.
- `maxDiffPixelRatio` (0–1, default 0) is the share of differing pixels tolerated before the check fails.
- A size change always fails and is reported with both sizes.

Configure them globally in `workbench.json` (`visual.threshold`, `visual.maxDiffPixelRatio`) or per scenario. The difference image shows changed pixels in red over a faded copy of the baseline, and the HTML report and the **Visual** panel show the baseline and the current image side by side, as an overlay, or as a difference.

## Missing assets and incomplete rendering

Failed stylesheet, script, font or image requests (HTTP 4xx/5xx or network failures) are error diagnostics and fail the **render** check. A screenshot of a page with broken styles is therefore never approved silently.

## Privacy

Screenshots show whatever the scenario renders. Use synthetic fixtures and never real customer data. Committed baselines become part of your repository; see [security](security.md).
