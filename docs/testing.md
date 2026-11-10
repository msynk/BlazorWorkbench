# Testing and Playwright integration

Workbench does not add a new test framework. It reuses [Playwright](https://playwright.dev/dotnet/) for browser automation, [bUnit](bunit.md) for render tests and [axe-core](accessibility.md) for accessibility. Each scenario becomes a set of checks:

| Check | Runs when | Fails when |
| --- | --- | --- |
| **render** | always | The preview does not become ready within the timeout, the error boundary catches an exception, or error diagnostics appear (console errors, failed resources, JS interop errors). |
| **performance** | the scenario declares a `Budget` and `--no-budgets` is not set | Ready time, DOM update count or interaction time exceeds the budget. |
| **interaction** | `--no-interactions` not set | A step fails. Each test starts from a fresh activation. |
| **accessibility** | `--a11y` (or `workbench a11y`) | axe-core reports violations at or above `--fail-on` (default `serious`). |
| **visual** | `--visual` | Screenshots differ from approved baselines, or the baseline is missing. |

## Running tests

```bash
workbench test                                   # render + interactions for every scenario
workbench test --changed-since origin/main       # only scenarios affected by changed files
workbench test -f "inputs-*,tag:forms"           # filter by id/title glob or tag
workbench test --a11y --visual -j 4 --retries 1  # parallel workers; retries mark flaky passes
workbench test --only-failed                     # rerun failures from the last report
workbench test --url http://localhost:5000       # use a running host instead of starting one
```

By default the CLI builds and starts the host project from `workbench.json` on a free loopback port, waits for `/_workbench/api/health`, runs the scenarios in headless Chromium, and then stops the host.

When the host is a .NET MAUI Blazor Hybrid app, the runner drives the app's own WebView2 instead of launching a browser, one scenario at a time. See [Blazor Hybrid](hybrid.md#automated-tests-windows).

### Outputs

The output directory (default `artifacts/workbench`) contains:

| File | Content |
| --- | --- |
| `report.json` | Machine-readable [`TestRunReport`](../src/BlazorWorkbench.Core/Contracts/TestReportContracts.cs): environment (OS, platform, browser version), settings, a summary, and per-scenario checks, steps, accessibility findings, visual comparisons, metrics and redacted diagnostics. |
| `junit.xml` | One test case per check, grouped by component. CI systems show it in their test tabs. |
| `index.html` | A self-contained review page with filters, steps, findings and screenshots. |
| `screenshots/` | Current screenshots and difference images. |
| `traces/` | Playwright traces of failing interaction tests (open with `npx playwright show-trace`). They contain DOM snapshots; see [security](security.md). |
| `failures/` | A screenshot at the point of each interaction failure. |
| `diagnostics.jsonl` | Every diagnostic of every page load, one JSON object per line with scenario id, check, session id, timestamp, source, severity and message. Use it to correlate with logs or traces. |
| `host.log` | Host output (written on infrastructure errors or with `--verbose`). |

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Every selected scenario passed or was skipped. |
| 1 | At least one check failed (including missing baselines unless `--allow-missing-baselines`). |
| 2 | Usage error (invalid options, missing report for `approve`). |
| 3 | Infrastructure error: the host did not start, the browser is missing, or the run was canceled. Component defects never produce 3. |

## Interaction semantics

Steps use Playwright's user-facing locators. The Playwright runner is authoritative:

- Locators are strict: an action on a locator that matches several elements fails, as in Playwright.
- Actions wait until the element is visible, enabled and stable. Assertions retry for up to 5 s (`StepTimeout`).
- After each action, the runner waits until Workbench-tracked work is idle and the DOM is quiet.
- `ExpectCallback(name, times)` counts calls of the scenario's automatically instrumented `EventCallback` parameters.
- `ExpectNoErrors()` fails if error diagnostics have been recorded so far.
- `Checkpoint(name)` captures an extra visual baseline (`{viewport}-{theme}-{name}`) when `--visual` is set. Each checkpoint is reported as its own visual check (approve it with `workbench approve` like any other screenshot); a failed or missing checkpoint fails the scenario but does not stop the interaction.

The **Interactions panel** in the UI runs the same steps with a lightweight in-browser runner. That runner simulates events, approximates the role and accessible-name computation, and emulates the default actions of `Enter`, `Space` and `Tab`. Use it to check steps quickly while you write them. If the two runners disagree, the CLI result counts.

## Using the engine from your own tests

`BlazorWorkbench.Testing` can be used from any .NET test project:

```csharp
await using var host = await WorkbenchHost.StartAsync("demo/BlazorWorkbench.Demo/BlazorWorkbench.Demo.csproj");
var report = await new WorkbenchTestRunner(host, new TestRunOptions
{
    Filter = "inputs-*",
    Accessibility = true,
    OutputDirectory = "artifacts/my-run",
}).RunAsync();
Assert.Equal(0, report.Summary.Failed);
```

For custom Playwright code, open any preview URL directly (`host.PreviewUrl(id)`) and wait for readiness:

```csharp
await page.GotoAsync(host.PreviewUrl("inputs-button--primary").ToString());
await page.WaitForFunctionAsync("() => window.__workbench?.ready");
```

### Automation surface (`window.__workbench`)

| Member | Description |
| --- | --- |
| `state`, `ready` | `loading`, `ready` or `error`. Also exposed as `<html data-workbench-state>`. |
| `scenarioId` | The active scenario. |
| `diagnostics`, `callbacks`, `gates` | Redacted snapshots. |
| `metrics()` | `{ readyMs, domUpdates, pending }` |
| `waitForIdle(timeoutMs)` | Resolves when tracked work is idle and the DOM is quiet. |
| `callbackCount(name)` | Number of calls of an instrumented callback. |
| `releaseGate(name)`, `failGate(name)`, `advanceClock(ms)` | Environment control. |
| `runAxe({ tags, disabledRules })` | Runs axe-core on the preview root. |
| `runInteraction(test)` | The in-browser runner (best effort). |
| `applyTools(tools)`, `highlight(selector)` | Canvas tools (grid, outline, forced states, ...) and element highlighting, as the shell uses them. |
| `html()`, `storage()`, `tokens()` | The formatted markup of the preview root, the preview's storage, and the CSS custom properties of the stylesheets. |

This surface is part of the public contract and follows the versioning policy in [compatibility](compatibility.md).

### Media features and accessibility todo

Automated runs emulate `prefers-reduced-motion: reduce` for every scenario. A scenario can add `forced-colors: active`
(Windows high contrast) and the `print` media type with `.Media(m => m.ForcedColors().Print())`; its screenshots then show
that rendering. A scenario with `.Accessibility(a => a.Todo("reason"))` is scanned, and its violations are listed in the
report with a `todo:` message, but they do not fail the run.

### Results in the Workbench UI

After `workbench test`, the host reads `report.json` from the results folder: every scenario in the explorer shows a dot for
its result (passed, failed, flaky, skipped), components show their number of failures, and `is:failing` lists them. The
**Visual** panel shows baseline, current and difference images. Hosts can serve results from elsewhere (for example a CI
server) by registering an `ITestResultsProvider` and an `IVisualResultsProvider`.

## Change impact (`--changed-since`)

`--changed-since <git ref>` collects the files changed since the ref, plus staged, unstaged and untracked files, and maps them to scenarios:

| Changed file | Selected scenarios |
| --- | --- |
| A component (`Button.razor`, `.razor.cs`, `.razor.css`, `.razor.js`) | Every scenario of that component (from PDB source paths), plus every scenario of components that render it, directly or through other components, and scenario files that render it (found by scanning `.razor`/`.cs` sources under the Workbench root for `<Button`, `OpenComponent<Button>` or `typeof(Button)`). |
| A scenario set or provider file | The scenarios it declares. |
| A baseline image (`baselines/{platform}/{scenario}/…`) | That scenario. |
| Markdown, `docs/`, `.github/`, `LICENSE`, baseline manifests | None. |
| Anything else (shared code, global CSS, project files) | **All scenarios.** The selection is conservative. |

The run prints why it selected what it did. With no affecting changes it exits 0 without starting a browser. The selection needs PDBs (Debug builds have them) and `git` on `PATH`.

## Performance measurements

- `readyMs` is the time from navigation start until readiness, as measured by the browser (`performance.now()`). It includes the Blazor circuit start-up.
- `domUpdates` is the number of DOM mutation batches observed until readiness. It is a proxy for render activity, **not** a count of component renders: Workbench cannot count the renders of a component it does not own. In bUnit, use `cut.RenderCount` for exact numbers.
- `serverActivities`, `serverMs` and `serverMaxMs` (Interactive Server only) are the number and total and maximum duration of inbound circuit activities: events, JS interop calls and render acknowledgements, as measured on the server by a circuit handler. They show server-side processing cost, separate from network and browser time.
- bUnit: `this.MeasureScenario(id, assemblies)` returns the allocated bytes and time of the synchronous initial render, and `cut.ComponentRenderCount()` returns how often the scenario's component rendered.
- Instrumentation overhead (MutationObserver, diagnostic hooks) is included in both values. Compare budgets only between runs on the same machine type and browser version, which the report records.
- In Interactive WebAssembly, every preview is a full document load, so `readyMs` includes downloading and starting the .NET runtime. Budgets set for Interactive Server can fail there on slow or busy machines. Runs that check something else (for example visual-only CI jobs) can pass `--no-budgets`; timings are still reported.

## Tracing with OpenTelemetry

Workbench emits `System.Diagnostics` activities and metrics under the name `BlazorWorkbench` (`WorkbenchTelemetry.Name`):

| Signal | Name | Tags |
| --- | --- | --- |
| Activity | `workbench.scenario.activate` | `workbench.scenario.id`, `workbench.session.id` |
| Activity | `workbench.http {METHOD}` (client) | `http.request.method`, `url.full` (redacted), `http.response.status_code`, `workbench.rule`, `workbench.outcome` |
| Counter | `workbench.http.requests` | `workbench.outcome` |
| Counter | `workbench.diagnostics` | `workbench.source`, `workbench.severity` |
| Histogram (ms) | `workbench.circuit.activity.duration` | – |

Export them from the host like any other source:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(WorkbenchTelemetry.Name).AddOtlpExporter())
    .WithMetrics(m => m.AddMeter(WorkbenchTelemetry.Name).AddOtlpExporter());
```

Diagnostics carry the same session id as the activities (`sessionId` in the UI, reports and `diagnostics.jsonl`).

## Flakiness

- Prefer gates and fixed clocks over timeouts. Interaction steps never sleep.
- `--retries n` reruns failed scenarios. A scenario that passes on retry is reported as *flaky* in its message.
- Traces and failure screenshots let you investigate a failure without rerunning it locally.
