using System.Diagnostics;
using Microsoft.Playwright;
using Locator = BlazorWorkbench.Locator;

namespace BlazorWorkbench.Testing;

/// <summary>Executes Workbench interaction steps with Playwright (the authoritative runner).</summary>
public static class PlaywrightSteps
{
    public static ILocator Resolve(IPage page, Locator locator) => locator.Kind switch
    {
        LocatorKind.Role => page.GetByRole(ParseRole(locator.Value), new PageGetByRoleOptions { Name = locator.Name, Exact = locator.Exact }),
        LocatorKind.Text => page.GetByText(locator.Value, new PageGetByTextOptions { Exact = locator.Exact }),
        LocatorKind.Label => page.GetByLabel(locator.Value, new PageGetByLabelOptions { Exact = locator.Exact }),
        LocatorKind.Placeholder => page.GetByPlaceholder(locator.Value, new PageGetByPlaceholderOptions { Exact = locator.Exact }),
        LocatorKind.TestId => page.GetByTestId(locator.Value),
        LocatorKind.Css => page.Locator(locator.Value),
        _ => throw new NotSupportedException($"Locator kind {locator.Kind} is not supported."),
    };

    public static AriaRole ParseRole(string role) =>
        Enum.TryParse<AriaRole>(role.Replace("-", "", StringComparison.Ordinal), ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{role}' is not an ARIA role known to Playwright.");

    /// <summary>Runs one step. Assertion failures throw <see cref="PlaywrightException"/> or <see cref="TimeoutException"/>.</summary>
    public static async Task RunAsync(IPage page, InteractionStep step, TimeSpan timeout, Func<string, Task>? checkpoint = null)
    {
        var ms = (float)timeout.TotalMilliseconds;
        ILocator Target() => Resolve(page, step.Target ?? throw new InvalidOperationException($"{step.Action} requires a target."));
        var expect = new LocatorAssertionsToBeVisibleOptions { Timeout = ms };

        switch (step.Action)
        {
            case StepAction.Click: await Target().ClickAsync(new LocatorClickOptions { Timeout = ms }); break;
            case StepAction.DoubleClick: await Target().DblClickAsync(new LocatorDblClickOptions { Timeout = ms }); break;
            case StepAction.Hover: await Target().HoverAsync(new LocatorHoverOptions { Timeout = ms }); break;
            case StepAction.Focus: await Target().FocusAsync(new LocatorFocusOptions { Timeout = ms }); break;
            case StepAction.Blur: await Target().BlurAsync(new LocatorBlurOptions { Timeout = ms }); break;
            case StepAction.Fill: await Target().FillAsync(step.Value ?? "", new LocatorFillOptions { Timeout = ms }); break;
            case StepAction.Type: await Target().PressSequentiallyAsync(step.Value ?? "", new LocatorPressSequentiallyOptions { Timeout = ms }); break;
            case StepAction.Press:
                if (step.Target is not null) await Target().PressAsync(step.Value!, new LocatorPressOptions { Timeout = ms });
                else await page.Keyboard.PressAsync(step.Value!);
                break;
            case StepAction.Check: await Target().CheckAsync(new LocatorCheckOptions { Timeout = ms }); break;
            case StepAction.Uncheck: await Target().UncheckAsync(new LocatorUncheckOptions { Timeout = ms }); break;
            case StepAction.Select:
                var target = Target();
                try { await target.SelectOptionAsync(new SelectOptionValue { Value = step.Value }, new LocatorSelectOptionOptions { Timeout = 1000 }); }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    await target.SelectOptionAsync(new SelectOptionValue { Label = step.Value }, new LocatorSelectOptionOptions { Timeout = ms });
                }
                break;
            case StepAction.ReleaseGate:
                if (!await page.EvaluateAsync<bool>("n => window.__workbench.releaseGate(n)", step.Value))
                    throw new PlaywrightException($"Gate '{step.Value}' was already released or failed.");
                break;
            case StepAction.FailGate: await page.EvaluateAsync<bool>("n => window.__workbench.failGate(n)", step.Value); break;
            case StepAction.WaitFor: await Target().WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = ms }); break;
            case StepAction.ExpectVisible: await Assertions.Expect(Target()).ToBeVisibleAsync(expect); break;
            case StepAction.ExpectHidden: await Assertions.Expect(Target()).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = ms }); break;
            case StepAction.ExpectText: await Assertions.Expect(Target()).ToContainTextAsync(step.Expected!, new LocatorAssertionsToContainTextOptions { Timeout = ms }); break;
            case StepAction.ExpectValue: await Assertions.Expect(Target()).ToHaveValueAsync(step.Expected!, new LocatorAssertionsToHaveValueOptions { Timeout = ms }); break;
            case StepAction.ExpectAttribute: await Assertions.Expect(Target()).ToHaveAttributeAsync(step.Value!, step.Expected!, new LocatorAssertionsToHaveAttributeOptions { Timeout = ms }); break;
            case StepAction.ExpectFocused: await Assertions.Expect(Target()).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = ms }); break;
            case StepAction.ExpectCount: await Assertions.Expect(Target()).ToHaveCountAsync(step.Count ?? 0, new LocatorAssertionsToHaveCountOptions { Timeout = ms }); break;
            case StepAction.ExpectCallback: await ExpectCallbackAsync(page, step.Value!, step.Count, timeout); break;
            case StepAction.ExpectNoErrors:
                var errors = (await LoadState.DiagnosticsAsync(page)).Where(d => d.Severity == Diagnostics.DiagnosticSeverity.Error).ToList();
                if (errors.Count > 0)
                    throw new PlaywrightException($"{errors.Count} error diagnostic(s); first: [{errors[0].Source}] {errors[0].Message}");
                break;
            case StepAction.Checkpoint:
                if (checkpoint is not null) await checkpoint(step.Value!);
                break;
            case StepAction.DragTo:
                await Target().DragToAsync(Resolve(page, step.Destination!), new LocatorDragToOptions { Timeout = ms });
                break;
            case StepAction.Upload:
                await Target().SetInputFilesAsync(new FilePayload
                {
                    Name = step.Value!,
                    MimeType = step.ContentType ?? "application/octet-stream",
                    Buffer = Convert.FromBase64String(step.FileContent ?? ""),
                }, new LocatorSetInputFilesOptions { Timeout = ms });
                break;
            case StepAction.ScrollIntoView: await Target().ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = ms }); break;
            case StepAction.ExpectEnabled: await Assertions.Expect(Target()).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = ms }); break;
            case StepAction.ExpectDisabled: await Assertions.Expect(Target()).ToBeDisabledAsync(new LocatorAssertionsToBeDisabledOptions { Timeout = ms }); break;
            case StepAction.ExpectChecked: await Assertions.Expect(Target()).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Timeout = ms }); break;
            case StepAction.ExpectUnchecked: await Assertions.Expect(Target()).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Checked = false, Timeout = ms }); break;
            default:
                throw new NotSupportedException($"Step {step.Action} is not supported.");
        }

        if (step.Action is StepAction.Click or StepAction.DoubleClick or StepAction.Fill or StepAction.Type or StepAction.Press or
            StepAction.Check or StepAction.Uncheck or StepAction.Select or StepAction.ReleaseGate or StepAction.FailGate or StepAction.Hover or StepAction.Focus or StepAction.Blur or
            StepAction.DragTo or StepAction.Upload or StepAction.ScrollIntoView)
            await WaitForIdleAsync(page, timeout);
    }

    public static Task WaitForIdleAsync(IPage page, TimeSpan timeout) =>
        page.EvaluateAsync<bool>("t => window.__workbench ? window.__workbench.waitForIdle(t) : true", (int)timeout.TotalMilliseconds);

    private static async Task ExpectCallbackAsync(IPage page, string name, int? times, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        int count;
        while (true)
        {
            count = await page.EvaluateAsync<int>("n => window.__workbench.callbackCount(n)", name);
            var ok = times is null ? count > 0 : count == times;
            if (ok && (times != 0 || watch.Elapsed > TimeSpan.FromMilliseconds(200)))
                return;
            if (watch.Elapsed > timeout)
                break;
            await Task.Delay(50);
        }

        throw new PlaywrightException($"Expected {name} to be invoked {(times is null ? "at least once" : $"{times} time(s)")} but it was invoked {count} time(s).");
    }
}
