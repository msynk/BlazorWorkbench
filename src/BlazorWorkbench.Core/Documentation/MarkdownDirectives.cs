using System.Text;
using System.Text.RegularExpressions;

namespace BlazorWorkbench.Documentation;

/// <summary>A piece of documentation Markdown: plain Markdown, or a directive such as <c>{{scenario:inputs-button--primary}}</c>.</summary>
/// <param name="Markdown">The Markdown text (null for directives).</param>
/// <param name="Directive">The directive name in lower case (<c>scenario</c>, <c>scenarios</c>, <c>parameters</c>, <c>tokens</c>), or null.</param>
/// <param name="Argument">The directive argument, such as a scenario or component id.</param>
public sealed record MarkdownSegment(string? Markdown, string? Directive = null, string? Argument = null)
{
    public bool IsDirective => Directive is not null;
}

/// <summary>
/// Splits documentation Markdown into text and directives. A directive is a line of its own, outside fenced code blocks:
/// <list type="bullet">
/// <item><c>{{scenario:id}}</c>: a live preview of a scenario with a link to open it.</item>
/// <item><c>{{scenarios:component-id}}</c>: live previews of every scenario of a component.</item>
/// <item><c>{{parameters:component-id}}</c>: the parameter table of a component.</item>
/// <item><c>{{tokens}}</c>: the design tokens (CSS custom properties) of the preview stylesheets.</item>
/// </list>
/// </summary>
public static partial class MarkdownDirectives
{
    /// <summary>The directive names Workbench renders.</summary>
    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal) { "scenario", "scenarios", "parameters", "tokens" };

    public static IReadOnlyList<MarkdownSegment> Split(string? markdown)
    {
        var segments = new List<MarkdownSegment>();
        if (string.IsNullOrEmpty(markdown))
            return segments;

        var text = new StringBuilder();
        var fenced = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                fenced = !fenced;

            var match = fenced ? null : DirectiveLine().Match(line);
            if (match is { Success: true } && Known.Contains(match.Groups["name"].Value.ToLowerInvariant()))
            {
                Flush();
                segments.Add(new MarkdownSegment(null, match.Groups["name"].Value.ToLowerInvariant(), match.Groups["arg"].Success ? match.Groups["arg"].Value : null));
                continue;
            }

            text.Append(line).Append('\n');
        }

        Flush();
        return segments;

        void Flush()
        {
            if (text.ToString().Trim().Length > 0)
                segments.Add(new MarkdownSegment(text.ToString().TrimEnd('\n')));
            text.Clear();
        }
    }

    [GeneratedRegex(@"^\s*\{\{\s*(?<name>[A-Za-z]+)\s*(?::\s*(?<arg>[^\s{}]+)\s*)?\}\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveLine();
}
