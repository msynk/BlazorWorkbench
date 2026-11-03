using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing;

/// <summary>Writes <c>report.json</c> (machine readable), <c>junit.xml</c> (CI test tabs) and <c>index.html</c> (human review).</summary>
public static class ReportWriter
{
    public static void WriteAll(TestRunReport report, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, WorkbenchJson.Indented));
        File.WriteAllText(Path.Combine(directory, "junit.xml"), ToJUnit(report));
        File.WriteAllText(Path.Combine(directory, "index.html"), ToHtml(report));
    }

    public static string ToJUnit(TestRunReport report)
    {
        static string Seconds(double ms) => (ms / 1000).ToString("0.###", CultureInfo.InvariantCulture);

        var suites = new XElement("testsuites",
            new XAttribute("name", "Blazor Workbench"),
            new XAttribute("tests", report.Scenarios.Sum(s => Math.Max(1, s.Checks.Count))),
            new XAttribute("failures", report.Scenarios.Sum(s => s.Checks.Count(c => c.Status is CheckStatus.Failed or CheckStatus.Missing))),
            new XAttribute("errors", report.Scenarios.Sum(s => s.Checks.Count(c => c.Status == CheckStatus.Error)) + report.InfrastructureErrors.Count),
            new XAttribute("time", Seconds(report.Summary.DurationMs)));

        foreach (var group in report.Scenarios.GroupBy(s => s.ComponentId ?? "workbench"))
        {
            var suite = new XElement("testsuite",
                new XAttribute("name", group.Key),
                new XAttribute("tests", group.Sum(s => Math.Max(1, s.Checks.Count))),
                new XAttribute("failures", group.Sum(s => s.Checks.Count(c => c.Status is CheckStatus.Failed or CheckStatus.Missing))),
                new XAttribute("errors", group.Sum(s => s.Checks.Count(c => c.Status == CheckStatus.Error))),
                new XAttribute("skipped", group.Sum(s => s.Status == CheckStatus.Skipped ? 1 : s.Checks.Count(c => c.Status == CheckStatus.Skipped))),
                new XAttribute("time", Seconds(group.Sum(s => s.DurationMs))));

            foreach (var scenario in group)
            {
                if (scenario.Checks.Count == 0)
                {
                    var test = new XElement("testcase", new XAttribute("classname", scenario.ScenarioId), new XAttribute("name", "scenario"), new XAttribute("time", "0"));
                    if (scenario.Status == CheckStatus.Skipped) test.Add(new XElement("skipped", new XAttribute("message", scenario.Message ?? "")));
                    suite.Add(test);
                    continue;
                }

                foreach (var check in scenario.Checks)
                {
                    var name = check.Variant is null ? $"{check.Kind.ToString().ToLowerInvariant()}: {check.Name}" : $"{check.Kind.ToString().ToLowerInvariant()}: {check.Variant}";
                    var test = new XElement("testcase",
                        new XAttribute("classname", scenario.ScenarioId),
                        new XAttribute("name", name),
                        new XAttribute("time", Seconds(check.DurationMs)));
                    var details = Details(scenario, check);
                    switch (check.Status)
                    {
                        case CheckStatus.Failed:
                        case CheckStatus.Missing:
                            test.Add(new XElement("failure", new XAttribute("message", check.Message ?? check.Status.ToString()), new XAttribute("type", check.Kind.ToString()), details));
                            break;
                        case CheckStatus.Error:
                            test.Add(new XElement("error", new XAttribute("message", check.Message ?? "Infrastructure error"), details));
                            break;
                        case CheckStatus.Skipped:
                            test.Add(new XElement("skipped", new XAttribute("message", check.Message ?? "")));
                            break;
                    }

                    if (scenario.Artifacts.Count > 0)
                        test.Add(new XElement("system-out", string.Join('\n', scenario.Artifacts.Select(a => $"[[ATTACHMENT|{a}]]"))));
                    suite.Add(test);
                }
            }

            suites.Add(suite);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), suites).ToString();
    }

    private static string Details(ScenarioResult scenario, CheckResult check)
    {
        var sb = new StringBuilder();
        foreach (var step in check.Steps ?? [])
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{step.Status}] {step.Description}{(step.Error is null ? "" : " — " + step.Error)}");
        foreach (var finding in (check.Findings ?? []).Where(f => f.Blocking))
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {finding.Impact} {finding.RuleId}: {finding.Help} ({string.Join(", ", finding.Targets.Take(3))}) {finding.HelpUrl}");
        if (check.Visual is { } v)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  actual: {v.ActualPath} baseline: {v.BaselinePath} diff: {v.DiffPath}");
        foreach (var d in scenario.Diagnostics.Where(d => d.Severity == Diagnostics.DiagnosticSeverity.Error).Take(5))
            sb.AppendLine(CultureInfo.InvariantCulture, $"  diagnostic [{d.Source}] {d.Message}");
        return sb.ToString();
    }

    public static string ToHtml(TestRunReport report)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        static string Badge(CheckStatus s) => $"<span class=\"st st-{s.ToString().ToLowerInvariant()}\">{s}</span>";

        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Blazor Workbench report</title>
            <style>
            :root{color-scheme:light dark;--b:#d0d0d8;--m:#666;--ok:#067647;--bad:#b42318;--warn:#8a5300}
            body{font:14px/1.45 system-ui,sans-serif;margin:0;padding:1.5rem;max-width:1200px}
            h1{margin:0 0 .25rem}.m{color:var(--m)}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid var(--b);padding:.35rem .5rem;text-align:start;vertical-align:top}
            .st{font-size:12px;font-weight:600;padding:0 .45rem;border-radius:999px;border:1px solid}.st-passed{color:var(--ok)}.st-failed,.st-missing,.st-error{color:var(--bad)}.st-skipped{color:var(--m)}
            .sum{display:flex;gap:1rem;margin:1rem 0;flex-wrap:wrap}.sum div{border:1px solid var(--b);border-radius:8px;padding:.5rem .9rem}.sum b{font-size:1.4rem;display:block}
            details{margin:.25rem 0}summary{cursor:pointer}code{font-size:12.5px}.imgs{display:flex;gap:.75rem;flex-wrap:wrap}.imgs figure{margin:0}.imgs img{max-width:380px;border:1px solid var(--b);background:#fff}
            .ov{position:relative;display:inline-block}.ov img+img{position:absolute;inset:0}
            input[type=search]{padding:.3rem .5rem;min-width:280px}.hide{display:none}
            </style></head><body>
            """);
        sb.Append(CultureInfo.InvariantCulture, $"<h1>Blazor Workbench report</h1><p class=\"m\">{E(report.StartedAt.ToString("u", CultureInfo.InvariantCulture))} · {E(report.Environment.Browser)} {E(report.Environment.BrowserVersion)} · {E(report.Environment.Os)} · {E(report.Environment.Runtime)}</p>");
        var s = report.Summary;
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"sum\"><div><b>{s.Total}</b>scenarios</div><div><b style=\"color:var(--ok)\">{s.Passed}</b>passed</div><div><b style=\"color:var(--bad)\">{s.Failed}</b>failed</div><div><b>{s.Skipped}</b>skipped</div><div><b>{s.Errors}</b>infrastructure errors</div><div><b>{s.DurationMs / 1000:0.0}s</b>duration</div></div>");
        foreach (var error in report.InfrastructureErrors)
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"st-error\">{E(error)}</p>");

        sb.Append("<p><label>Filter <input type=\"search\" id=\"q\" placeholder=\"scenario id or status\"></label> <label><input type=\"checkbox\" id=\"failed\"> failures only</label></p>");
        sb.Append("<table><thead><tr><th>Status</th><th>Scenario</th><th>Checks</th><th>Ready</th></tr></thead><tbody>");
        foreach (var scenario in report.Scenarios)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr data-s=\"{E(scenario.Status.ToString().ToLowerInvariant())}\" data-id=\"{E(scenario.ScenarioId)}\"><td>{Badge(scenario.Status)}</td><td><strong>{E(scenario.Name)}</strong><br><code>{E(scenario.ScenarioId)}</code>");
            if (scenario.Message is { } message) sb.Append(CultureInfo.InvariantCulture, $"<div class=\"m\">{E(message)}</div>");
            sb.Append("</td><td>");
            foreach (var check in scenario.Checks)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<details{(check.Status is CheckStatus.Failed or CheckStatus.Missing or CheckStatus.Error ? " open" : "")}><summary>{Badge(check.Status)} {E(check.Kind.ToString())}: {E(check.Variant ?? check.Name)} <span class=\"m\">{check.DurationMs:0} ms</span></summary>");
                if (check.Message is { } m) sb.Append(CultureInfo.InvariantCulture, $"<p>{E(m)}</p>");
                if (check.Steps is { Count: > 0 } steps)
                {
                    sb.Append("<ol>");
                    foreach (var step in steps)
                        sb.Append(CultureInfo.InvariantCulture, $"<li>{Badge(step.Status)} <code>{E(step.Description)}</code>{(step.Error is null ? "" : $" — {E(step.Error)}")}</li>");
                    sb.Append("</ol>");
                }

                if (check.Findings is { Count: > 0 } findings)
                {
                    sb.Append("<ul>");
                    foreach (var f in findings)
                        sb.Append(CultureInfo.InvariantCulture, $"<li>{(f.Blocking ? "<b>blocking</b> " : f.NeedsReview ? "needs review " : "")}{E(f.Impact)} <code>{E(f.RuleId)}</code> {E(f.Help)} <span class=\"m\">{E(string.Join(", ", f.Targets.Take(3)))}</span> {(f.HelpUrl is null ? "" : $"<a href=\"{E(f.HelpUrl)}\" rel=\"noopener noreferrer\">guidance</a>")}</li>");
                    sb.Append("</ul><p class=\"m\">Automated checks cover a subset of WCAG; manual review is still required.</p>");
                }

                if (check.Visual is { } v)
                {
                    sb.Append("<div class=\"imgs\">");
                    if (v.ActualPath is not null) sb.Append(CultureInfo.InvariantCulture, $"<figure><figcaption>Current</figcaption><img loading=\"lazy\" src=\"{E(v.ActualPath)}\" alt=\"Current screenshot\"></figure>");
                    if (v.DiffPath is not null) sb.Append(CultureInfo.InvariantCulture, $"<figure><figcaption>Difference ({v.DiffRatio:P3})</figcaption><img loading=\"lazy\" src=\"{E(v.DiffPath)}\" alt=\"Difference\"></figure>");
                    sb.Append(CultureInfo.InvariantCulture, $"</div><p class=\"m\">Baseline: <code>{E(v.BaselinePath)}</code> ({v.Width}×{v.Height})</p>");
                }

                sb.Append("</details>");
            }

            if (scenario.Diagnostics.Count > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<details><summary>{scenario.Diagnostics.Count} diagnostic(s)</summary><ul>");
                foreach (var d in scenario.Diagnostics)
                    sb.Append(CultureInfo.InvariantCulture, $"<li>[{E(d.Severity.ToString())}] [{E(d.Source.ToString())}] {E(d.Message)}</li>");
                sb.Append("</ul></details>");
            }

            sb.Append(CultureInfo.InvariantCulture, $"</td><td>{(scenario.ReadyMs is { } r ? $"{r:0} ms" : "—")}</td></tr>");
        }

        sb.Append("""
            </tbody></table>
            <script>
            const q=document.getElementById('q'),f=document.getElementById('failed');
            function apply(){const t=q.value.toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>{const bad=['failed','error','missing'].includes(r.dataset.s);r.classList.toggle('hide',(f.checked&&!bad)||(t&&!(r.dataset.id.includes(t)||r.dataset.s.includes(t))));});}
            q.oninput=apply;f.onchange=apply;
            </script></body></html>
            """);
        return sb.ToString();
    }
}
