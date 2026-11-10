using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorWorkbench.Documentation;

/// <summary>
/// Renders a safe Markdown subset to HTML: headings, paragraphs, emphasis, inline code, fenced code blocks, lists,
/// block quotes, horizontal rules, pipe tables and links. Raw HTML is always escaped and link targets are restricted
/// to http(s), mailto and relative URLs, so untrusted documentation cannot inject script.
/// </summary>
public static partial class SafeMarkdown
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var html = new StringBuilder();
        var paragraph = new List<string>();
        var i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append("<p>").Append(Inline(string.Join(' ', paragraph.Select(p => p.Trim())))).Append("</p>\n");
            paragraph.Clear();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = SafeLanguage(trimmed[3..].Trim());
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[i++]);
                i++;
                html.Append("<pre><code").Append(language.Length > 0 ? $" class=\"language-{language}\"" : "").Append('>')
                    .Append(WebUtility.HtmlEncode(string.Join('\n', code))).Append("</code></pre>\n");
                continue;
            }

            var heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                html.Append($"<h{level} id=\"{Slug(text)}\">").Append(Inline(text)).Append($"</h{level}>\n");
                i++;
                continue;
            }

            if (HrRegex().IsMatch(trimmed))
            {
                FlushParagraph();
                html.Append("<hr />\n");
                i++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                while (i < lines.Length && lines[i].Trim().StartsWith('>'))
                    quote.Add(lines[i++].Trim()[1..].Trim());
                html.Append("<blockquote>").Append(ToHtml(string.Join('\n', quote))).Append("</blockquote>\n");
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[i + 1].Trim()))
            {
                FlushParagraph();
                var header = SplitRow(trimmed);
                i += 2;
                html.Append("<table><thead><tr>");
                foreach (var cell in header) html.Append("<th>").Append(Inline(cell)).Append("</th>");
                html.Append("</tr></thead><tbody>");
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    html.Append("<tr>");
                    foreach (var cell in SplitRow(lines[i++].Trim())) html.Append("<td>").Append(Inline(cell)).Append("</td>");
                    html.Append("</tr>");
                }

                html.Append("</tbody></table>\n");
                continue;
            }

            var unordered = UnorderedRegex().Match(line);
            var ordered = OrderedRegex().Match(line);
            if (unordered.Success || ordered.Success)
            {
                FlushParagraph();
                var tag = unordered.Success ? "ul" : "ol";
                html.Append('<').Append(tag).Append('>');
                while (i < lines.Length)
                {
                    var m = unordered.Success ? UnorderedRegex().Match(lines[i]) : OrderedRegex().Match(lines[i]);
                    if (!m.Success) break;
                    var item = m.Groups[1].Value.Trim();
                    i++;
                    // Indented lines that do not start a new item continue the current one (a wrapped list item).
                    while (i < lines.Length && lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]) && lines[i].Trim().Length > 0 &&
                           !UnorderedRegex().IsMatch(lines[i]) && !OrderedRegex().IsMatch(lines[i]))
                        item += " " + lines[i++].Trim();
                    html.Append("<li>").Append(Inline(item)).Append("</li>");
                }

                html.Append("</").Append(tag).Append(">\n");
                continue;
            }

            paragraph.Add(line);
            i++;
        }

        FlushParagraph();
        return html.ToString();
    }

    /// <summary>Renders inline Markdown (code, emphasis, links) with HTML escaping.</summary>
    public static string Inline(string text)
    {
        // Code spans first, so their content is never interpreted.
        var parts = CodeSpanRegex().Split(text);
        var sb = new StringBuilder();
        for (var p = 0; p < parts.Length; p++)
        {
            if (p % 2 == 1)
            {
                sb.Append("<code>").Append(WebUtility.HtmlEncode(parts[p])).Append("</code>");
                continue;
            }

            var s = WebUtility.HtmlEncode(parts[p]);
            s = LinkRegex().Replace(s, m =>
            {
                var url = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                return IsSafeUrl(url)
                    ? $"<a href=\"{WebUtility.HtmlEncode(url)}\"{(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? " rel=\"noopener noreferrer\" target=\"_blank\"" : "")}>{m.Groups[1].Value}</a>"
                    : m.Groups[1].Value;
            });
            s = BoldRegex().Replace(s, "<strong>$1</strong>");
            s = ItalicRegex().Replace(s, "<em>$1</em>");
            sb.Append(s);
        }

        return sb.ToString();
    }

    public static bool IsSafeUrl(string url)
    {
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) return true;
        if (url.StartsWith('#') || url.StartsWith("./", StringComparison.Ordinal) || url.StartsWith("../", StringComparison.Ordinal)) return true;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto";
        // Relative path without scheme (no colon before the first slash).
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var slash = url.IndexOf('/', StringComparison.Ordinal);
        return colon < 0 || (slash >= 0 && slash < colon);
    }

    public static string Slug(string text) => Naming.ToKebabCase(text);

    private static string SafeLanguage(string value) => LanguageRegex().IsMatch(value) ? value : "";

    private static IEnumerable<string> SplitRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim());

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")] private static partial Regex HeadingRegex();
    [GeneratedRegex(@"^(-{3,}|\*{3,}|_{3,})$")] private static partial Regex HrRegex();
    [GeneratedRegex(@"^\s*[-*+]\s+(.*)$")] private static partial Regex UnorderedRegex();
    [GeneratedRegex(@"^\s*\d+[.)]\s+(.*)$")] private static partial Regex OrderedRegex();
    [GeneratedRegex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$")] private static partial Regex TableSeparatorRegex();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex CodeSpanRegex();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex LinkRegex();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex BoldRegex();
    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")] private static partial Regex ItalicRegex();
    [GeneratedRegex(@"^[A-Za-z0-9+#\-]{1,20}$")] private static partial Regex LanguageRegex();
}
