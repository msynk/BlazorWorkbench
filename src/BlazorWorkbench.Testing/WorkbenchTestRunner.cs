using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Diagnostics;
using BlazorWorkbench.Testing.Imaging;
using Microsoft.Playwright;
using PwColorScheme = Microsoft.Playwright.ColorScheme;
using DiagnosticSource = BlazorWorkbench.Diagnostics.DiagnosticSource;

namespace BlazorWorkbench.Testing;

/// <summary>Reads the preview's automation surface (<c>window.__workbench</c>).</summary>
public static class LoadState
{
    public static async Task<IReadOnlyList<DiagnosticDto>> DiagnosticsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<JsonElement>("() => window.__workbench ? window.__workbench.diagnostics : []");
        return json.Deserialize<List<DiagnosticDto>>(WorkbenchJson.Options) ?? [];
    }
}

/// <summary>
/// Executes scenarios in a real browser: render health, performance budgets, interaction tests, accessibility scans and
/// visual comparisons. The same engine backs <c>workbench test</c> and can be used from any .NET test project.
/// </summary>
public sealed class WorkbenchTestRunner(WorkbenchHost host, TestRunOptions options, ITestRunObserver? observer = null)
{
    private static readonly string[] ImpactOrder = ["minor", "moderate", "serious", "critical"];
    private readonly string _output = Path.GetFullPath(options.OutputDirectory);
    private readonly ConcurrentQueue<string> _diagnosticLines = new();
    private BaselineStore? _baselines;
    private HybridWebView? _hybrid;

    private bool Traces => options.TraceOnFailure && _hybrid is null;

    public async Task<TestRunReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await host.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var selected = Select(catalog);
        _baselines = options.BaselineDirectory is { } dir ? new BaselineStore(dir, BaselineStore.PlatformFor(catalog.HostingModel)) : null;
        if (options.Visual && _baselines is null)
            throw new WorkbenchInfrastructureException("Visual checks need a baseline directory (set 'baselines' in workbench.json or --baselines).");

        PrepareOutputDirectory();

        using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        IBrowser browser;
        if (host.Info.IsHybrid)
        {
            // Blazor Hybrid: drive the app's own WebView. It has one page, so scenarios run one at a time.
            browser = await HybridWebView.ConnectAsync(playwright, host.Info, cancellationToken).ConfigureAwait(false);
            _hybrid = await HybridWebView.OpenAsync(browser, host.PreviewBaseAddress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var browserType = options.Browser.ToLowerInvariant() switch
            {
                "chromium" or "chrome" => playwright.Chromium,
                "firefox" => playwright.Firefox,
                "webkit" => playwright.Webkit,
                _ => throw new WorkbenchInfrastructureException($"Unknown browser '{options.Browser}'. Use chromium, firefox or webkit."),
            };

            try
            {
                browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = !options.Headed }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                throw new WorkbenchInfrastructureException($"{options.Browser} could not be launched. Run 'workbench install-browsers' first.\n{ex.Message.Split('\n')[0]}", ex);
            }
        }

        await using var _ = browser.ConfigureAwait(false);
        var workers = _hybrid is null ? Math.Max(1, options.Workers) : 1;
        var report = new TestRunReport
        {
            StartedAt = DateTimeOffset.UtcNow,
            WorkbenchVersion = catalog.WorkbenchVersion,
            Environment = new RunEnvironment
            {
                Os = BaselineStore.CurrentOs,
                Platform = BaselineStore.PlatformFor(catalog.HostingModel),
                HostingModel = catalog.HostingModel,
                Runtime = RuntimeInformation.FrameworkDescription,
                Browser = _hybrid is null ? options.Browser : "webview",
                BrowserVersion = browser.Version,
                Ci = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")),
                HostUrl = host.BaseAddress.ToString(),
            },
            Settings = new RunSettings
            {
                Filter = options.Filter,
                Interactions = options.Interactions,
                Accessibility = options.Accessibility,
                Visual = options.Visual,
                Performance = options.Performance,
                Viewports = options.Viewports,
                Themes = options.Themes,
                Workers = workers,
                VisualThreshold = options.VisualThreshold,
                MaxDiffPixelRatio = options.MaxDiffPixelRatio,
                AccessibilityFailOn = options.AccessibilityFailOn,
                Retries = options.Retries,
            },
        };

        var results = new ConcurrentDictionary<string, ScenarioResult>();
        using var gate = new SemaphoreSlim(workers);
        var tasks = selected.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                observer?.OnScenarioStarted(item.Scenario.Id);
                ScenarioResult result;
                var attempt = 0;
                do
                {
                    attempt++;
                    result = await RunScenarioAsync(browser, catalog, item.Component, item.Scenario, cancellationToken).ConfigureAwait(false);
                    result.Attempts = attempt;
                }
                while (result.Status is CheckStatus.Failed or CheckStatus.Error && attempt <= options.Retries && !cancellationToken.IsCancellationRequested);

                if (attempt > 1 && result.Status == CheckStatus.Passed)
                    result.Message = $"Passed on attempt {attempt} (flaky).";
                results[item.Scenario.Id] = result;
                observer?.OnScenarioCompleted(result);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Deterministic order regardless of parallelism.
        report.Scenarios.AddRange(selected.Select(s => results[s.Scenario.Id]));
        report.FinishedAt = DateTimeOffset.UtcNow;
        report.Summarize();
        ReportWriter.WriteAll(report, _output);
        await File.WriteAllLinesAsync(Path.Combine(_output, "diagnostics.jsonl"), _diagnosticLines.Order(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        return report;
    }

    internal IReadOnlyList<(ComponentDto Component, ScenarioDto Scenario)> Select(CatalogDto catalog)
    {
        var items = catalog.Components.SelectMany(c => c.Scenarios.Select(s => (c, s)));
        if (!string.IsNullOrWhiteSpace(options.Filter))
        {
            var terms = options.Filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            items = items.Where(x => terms.Any(t => t.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)
                ? x.s.Tags.Concat(x.c.Tags).Contains(t[4..], StringComparer.OrdinalIgnoreCase)
                : Catalog.Glob.IsMatch(x.s.Id, t) || Catalog.Glob.IsMatch(x.c.Id, t) || Catalog.Glob.IsMatch($"{x.c.Title}/{x.s.Name}", t)));
        }

        if (options.OnlyScenarios is { } only)
            items = items.Where(x => only.Contains(x.s.Id));
        return items.ToList();
    }

    private void PrepareOutputDirectory()
    {
        Directory.CreateDirectory(_output);
        foreach (var sub in new[] { "screenshots", "traces", "failures" })
        {
            var path = Path.Combine(_output, sub);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private sealed record Variant(ViewportPreset Viewport, ThemeDto Theme)
    {
        public string Name => $"{Viewport.Name}-{Theme.Name}";
    }

    private List<Variant> Variants(CatalogDto catalog, ScenarioDto scenario)
    {
        ViewportPreset FindViewport(string name) => catalog.Viewports.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                                                    ?? throw new WorkbenchInfrastructureException($"Viewport '{name}' is not defined by the host.");
        ThemeDto FindTheme(string name) => catalog.Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                                           ?? throw new WorkbenchInfrastructureException($"Theme '{name}' is not defined by the host.");

        var viewports = scenario.Visual.Viewports.Count > 0 ? scenario.Visual.Viewports
            : scenario.Environment.Viewport is { } v ? [v]
            : options.Viewports.Count > 0 ? options.Viewports
            : [catalog.Viewports.FirstOrDefault(x => x.Name == "laptop")?.Name ?? catalog.Viewports[0].Name];
        var themes = scenario.Visual.Themes.Count > 0 ? scenario.Visual.Themes
            : scenario.Environment.Theme is { } t ? [t]
            : options.Themes.Count > 0 ? options.Themes
            : [catalog.Themes[0].Name];
        return viewports.SelectMany(vp => themes.Select(th => new Variant(FindViewport(vp), FindTheme(th)))).ToList();
    }

    private async Task<ScenarioResult> RunScenarioAsync(IBrowser browser, CatalogDto catalog, ComponentDto component, ScenarioDto scenario, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var result = new ScenarioResult { ScenarioId = scenario.Id, ComponentId = component.Id, Name = $"{component.Title} / {scenario.Name}" };
        if (scenario.SkipReason is { } skip)
        {
            result.Status = CheckStatus.Skipped;
            result.Message = skip;
            return result;
        }

        var invalid = catalog.Issues.Where(i => i.Severity == Catalog.IssueSeverity.Error && string.Equals(i.ScenarioId, scenario.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (invalid.Count > 0)
        {
            result.Checks.Add(new CheckResult
            {
                Kind = CheckKind.Render,
                Name = "validation",
                Status = CheckStatus.Failed,
                Message = string.Join("; ", invalid.Select(i => $"{i.Code}: {i.Message}")),
            });
            result.Conclude();
            return result;
        }

        try
        {
            var variants = Variants(catalog, scenario);
            var primary = variants[0];

            // 1. Render health, performance, accessibility and the primary visual variant.
            await using (var lease = await OpenPageAsync(browser, primary, scenario).ConfigureAwait(false))
            {
                var page = lease.Page;
                var (resources, detach) = TrackResources(page);
                LoadResult load;
                try
                {
                    load = await LoadAsync(page, scenario, primary).ConfigureAwait(false);
                }
                finally
                {
                    detach();
                }

                result.ReadyMs = load.ReadyMs;
                result.DomUpdates = load.DomUpdates;
                var diagnostics = load.Diagnostics.Where(d => d.Source != DiagnosticSource.Resource).Concat(resources).ToList();
                result.Diagnostics.AddRange(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Take(100));
                Export(scenario.Id, "render", diagnostics);
                var render = RenderCheck(load, diagnostics);
                if (load.State == "ready" && _hybrid is null)
                    await AddServerMetricsAsync(page, render).ConfigureAwait(false);
                result.Checks.Add(render);

                if (options.Performance && BudgetCheck(scenario, load) is { } budget)
                    result.Checks.Add(budget);

                if (load.State == "ready")
                {
                    if (options.Accessibility)
                        result.Checks.Add(await AccessibilityCheckAsync(page, scenario).ConfigureAwait(false));
                    if (options.Visual)
                        result.Checks.Add(await VisualCheckAsync(page, scenario, primary, primary.Name).ConfigureAwait(false));
                }
            }

            // 2. Additional visual variants.
            if (options.Visual && !scenario.Visual.Skip)
            {
                foreach (var variant in variants.Skip(1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var lease = await OpenPageAsync(browser, variant, scenario).ConfigureAwait(false);
                    var page = lease.Page;
                    var load = await LoadAsync(page, scenario, variant).ConfigureAwait(false);
                    result.Checks.Add(load.State == "ready"
                        ? await VisualCheckAsync(page, scenario, variant, variant.Name).ConfigureAwait(false)
                        : new CheckResult { Kind = CheckKind.Visual, Name = "visual", Variant = variant.Name, Status = CheckStatus.Failed, Message = $"Preview did not become ready ({load.State})." });
                }
            }

            // 3. Interaction tests, each against a fresh activation.
            if (options.Interactions)
            {
                foreach (var test in scenario.Interactions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Checks.Add(await InteractionCheckAsync(browser, scenario, primary, test).ConfigureAwait(false));
                }
            }
        }
        catch (WorkbenchInfrastructureException ex)
        {
            result.Checks.Add(new CheckResult { Kind = CheckKind.Render, Name = "infrastructure", Status = CheckStatus.Error, Message = ex.Message });
        }
        catch (PlaywrightException ex)
        {
            result.Checks.Add(new CheckResult { Kind = CheckKind.Render, Name = "infrastructure", Status = CheckStatus.Error, Message = FirstLine(ex.Message) });
        }

        result.DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds);
        result.Conclude();
        return result;
    }

    /// <summary>A page for one load: a fresh browser context, or the Blazor Hybrid app's WebView emulating the variant.</summary>
    private sealed class PageLease(IPage page, IBrowserContext context, Func<ValueTask> release) : IAsyncDisposable
    {
        public IPage Page => page;
        public IBrowserContext Context => context;
        public ValueTask DisposeAsync() => release();
    }

    private async Task<PageLease> OpenPageAsync(IBrowser browser, Variant variant, ScenarioDto scenario)
    {
        if (_hybrid is { } hybrid)
        {
            var page = await hybrid.AcquireAsync(variant.Viewport, variant.Theme, scenario.Environment.Culture ?? "en-US").ConfigureAwait(false);
            return new PageLease(page, page.Context, () =>
            {
                hybrid.Release();
                return ValueTask.CompletedTask;
            });
        }

        var context = await NewContextAsync(browser, variant, scenario).ConfigureAwait(false);
        try
        {
            return new PageLease(await context.NewPageAsync().ConfigureAwait(false), context, context.DisposeAsync);
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private Task<IBrowserContext> NewContextAsync(IBrowser browser, Variant variant, ScenarioDto scenario) =>
        browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = variant.Viewport.Width, Height = variant.Viewport.Height },
            DeviceScaleFactor = (float)variant.Viewport.DeviceScaleFactor,
            ColorScheme = variant.Theme.ColorScheme == BlazorWorkbench.ColorScheme.Dark ? PwColorScheme.Dark : PwColorScheme.Light,
            Locale = scenario.Environment.Culture ?? "en-US",
            TimezoneId = "UTC",
            ReducedMotion = ReducedMotion.Reduce,
        });

    private sealed record LoadResult(string State, double? ReadyMs, int? DomUpdates, int Pending, double LoadMs, IReadOnlyList<DiagnosticDto> Diagnostics);

    private async Task<LoadResult> LoadAsync(IPage page, ScenarioDto scenario, Variant variant)
    {
        var watch = Stopwatch.StartNew();
        await page.GotoAsync(host.PreviewUrl(scenario.Id, variant.Theme.Name).ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)options.ReadyTimeout.TotalMilliseconds });
        try
        {
            await page.WaitForFunctionAsync("() => ['ready', 'error'].includes(document.documentElement.getAttribute('data-workbench-state'))", null,
                new PageWaitForFunctionOptions { Timeout = (float)options.ReadyTimeout.TotalMilliseconds, PollingInterval = 50 });
        }
        catch (TimeoutException)
        {
            // Reported below as a readiness failure.
        }

        var info = await page.EvaluateAsync<JsonElement>("() => ({ state: window.__workbench ? window.__workbench.state : 'missing', metrics: window.__workbench ? window.__workbench.metrics() : null })");
        var state = info.GetProperty("state").GetString() ?? "missing";
        double? ready = null;
        int? updates = null;
        var pending = 0;
        if (info.TryGetProperty("metrics", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            if (m.TryGetProperty("readyMs", out var r) && r.ValueKind == JsonValueKind.Number) ready = r.GetDouble();
            if (m.TryGetProperty("domUpdates", out var d) && d.ValueKind == JsonValueKind.Number) updates = d.GetInt32();
            if (m.TryGetProperty("pending", out var p) && p.ValueKind == JsonValueKind.Number) pending = p.GetInt32();
        }

        if (state == "ready")
            await page.EvaluateAsync("() => document.fonts.ready.then(() => true)");
        var diagnostics = state == "missing" ? [] : await LoadState.DiagnosticsAsync(page);
        return new LoadResult(state, ready, updates, pending, watch.Elapsed.TotalMilliseconds, diagnostics);
    }

    private static (List<DiagnosticDto> Items, Action Detach) TrackResources(IPage page)
    {
        var list = new List<DiagnosticDto>();
        void Add(string message) { lock (list) list.Add(new DiagnosticDto(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), DiagnosticSource.Resource, DiagnosticSeverity.Error, "", message, null)); }
        // Exceptions must not escape Playwright event handlers: they would tear down the driver connection.
        EventHandler<IResponse> onResponse = (_, response) =>
        {
            try
            {
                if (response.Status >= 400 && !response.Url.Contains("/_workbench/api/", StringComparison.Ordinal))
                    Add($"{response.Request.ResourceType} {Redactor.Default.RedactUrl(response.Url)} → {response.Status}");
            }
            catch (PlaywrightException)
            {
            }
        };
        EventHandler<IRequest> onFailed = (_, request) =>
        {
            try
            {
                if (request.Failure is { } failure && !failure.Contains("ERR_ABORTED", StringComparison.Ordinal))
                    Add($"{request.ResourceType} {Redactor.Default.RedactUrl(request.Url)} failed: {failure}");
            }
            catch (PlaywrightException)
            {
            }
        };
        page.Response += onResponse;
        page.RequestFailed += onFailed;
        // A Blazor Hybrid WebView page is shared by every load, so handlers are removed after each load.
        return (list, () =>
        {
            page.Response -= onResponse;
            page.RequestFailed -= onFailed;
        });
    }

    private CheckResult RenderCheck(LoadResult load, IReadOnlyList<DiagnosticDto> diagnostics)
    {
        var check = new CheckResult
        {
            Kind = CheckKind.Render,
            Name = "render",
            DurationMs = Math.Round(load.LoadMs),
            Metrics = new Dictionary<string, double>(),
        };
        if (load.ReadyMs is { } readyMs) check.Metrics["readyMs"] = readyMs;
        if (load.DomUpdates is { } updates) check.Metrics["domUpdates"] = updates;

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        (check.Status, check.Message) = load.State switch
        {
            "ready" when errors.Count == 0 => (CheckStatus.Passed, null),
            "ready" => (CheckStatus.Failed, $"{errors.Count} error diagnostic(s): [{errors[0].Source}] {errors[0].Message}"),
            "error" => (CheckStatus.Failed, errors.FirstOrDefault() is { } e ? $"Render failed: [{e.Source}] {e.Message}" : "The preview reported an error."),
            "missing" => (CheckStatus.Error, "The preview bridge did not load (is this a Blazor Workbench host?)."),
            _ => (CheckStatus.Failed, $"Not ready after {options.ReadyTimeout.TotalSeconds:0} s ({load.Pending} pending operation(s)). Use .ReadyWhen(selector) or gates for intentionally pending states."),
        };
        return check;
    }

    private static CheckResult? BudgetCheck(ScenarioDto scenario, LoadResult load)
    {
        var budget = scenario.Budget;
        if (budget.ReadyWithinMs is null && budget.MaxDomUpdates is null)
            return null;
        var problems = new List<string>();
        if (budget.ReadyWithinMs is { } readyBudget && load.ReadyMs is { } ready && ready > readyBudget)
            problems.Add($"ready after {ready:0} ms (budget {readyBudget:0} ms)");
        if (budget.MaxDomUpdates is { } maxUpdates && load.DomUpdates is { } updates && updates > maxUpdates)
            problems.Add($"{updates} DOM update batches (budget {maxUpdates})");
        return new CheckResult
        {
            Kind = CheckKind.Performance,
            Name = "budget",
            Status = problems.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed,
            Message = problems.Count == 0 ? null : "Budget exceeded: " + string.Join(", ", problems),
            Metrics = new Dictionary<string, double> { ["readyMs"] = load.ReadyMs ?? -1, ["domUpdates"] = load.DomUpdates ?? -1 },
        };
    }

    private async Task<CheckResult> AccessibilityCheckAsync(IPage page, ScenarioDto scenario)
    {
        var check = new CheckResult { Kind = CheckKind.Accessibility, Name = "axe" };
        if (scenario.Accessibility.Skip)
        {
            check.Status = CheckStatus.Skipped;
            check.Message = scenario.Accessibility.SkipReason;
            return check;
        }

        var watch = Stopwatch.StartNew();
        var outcome = await page.EvaluateAsync<JsonElement>("o => window.__workbench.runAxe(o)",
            new { disabledRules = scenario.Accessibility.DisabledRules, tags = options.AccessibilityTags });
        check.DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds);
        var failIndex = Array.IndexOf(ImpactOrder, options.AccessibilityFailOn.ToLowerInvariant());
        if (failIndex < 0) failIndex = 2;

        var findings = new List<AccessibilityFinding>();
        foreach (var (property, review) in new[] { ("violations", false), ("incomplete", true) })
        {
            if (!outcome.TryGetProperty(property, out var items))
                continue;
            foreach (var item in items.EnumerateArray())
            {
                var impact = item.TryGetProperty("impact", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                var blocking = !review && Array.IndexOf(ImpactOrder, impact ?? "") >= failIndex;
                findings.Add(new AccessibilityFinding(
                    item.GetProperty("id").GetString()!,
                    impact,
                    item.GetProperty("help").GetString() ?? "",
                    item.TryGetProperty("helpUrl", out var url) ? url.GetString() : null,
                    item.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(t => t.GetString()!).ToArray() : [],
                    item.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("target").GetString()!).ToArray(),
                    blocking,
                    review));
            }
        }

        check.Findings = findings;
        var blockingCount = findings.Count(f => f.Blocking);
        check.Status = blockingCount > 0 ? CheckStatus.Failed : CheckStatus.Passed;
        var violations = findings.Count(f => !f.NeedsReview);
        check.Message = blockingCount > 0
            ? $"{blockingCount} blocking violation(s) (impact ≥ {options.AccessibilityFailOn}): {string.Join(", ", findings.Where(f => f.Blocking).Select(f => f.RuleId).Distinct())}"
            : violations > 0 ? $"{violations} non-blocking violation(s)" : null;
        if (outcome.TryGetProperty("engine", out var engine))
            check.Metrics = new Dictionary<string, double> { ["passes"] = outcome.TryGetProperty("passes", out var p) ? p.GetDouble() : 0 };
        _ = engine;
        return check;
    }

    private async Task<CheckResult> VisualCheckAsync(IPage page, ScenarioDto scenario, Variant variant, string variantName)
    {
        var check = new CheckResult { Kind = CheckKind.Visual, Name = "visual", Variant = variantName };
        if (scenario.Visual.Skip)
        {
            check.Status = CheckStatus.Skipped;
            check.Message = scenario.Visual.SkipReason;
            return check;
        }

        var watch = Stopwatch.StartNew();
        var masks = scenario.Visual.IgnoreSelectors.Select(s => page.Locator(s)).ToList();
        byte[] png;
        if (scenario.Visual.FullPage || scenario.Layout == ScenarioLayout.Fullscreen)
        {
            png = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = scenario.Visual.FullPage,
                Animations = ScreenshotAnimations.Disabled,
                Caret = ScreenshotCaret.Hide,
                Mask = masks,
                Type = ScreenshotType.Png,
            });
        }
        else
        {
            png = await page.Locator("#workbench-preview-root").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                Caret = ScreenshotCaret.Hide,
                Mask = masks,
                Type = ScreenshotType.Png,
            });
        }

        var actual = PngImage.Decode(png);
        var store = _baselines!;
        var relative = store.RelativePath(scenario.Id, variantName);
        var actualRelative = "screenshots/" + BaselineStore.ScenarioPath(scenario.Id, variantName);
        File.WriteAllBytes(EnsureDirectory(Path.Combine(_output, actualRelative)), png);

        var visual = new VisualComparison
        {
            Variant = variantName,
            Viewport = variant.Viewport.Name,
            Theme = variant.Theme.Name,
            BaselinePath = relative,
            ActualPath = actualRelative,
            Width = actual.Width,
            Height = actual.Height,
        };
        check.Visual = visual;

        var baseline = store.TryLoad(scenario.Id, variantName);
        if (baseline is null)
        {
            visual.DiffRatio = 1;
            check.Status = options.FailOnMissingBaseline ? CheckStatus.Missing : CheckStatus.Skipped;
            check.Message = $"No approved baseline for {variantName}. Review the screenshot, then run 'workbench approve --filter \"{scenario.Id}\"'.";
            check.DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds);
            return check;
        }

        var threshold = scenario.Visual.Threshold ?? options.VisualThreshold;
        var maxRatio = scenario.Visual.MaxDiffPixelRatio ?? options.MaxDiffPixelRatio;
        var comparison = ImageComparer.Compare(baseline, actual, threshold, options.IgnoreAntiAliasing);
        visual.DiffRatio = Math.Round(comparison.DiffRatio, 6);
        visual.DiffPixels = comparison.DiffPixels;
        visual.AntiAliasedPixels = comparison.AntiAliasedPixels;
        visual.SizeMismatch = comparison.SizeMismatch;

        var passed = !comparison.SizeMismatch && comparison.DiffRatio <= maxRatio;
        if (!passed)
        {
            var diffRelative = Path.ChangeExtension(actualRelative, ".diff.png");
            comparison.Diff.Save(Path.Combine(_output, diffRelative));
            visual.DiffPath = diffRelative;
        }

        check.Status = passed ? CheckStatus.Passed : CheckStatus.Failed;
        check.Message = passed ? null : comparison.SizeMismatch
            ? $"Size changed from {baseline.Width}×{baseline.Height} to {actual.Width}×{actual.Height}."
            : $"{comparison.DiffPixels} pixels ({comparison.DiffRatio:P3}) differ (tolerance {maxRatio:P3}, threshold {threshold}).";
        check.DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds);
        return check;
    }

    private async Task<CheckResult> InteractionCheckAsync(IBrowser browser, ScenarioDto scenario, Variant variant, InteractionTest test)
    {
        var check = new CheckResult { Kind = CheckKind.Interaction, Name = test.Name, Steps = [] };
        var watch = Stopwatch.StartNew();
        await using var lease = await OpenPageAsync(browser, variant, scenario).ConfigureAwait(false);
        var context = lease.Context;
        if (Traces)
            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Title = $"{scenario.Id} › {test.Name}" });

        var page = lease.Page;
        var load = await LoadAsync(page, scenario, variant).ConfigureAwait(false);
        var failed = load.State != "ready";
        if (failed)
            check.Message = $"Preview did not become ready ({load.State}).";

        for (var i = 0; i < test.Steps.Count; i++)
        {
            var step = test.Steps[i];
            if (failed)
            {
                check.Steps.Add(new StepOutcome(i, step.ToString(), CheckStatus.Skipped, null, 0));
                continue;
            }

            var stepWatch = Stopwatch.StartNew();
            try
            {
                await PlaywrightSteps.RunAsync(page, step, options.StepTimeout, async name =>
                {
                    if (options.Visual)
                    {
                        var checkpoint = await VisualCheckAsync(page, scenario, variant, $"{variant.Name}-{Naming.ToKebabCase(name)}").ConfigureAwait(false);
                        if (checkpoint.Status is CheckStatus.Failed or CheckStatus.Missing)
                            throw new PlaywrightException($"Visual checkpoint '{name}': {checkpoint.Message}");
                    }
                }).ConfigureAwait(false);
                check.Steps.Add(new StepOutcome(i, step.ToString(), CheckStatus.Passed, null, Math.Round(stepWatch.Elapsed.TotalMilliseconds)));
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                failed = true;
                var message = FirstLine(ex.Message);
                check.Steps.Add(new StepOutcome(i, step.ToString(), CheckStatus.Failed, message, Math.Round(stepWatch.Elapsed.TotalMilliseconds)));
                check.Message = $"Step {i + 1} ({step.Action}) failed: {message}";
            }
        }

        check.DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds);
        check.Status = failed ? CheckStatus.Failed : CheckStatus.Passed;
        try
        {
            Export(scenario.Id, $"interaction:{test.Name}", await LoadState.DiagnosticsAsync(page).ConfigureAwait(false));
        }
        catch (PlaywrightException)
        {
        }

        if (!failed && options.Performance && scenario.Budget.InteractionWithinMs is { } limit && check.DurationMs > limit)
        {
            check.Status = CheckStatus.Failed;
            check.Message = $"Interaction took {check.DurationMs:0} ms (budget {limit:0} ms).";
        }

        if (check.Status == CheckStatus.Failed)
        {
            var safe = Naming.ToKebabCase(test.Name);
            var shot = Path.Combine("failures", scenario.Id, safe + ".png").Replace('\\', '/');
            try
            {
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(_output, shot), Animations = ScreenshotAnimations.Disabled });
                check.Metrics = null;
                check.Steps.Add(new StepOutcome(test.Steps.Count, $"screenshot: {shot}", CheckStatus.Skipped, null, 0));
            }
            catch (PlaywrightException)
            {
            }

            if (Traces)
            {
                var trace = Path.Combine("traces", scenario.Id, safe + ".zip").Replace('\\', '/');
                await context.Tracing.StopAsync(new TracingStopOptions { Path = EnsureDirectory(Path.Combine(_output, trace)) });
                check.Steps.Add(new StepOutcome(test.Steps.Count + 1, $"trace: {trace} (open with 'npx playwright show-trace')", CheckStatus.Skipped, null, 0));
            }
        }
        else if (Traces)
        {
            await context.Tracing.StopAsync();
        }

        return check;
    }

    private void Export(string scenarioId, string check, IEnumerable<DiagnosticDto> diagnostics)
    {
        foreach (var d in diagnostics)
            _diagnosticLines.Enqueue(JsonSerializer.Serialize(new { scenarioId, check, d.Timestamp, d.SessionId, d.Source, d.Severity, d.Message, d.Details }, WorkbenchJson.Options));
    }

    private static async Task AddServerMetricsAsync(IPage page, CheckResult check)
    {
        try
        {
            var metrics = await page.EvaluateAsync<JsonElement>("() => window.__workbench.serverMetrics ? window.__workbench.serverMetrics() : null").ConfigureAwait(false);
            if (metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("server", out var server))
            {
                check.Metrics ??= [];
                check.Metrics["serverActivities"] = server.GetProperty("activities").GetDouble();
                check.Metrics["serverMs"] = server.GetProperty("totalMs").GetDouble();
                check.Metrics["serverMaxMs"] = server.GetProperty("maxMs").GetDouble();
            }
        }
        catch (PlaywrightException)
        {
            // Metrics are best effort; they never fail a check.
        }
    }

    private static string EnsureDirectory(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        return file;
    }

    private static string FirstLine(string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = lines.FirstOrDefault() ?? message;
        // Playwright assertion messages put the useful detail on the following lines.
        var detail = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Expected", StringComparison.Ordinal) || l.StartsWith("Received", StringComparison.Ordinal) || l.StartsWith("Locator", StringComparison.Ordinal));
        return detail is null ? first : $"{first} {detail}";
    }
}
