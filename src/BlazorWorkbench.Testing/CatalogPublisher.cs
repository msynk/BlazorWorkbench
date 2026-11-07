using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Documentation;
using Microsoft.Playwright;

namespace BlazorWorkbench.Testing;

/// <summary>Options of <see cref="CatalogPublisher"/>.</summary>
public sealed class PublishOptions
{
    public required string OutputDirectory { get; init; }

    /// <summary>Capture a screenshot of every scenario (requires a browser).</summary>
    public bool Screenshots { get; init; } = true;

    /// <summary>Components or scenarios carrying any of these tags are left out of the published catalog.</summary>
    public IReadOnlyList<string> ExcludeTags { get; init; } = ["internal"];

    public string? Title { get; init; }
    public string? Version { get; init; }
    public string Viewport { get; init; } = "laptop";
    public int Workers { get; init; } = 4;
}

/// <summary>
/// Publishes a static, self-contained component catalog: an index with search, one documentation page per component
/// (parameters, Markdown docs, scenarios with screenshots and Razor usage) and a coverage report. The output contains
/// no host URLs, absolute paths, environment details or diagnostics, so it can be hosted on any static file server.
/// </summary>
public static class CatalogPublisher
{
    public static async Task<string> PublishAsync(WorkbenchHost host, PublishOptions options, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var catalog = await host.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var output = Path.GetFullPath(options.OutputDirectory);
        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(Path.Combine(output, "components"));

        bool Excluded(IEnumerable<string> tags) => tags.Any(t => options.ExcludeTags.Contains(t, StringComparer.OrdinalIgnoreCase));
        var components = catalog.Components
            .Where(c => !Excluded(c.Tags))
            .Select(c => new { Component = c, Scenarios = c.Scenarios.Where(s => !Excluded(s.Tags) && !s.IsGenerated || c.Scenarios.All(x => x.IsGenerated)).ToList() })
            .ToList();

        var screenshots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.Screenshots)
        {
            Directory.CreateDirectory(Path.Combine(output, "screenshots"));
            using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            // Blazor Hybrid hosts render in the app's own WebView (one page, so one screenshot at a time).
            var hybrid = host.Info.IsHybrid;
            await using var browser = hybrid
                ? await HybridWebView.ConnectAsync(playwright, host.Info, cancellationToken).ConfigureAwait(false)
                : await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
            var webView = hybrid ? await HybridWebView.OpenAsync(browser, host.PreviewBaseAddress, cancellationToken).ConfigureAwait(false) : null;
            var viewport = catalog.Viewports.FirstOrDefault(v => v.Name == options.Viewport) ?? catalog.Viewports[0];
            var theme = catalog.Themes[0];
            using var gate = new SemaphoreSlim(webView is null ? Math.Max(1, options.Workers) : 1);
            var work = components.SelectMany(c => c.Scenarios.Where(s => s.SkipReason is null)).Select(async scenario =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                IBrowserContext? context = null;
                var acquired = false;
                try
                {
                    IPage page;
                    if (webView is not null)
                    {
                        page = await webView.AcquireAsync(viewport, theme, scenario.Environment.Culture ?? "en-US").ConfigureAwait(false);
                        acquired = true;
                    }
                    else
                    {
                        context = await browser.NewContextAsync(new BrowserNewContextOptions
                        {
                            ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height },
                            ReducedMotion = ReducedMotion.Reduce,
                            TimezoneId = "UTC",
                            Locale = scenario.Environment.Culture ?? "en-US",
                        }).ConfigureAwait(false);
                        page = await context.NewPageAsync().ConfigureAwait(false);
                    }

                    await page.GotoAsync(host.PreviewUrl(scenario.Id).ToString()).ConfigureAwait(false);
                    await page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-workbench-state') === 'ready'", null,
                        new PageWaitForFunctionOptions { Timeout = 20_000 }).ConfigureAwait(false);
                    var file = Path.Combine(output, "screenshots", scenario.Id + ".png");
                    if (scenario.Layout == ScenarioLayout.Fullscreen)
                        await page.ScreenshotAsync(new PageScreenshotOptions { Path = file, Animations = ScreenshotAnimations.Disabled }).ConfigureAwait(false);
                    else
                        await page.Locator("#workbench-preview-root").ScreenshotAsync(new LocatorScreenshotOptions { Path = file, Animations = ScreenshotAnimations.Disabled }).ConfigureAwait(false);
                    lock (screenshots)
                        screenshots.Add(scenario.Id);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or WorkbenchInfrastructureException)
                {
                    log?.Invoke($"warning: screenshot of {scenario.Id} failed: {ex.Message.Split('\n')[0]}");
                }
                finally
                {
                    if (context is not null)
                        await context.DisposeAsync().ConfigureAwait(false);
                    else if (acquired)
                        webView!.Release();
                    gate.Release();
                }
            });
            await Task.WhenAll(work).ConfigureAwait(false);
        }

        var title = options.Title ?? catalog.Title;
        File.WriteAllText(Path.Combine(output, "style.css"), Css);

        foreach (var entry in components)
        {
            var page = ComponentPage(entry.Component, entry.Scenarios, screenshots, title);
            File.WriteAllText(Path.Combine(output, "components", entry.Component.Id + ".html"), page);
        }

        var search = components.Select(c => new
        {
            id = c.Component.Id,
            title = c.Component.Title,
            name = c.Component.Name,
            summary = c.Component.Summary ?? c.Component.Description,
            tags = c.Component.Tags,
            parameters = c.Component.Parameters.Select(p => p.Name),
            scenarios = c.Scenarios.Select(s => s.Name),
        });
        File.WriteAllText(Path.Combine(output, "search.json"), JsonSerializer.Serialize(search, WorkbenchJson.Options));

        var coverage = Coverage(components.Select(c => c.Component).ToList());
        File.WriteAllText(Path.Combine(output, "coverage.json"), JsonSerializer.Serialize(coverage, WorkbenchJson.Indented));
        File.WriteAllText(Path.Combine(output, "index.html"), IndexPage(title, options.Version, components.Select(c => (c.Component, c.Scenarios.Count)).ToList(), coverage, screenshots.Count));
        log?.Invoke($"Published {components.Count} components and {screenshots.Count} screenshots to {output}");
        return output;
    }

    public sealed record CoverageReport(int Components, int WithScenarios, int Documented, int Parameters, int DocumentedParameters, IReadOnlyList<string> MissingDocs, IReadOnlyList<string> MissingScenarios);

    public static CoverageReport Coverage(IReadOnlyList<ComponentDto> components)
    {
        var parameters = components.SelectMany(c => c.Parameters.Where(p => p.Kind != ParameterKind.Cascading)).ToList();
        return new CoverageReport(
            components.Count,
            components.Count(c => c.Scenarios.Any(s => !s.IsGenerated)),
            components.Count(c => c.Summary is not null || c.Documentation is not null || c.Description is not null),
            parameters.Count,
            parameters.Count(p => p.Summary is not null),
            components.Where(c => c.Summary is null && c.Documentation is null && c.Description is null).Select(c => c.Title).ToList(),
            components.Where(c => c.Scenarios.All(s => s.IsGenerated)).Select(c => c.Title).ToList());
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Layout(string title, string body, string root) => $$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(title)}}</title><link rel="stylesheet" href="{{root}}style.css"></head>
        <body><main>{{body}}</main></body></html>
        """;

    private static string IndexPage(string title, string? version, IReadOnlyList<(ComponentDto Component, int Scenarios)> components, CoverageReport coverage, int screenshots)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<h1>{E(title)}</h1><p class=\"m\">{components.Count} components{(version is null ? "" : $" · version {E(version)}")} · generated by Blazor Workbench</p>");
        sb.Append("<p><label for=\"q\">Search</label> <input id=\"q\" type=\"search\" placeholder=\"Component, parameter or tag\" autocomplete=\"off\"></p>");
        foreach (var group in components.GroupBy(c => c.Component.Title.Contains('/') ? c.Component.Title.Split('/')[0] : "Components").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<h2>{E(group.Key)}</h2><ul class=\"cards\">");
            foreach (var (component, count) in group)
            {
                var keywords = string.Join(' ', new[] { component.Name, component.Title }.Concat(component.Tags).Concat(component.Parameters.Select(p => p.Name))).ToLowerInvariant();
                sb.Append(CultureInfo.InvariantCulture, $"<li data-k=\"{E(keywords)}\"><a href=\"components/{E(component.Id)}.html\"><strong>{E(component.Title.Split('/')[^1])}</strong></a><span class=\"m\">{E(component.Summary ?? component.Description)}</span><span class=\"m\">{count} scenario(s)</span>{(component.DeprecationMessage is null ? "" : "<span class=\"badge\">deprecated</span>")}</li>");
            }

            sb.Append("</ul>");
        }

        static string Pct(int part, int total) => total == 0 ? "100%" : ((double)part / total).ToString("P0", CultureInfo.InvariantCulture);
        sb.Append(CultureInfo.InvariantCulture, $"<h2>Coverage</h2><table><tr><th>Components with scenarios</th><td>{Pct(coverage.WithScenarios, coverage.Components)}</td></tr><tr><th>Documented components</th><td>{Pct(coverage.Documented, coverage.Components)}</td></tr><tr><th>Documented parameters</th><td>{Pct(coverage.DocumentedParameters, coverage.Parameters)}</td></tr><tr><th>Screenshots</th><td>{screenshots}</td></tr></table>");
        if (coverage.MissingDocs.Count > 0)
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"m\">Missing documentation: {E(string.Join(", ", coverage.MissingDocs))}</p>");
        sb.Append("""
            <script>
            const q=document.getElementById('q');
            q.addEventListener('input',()=>{const t=q.value.toLowerCase().trim();document.querySelectorAll('[data-k]').forEach(li=>li.hidden=t&&!t.split(/\s+/).every(w=>li.dataset.k.includes(w)));});
            </script>
            """);
        return Layout(title, sb.ToString(), "");
    }

    private static string ComponentPage(ComponentDto c, IReadOnlyList<ScenarioDto> scenarios, HashSet<string> screenshots, string title)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<p><a href=\"../index.html\">← {E(title)}</a></p><p class=\"m\">{E(c.Title.Replace("/", " / ", StringComparison.Ordinal))}</p><h1>{E(c.Name)}</h1>");
        sb.Append(CultureInfo.InvariantCulture, $"<p class=\"m\"><code>{E(c.FullName)}</code> · {E(c.AssemblyName)}{(c.SourcePath is null ? "" : $" · <code>{E(c.SourcePath)}</code>")}</p>");
        if (c.DeprecationMessage is { } deprecated)
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"warn\"><strong>Deprecated.</strong> {E(deprecated)}</p>");
        if ((c.Description ?? c.Summary) is { } summary)
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"lead\">{E(summary)}</p>");
        if (c.Documentation is { } docs)
            sb.Append(SafeMarkdown.ToHtml(docs));

        sb.Append("<h2>Parameters</h2>");
        if (c.Parameters.Count == 0)
        {
            sb.Append("<p class=\"m\">No parameters.</p>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>Name</th><th>Type</th><th>Default</th><th>Description</th></tr></thead><tbody>");
            foreach (var p in c.Parameters)
                sb.Append(CultureInfo.InvariantCulture, $"<tr><td><code>{E(p.Name)}</code>{(p.IsRequired ? " <span class=\"badge\">required</span>" : "")}{(p.Kind == ParameterKind.Cascading ? " <span class=\"badge\">cascading</span>" : "")}{(p.IsObsolete ? " <span class=\"badge\">obsolete</span>" : "")}</td><td><code>{E(p.TypeName)}</code></td><td>{E(p.DefaultValueDisplay ?? "—")}</td><td>{E(p.Summary)}</td></tr>");
            sb.Append("</tbody></table>");
        }

        if (c.Dependencies.Count > 0)
            sb.Append(CultureInfo.InvariantCulture, $"<h2>Dependencies</h2><p>{string.Join(", ", c.Dependencies.Select(d => $"<code>{E(d)}</code>"))}</p>");

        sb.Append("<h2>Examples</h2>");
        foreach (var s in scenarios)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<section class=\"example\" id=\"{E(s.Key)}\"><h3>{E(s.Name)}</h3>");
            if (s.Description is { } d) sb.Append(CultureInfo.InvariantCulture, $"<p class=\"m\">{E(d)}</p>");
            if (screenshots.Contains(s.Id)) sb.Append(CultureInfo.InvariantCulture, $"<img loading=\"lazy\" src=\"../screenshots/{E(s.Id)}.png\" alt=\"{E(c.Name)} – {E(s.Name)}\">");
            if (s.Snippet is { } snippet) sb.Append(CultureInfo.InvariantCulture, $"<pre><code>{E(snippet)}</code></pre>");
            sb.Append("</section>");
        }

        return Layout($"{c.Name} · {title}", sb.ToString(), "../");
    }

    private const string Css = """
        :root{color-scheme:light dark;--b:#d6d6de;--m:#5f5f6b;--a:#4f46e5}
        body{margin:0;font:15px/1.55 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        main{max-width:1000px;margin:0 auto;padding:1.5rem 1rem 4rem}
        a{color:var(--a)}.m{color:var(--m)}.lead{font-size:1.1rem}.warn{border-left:4px solid #b54708;padding:.4rem .8rem}
        code,pre{font-family:ui-monospace,Consolas,monospace;font-size:13px}pre{background:rgba(127,127,127,.12);padding:.75rem;border-radius:6px;overflow:auto}
        table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid var(--b);padding:.35rem .5rem;text-align:start;vertical-align:top}
        .badge{font-size:11px;border:1px solid var(--b);border-radius:999px;padding:0 .4rem;color:var(--m)}
        .cards{list-style:none;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:.75rem}
        .cards li{border:1px solid var(--b);border-radius:8px;padding:.6rem .8rem;display:flex;flex-direction:column;gap:.2rem}
        .example{border:1px solid var(--b);border-radius:8px;padding:.25rem 1rem 1rem;margin:1rem 0}.example img{max-width:100%;border:1px solid var(--b);background:#fff}
        input[type=search]{font:inherit;padding:.35rem .5rem;min-width:min(320px,100%)}
        """;
}
