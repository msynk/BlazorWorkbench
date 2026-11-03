using System.Text.RegularExpressions;

namespace BlazorWorkbench;

/// <summary>Matches requests against <see cref="HttpRule"/> patterns (see <see cref="HttpRule.Pattern"/> for the syntax).</summary>
public static class HttpRuleMatcher
{
    public static bool IsMatch(HttpRule rule, string method, Uri requestUri)
    {
        if (rule.Method != "*" && !string.Equals(rule.Method, method, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsMatch(rule.Pattern, requestUri);
    }

    public static bool IsMatch(string pattern, Uri requestUri)
    {
        if (!requestUri.IsAbsoluteUri)
            requestUri = new Uri(new Uri(HttpSimulation.DefaultBaseAddress), requestUri);

        var queryIndex = pattern.IndexOf('?', StringComparison.Ordinal);
        var pathPattern = queryIndex >= 0 ? pattern[..queryIndex] : pattern;
        var queryPattern = queryIndex >= 0 ? pattern[(queryIndex + 1)..] : null;

        string path;
        if (Uri.TryCreate(pathPattern, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            if (!string.Equals(absolute.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase) || absolute.Port != requestUri.Port)
                return false;
            pathPattern = absolute.AbsolutePath;
        }

        path = requestUri.AbsolutePath;
        if (!pathPattern.StartsWith('/'))
            pathPattern = "/" + pathPattern;

        if (!GlobToRegex(Normalize(pathPattern)).IsMatch(Normalize(Uri.UnescapeDataString(path))))
            return false;

        if (queryPattern is null)
            return true;

        var actual = ParseQuery(requestUri.Query);
        foreach (var (name, value) in ParseQuery(queryPattern))
        {
            if (!actual.TryGetValue(name, out var actualValue))
                return false;
            if (value != "*" && !string.Equals(value, actualValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace(@"\*\*", "\u0001", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\u0001", ".*", StringComparison.Ordinal);
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair);
            var value = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : "";
            result[name] = value;
        }

        return result;
    }
}
