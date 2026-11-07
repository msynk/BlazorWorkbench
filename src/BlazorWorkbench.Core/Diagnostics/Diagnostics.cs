using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlazorWorkbench.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSource>))]
public enum DiagnosticSource
{
    Scenario,
    Render,
    Console,
    Network,
    Http,
    JsInterop,
    Navigation,
    Callback,
    Gate,
    Performance,
    Resource,
    Accessibility,
    Focus,
    Host,

    /// <summary>Simulated platform services (device APIs such as share, launcher or file picker in Blazor Hybrid apps).</summary>
    Platform,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    Trace,
    Info,
    Warning,
    Error,
}

/// <summary>A diagnostic event correlated with a scenario. Messages are redacted before they are stored.</summary>
public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticSource Source,
    DiagnosticSeverity Severity,
    string ScenarioId,
    string Message,
    string? Details = null,
    IReadOnlyDictionary<string, string>? Data = null,
    string? SessionId = null);

/// <summary>Extension point that observes diagnostics (for example to forward them to a log or test report).</summary>
public interface IDiagnosticCollector
{
    void OnDiagnostic(DiagnosticEvent diagnostic);
}

/// <summary>Bounded, thread-safe diagnostic buffer for one scenario session. The oldest entries are dropped first.</summary>
public sealed class DiagnosticLog
{
    private readonly Lock _lock = new();
    private readonly Queue<DiagnosticEvent> _events = new();
    private readonly int _capacity;
    private readonly Redactor _redactor;
    private readonly IReadOnlyList<IDiagnosticCollector> _collectors;

    private readonly string? _sessionId;

    public DiagnosticLog(int capacity, Redactor redactor, IEnumerable<IDiagnosticCollector>? collectors = null, string? sessionId = null)
    {
        _capacity = Math.Max(10, capacity);
        _redactor = redactor;
        _collectors = collectors?.ToArray() ?? [];
        _sessionId = sessionId;
    }

    public event Action<DiagnosticEvent>? Added;

    public int Dropped { get; private set; }

    public DiagnosticEvent Add(DiagnosticSource source, DiagnosticSeverity severity, string scenarioId, string message, string? details = null,
        IReadOnlyDictionary<string, string>? data = null, DateTimeOffset? timestamp = null)
    {
        var e = new DiagnosticEvent(
            timestamp ?? DateTimeOffset.UtcNow,
            source,
            severity,
            scenarioId,
            Truncate(_redactor.RedactText(message), 2_000),
            details is null ? null : Truncate(_redactor.RedactText(details), 8_000),
            data?.ToDictionary(kv => kv.Key, kv => _redactor.RedactValue(kv.Key, kv.Value)),
            _sessionId);

        lock (_lock)
        {
            _events.Enqueue(e);
            while (_events.Count > _capacity)
            {
                _events.Dequeue();
                Dropped++;
            }
        }

        foreach (var collector in _collectors)
        {
            try { collector.OnDiagnostic(e); }
            catch (Exception) { /* Collectors must not break previews. */ }
        }

        Added?.Invoke(e);
        return e;
    }

    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (_lock)
            return _events.ToArray();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
            Dropped = 0;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + $"… ({value.Length - max} more characters)";
}

/// <summary>Configures what diagnostics, reports and screenshots treat as sensitive.</summary>
public sealed class RedactionOptions
{
    public string Replacement { get; set; } = "[REDACTED]";

    public ISet<string> SensitiveHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "Api-Key", "X-Auth-Token",
        "X-CSRF-Token", "X-XSRF-Token", "RequestVerificationToken", "X-Amz-Security-Token", "Ocp-Apim-Subscription-Key",
    };

    /// <summary>Query parameter and field names (case insensitive) whose values are redacted.</summary>
    public ISet<string> SensitiveNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "token", "access_token", "id_token", "refresh_token", "code", "key", "api_key", "apikey", "api-key", "secret",
        "client_secret", "password", "passwd", "pwd", "sig", "signature", "session", "sessionid", "auth", "credential",
        "credentials", "connectionstring", "connection_string", "private_key", "otp",
    };

    /// <summary>Additional regular expressions; every match is replaced.</summary>
    public IList<Regex> Patterns { get; } = [];
}

/// <summary>Removes secrets from text, URLs, headers and JSON-like payloads. Conservative by design: it prefers over-redaction.</summary>
public sealed partial class Redactor
{
    public static Redactor Default { get; } = new(new RedactionOptions());

    private readonly RedactionOptions _options;

    public Redactor(RedactionOptions options) => _options = options;

    public string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var r = _options.Replacement;
        text = BearerRegex().Replace(text, m => $"{m.Groups[1].Value} {r}");
        text = JwtRegex().Replace(text, r);
        text = KeyValueRegex().Replace(text, m => !IsSensitiveTextName(m.Groups["name"].Value) ? m.Value
            : m.Groups["quoted"].Success
                ? $"{m.Groups["name"].Value}{m.Groups["q0"].Value}{m.Groups["sep"].Value}\"{r}{m.Groups["qend"].Value}"
                : $"{m.Groups["name"].Value}{m.Groups["q0"].Value}{m.Groups["sep"].Value}{r}");
        text = UrlCredentialsRegex().Replace(text, m => $"{m.Groups[1].Value}{r}@");
        foreach (var pattern in _options.Patterns)
            text = pattern.Replace(text, r);
        return text;
    }

    /// <summary>Redacts sensitive query parameters and embedded credentials from a URL.</summary>
    public string RedactUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var hashIndex = url.IndexOf('#', StringComparison.Ordinal);
        var fragment = hashIndex >= 0 ? url[hashIndex..] : "";
        var withoutFragment = hashIndex >= 0 ? url[..hashIndex] : url;
        var q = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        var path = UrlCredentialsRegex().Replace(q >= 0 ? withoutFragment[..q] : withoutFragment, m => $"{m.Groups[1].Value}{_options.Replacement}@");
        if (q < 0)
            return path + (fragment.Length > 0 ? RedactText(fragment) : "");

        var pairs = withoutFragment[(q + 1)..].Split('&').Select(pair =>
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) return pair;
            var name = Uri.UnescapeDataString(pair[..eq]);
            return IsSensitiveName(name) ? $"{pair[..eq]}={Uri.EscapeDataString(_options.Replacement)}" : pair;
        });
        return $"{path}?{string.Join('&', pairs)}{(fragment.Length > 0 ? RedactText(fragment) : "")}";
    }

    public string RedactHeader(string name, string value) =>
        _options.SensitiveHeaders.Contains(name) || IsSensitiveName(name) ? _options.Replacement : RedactText(value);

    /// <summary>Redacts a named value (header, field, query parameter).</summary>
    public string RedactValue(string name, string value) =>
        IsSensitiveName(name) || _options.SensitiveHeaders.Contains(name) ? _options.Replacement : RedactText(value);

    public bool IsSensitiveName(string name) => _options.SensitiveNames.Contains(name) || IsSensitiveTextName(name);

    // Free text uses name patterns only; short ambiguous names such as "code" or "key" are redacted in URLs and headers but
    // not in prose ("status code: 404") to keep diagnostics useful.
    private static bool IsSensitiveTextName(string name)
    {
        if (name.Equals("pwd", StringComparison.OrdinalIgnoreCase) || name.Equals("passwd", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("credential", StringComparison.OrdinalIgnoreCase) || name.Equals("otp", StringComparison.OrdinalIgnoreCase))
            return true;
        var normalized = name.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        return normalized.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("token", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("apikey", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("connectionstring", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\b(Bearer|Basic|Digest)\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtRegex();

    // name=value, name: value, "name": "value" (JSON) and Password=...; (connection strings). A quoted value is matched up
    // to its closing quote (honoring \" escapes) or the end of the text, so values containing spaces, commas, semicolons,
    // ampersands or braces are redacted entirely.
    [GeneratedRegex("""(?<name>[A-Za-z][A-Za-z0-9_\-]{1,40})(?<q0>"?)(?<sep>\s*[=:]\s*)(?:"(?<quoted>(?:[^"\\]|\\.)*)(?<qend>"|$)|(?<value>[^\s"&;,}]+))""")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"(\b[a-z][a-z0-9+.\-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentialsRegex();
}
