using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html;
using BlazorWorkbench.Components.Preview;
using Bunit;

namespace BlazorWorkbench.Testing.Bunit;

/// <summary>
/// Markup snapshots: the rendered HTML of a scenario compared with a committed <c>{scenario-id}.verified.html</c> file, like
/// Verify or Jest snapshots. They need no browser and give the same result on every platform, so they run in any CI job;
/// screenshots (<c>workbench test --visual</c>) still catch what markup cannot show, such as CSS changes.
/// </summary>
public static partial class MarkupSnapshots
{
    /// <summary>Set to <c>1</c> (or <c>true</c>) to accept every changed or new snapshot instead of failing.</summary>
    public const string UpdateVariable = "WORKBENCH_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Compares the scenario's rendered markup (the inside of the preview root, pretty-printed, without Blazor's internal
    /// attributes) with <c>{directory}/{scenario-id}.verified.html</c>. A missing or different snapshot fails the test and
    /// writes <c>{scenario-id}.received.html</c> next to it; rename it (or set <see cref="UpdateVariable"/>) to accept it.
    /// </summary>
    /// <param name="cut">The rendered scenario.</param>
    /// <param name="directory">Folder of the snapshots; defaults to <c>__snapshots__</c> next to the calling test file.</param>
    /// <param name="name">File name without extension; defaults to the scenario id.</param>
    /// <param name="callerFile">Filled in by the compiler.</param>
    public static void MatchMarkupSnapshot(this IRenderedComponent<ScenarioHost> cut, string? directory = null, string? name = null, [CallerFilePath] string callerFile = "")
    {
        ArgumentNullException.ThrowIfNull(cut);
        var folder = directory ?? Path.Combine(Path.GetDirectoryName(callerFile) ?? Directory.GetCurrentDirectory(), "__snapshots__");
        var fileName = Sanitize(name ?? cut.Instance.Scenario.Id);
        var verified = Path.Combine(folder, fileName + ".verified.html");
        var received = Path.Combine(folder, fileName + ".received.html");
        var actual = Normalize(cut);

        var expected = File.Exists(verified) ? File.ReadAllText(verified).ReplaceLineEndings("\n") : null;
        if (expected == actual)
        {
            if (File.Exists(received))
                File.Delete(received);
            return;
        }

        Directory.CreateDirectory(folder);
        if (Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "True")
        {
            File.WriteAllText(verified, actual);
            if (File.Exists(received))
                File.Delete(received);
            return;
        }

        File.WriteAllText(received, actual);
        throw new ScenarioAssertionException(expected is null
            ? $"{cut.Instance.Scenario.Id}: no markup snapshot yet. Review {received} and rename it to {Path.GetFileName(verified)} (or set {UpdateVariable}=1)."
            : $"{cut.Instance.Scenario.Id}: the markup differs from {Path.GetFileName(verified)}.{Environment.NewLine}{FirstDifference(expected, actual)}{Environment.NewLine}" +
              $"Review {received}; rename it to {Path.GetFileName(verified)} (or set {UpdateVariable}=1) if the change is intended.");
    }

    /// <summary>The normalized markup used by <see cref="MatchMarkupSnapshot"/>.</summary>
    public static string Normalize(IRenderedComponent<ScenarioHost> cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var root = cut.Find("#" + ScenarioHost.RootElementId);
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            var formatter = new PrettyMarkupFormatter { Indentation = "  ", NewLine = "\n" };
            foreach (var child in root.ChildNodes)
                child.ToHtml(writer, formatter);
        }

        // Blazor's event handler and element reference markers change between runs and builds.
        var html = BlazorAttribute().Replace(sb.ToString(), "");
        var lines = html.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0);
        return string.Join('\n', lines) + "\n";
    }

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var left = i < e.Length ? e[i] : "(end)";
            var right = i < a.Length ? a[i] : "(end)";
            if (left != right)
                return $"Line {i + 1}:{Environment.NewLine}  expected: {left.Trim()}{Environment.NewLine}  actual:   {right.Trim()}";
        }

        return "";
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [GeneratedRegex("""\s+blazor:[A-Za-z0-9_.:-]+(="[^"]*")?""")]
    private static partial Regex BlazorAttribute();
}
