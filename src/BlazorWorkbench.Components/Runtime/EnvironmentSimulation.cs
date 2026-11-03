using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using BlazorWorkbench.Diagnostics;
using Microsoft.AspNetCore.Components.Authorization;
using DiagnosticSource = BlazorWorkbench.Diagnostics.DiagnosticSource;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>A recorded fake HTTP exchange (URLs and headers are redacted).</summary>
public sealed record HttpExchange(DateTimeOffset Timestamp, string Method, string Url, int? Status, double DurationMs, string Outcome, string? Rule);

/// <summary>
/// Answers every request from the scenario's <see cref="HttpSimulation"/> rules. Requests never reach the network;
/// unmatched requests return 404 (configurable) and are reported as warnings.
/// </summary>
public sealed class WorkbenchHttpMessageHandler : HttpMessageHandler
{
    private readonly ScenarioSession _session;
    private readonly Lock _lock = new();
    private readonly Dictionary<HttpRule, int> _hits = [];
    private readonly List<HttpExchange> _exchanges = [];

    internal WorkbenchHttpMessageHandler(ScenarioSession session) => _session = session;

    public event Action<HttpExchange>? Exchanged;

    public IReadOnlyList<HttpExchange> Exchanges
    {
        get
        {
            lock (_lock)
                return _exchanges.ToArray();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? new Uri(HttpSimulation.DefaultBaseAddress);
        if (!uri.IsAbsoluteUri)
            uri = new Uri(new Uri(HttpSimulation.DefaultBaseAddress), uri);
        var method = request.Method.Method;
        var url = _session.Redactor.RedactUrl(uri.ToString());
        var scenario = _session.Scenario;
        var env = scenario?.Environment;
        var simulation = env?.Http;
        var stopwatch = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _session.Disposed);
        var token = linked.Token;

        using var trace = WorkbenchTelemetry.ActivitySource.StartActivity($"workbench.http {method}", ActivityKind.Client);
        trace?.SetTag("http.request.method", method);
        trace?.SetTag("url.full", url);
        trace?.SetTag("workbench.scenario.id", _session.ScenarioId);
        trace?.SetTag("workbench.session.id", _session.SessionId);

        var activity = _session.Activity.Begin();
        try
        {
            if (env?.Offline == true)
            {
                Record(method, url, null, stopwatch, "offline", null, DiagnosticSeverity.Warning);
                throw new HttpRequestException(HttpRequestError.ConnectionError, $"Simulated offline mode: {method} {url} failed.");
            }

            var rule = Match(simulation, method, uri);
            if (rule is null)
            {
                var status = simulation?.UnmatchedStatus ?? HttpStatusCode.NotFound;
                Record(method, url, (int)status, stopwatch, "unmatched", null, DiagnosticSeverity.Warning,
                    $"No fake HTTP rule matched {method} {url}; responded {(int)status}. Add .Http(h => h.{ToMethodName(method)}(\"{uri.AbsolutePath}\")...) to the scenario.");
                return new HttpResponseMessage(status) { RequestMessage = request, Content = new StringContent("", Encoding.UTF8, "text/plain") };
            }

            if (rule.Gate is { } gate)
            {
                // Waiting at a gate is intentional: the scenario is "ready" in its pending state.
                activity.Dispose();
                Record(method, url, null, stopwatch, $"held at gate '{gate}'", rule.ToString(), DiagnosticSeverity.Info);
                try
                {
                    await _session.Gates.WaitAsync(gate, token).ConfigureAwait(false);
                }
                catch (GateFailedException ex)
                {
                    Record(method, url, null, stopwatch, "gate failed", rule.ToString(), DiagnosticSeverity.Warning);
                    throw new HttpRequestException(HttpRequestError.ConnectionError, $"Simulated failure released by gate '{gate}': {ex.Message}", ex);
                }
                finally
                {
                    activity = _session.Activity.Begin();
                }
            }

            var latency = rule.Latency ?? simulation?.Latency ?? TimeSpan.Zero;
            if (latency > TimeSpan.Zero)
                await Task.Delay(latency, token).ConfigureAwait(false);

            switch (rule.Fault)
            {
                case HttpFault.NetworkError:
                    Record(method, url, null, stopwatch, "network error", rule.ToString(), DiagnosticSeverity.Warning);
                    throw new HttpRequestException(HttpRequestError.ConnectionError, $"Simulated network failure for {method} {url}.");
                case HttpFault.Timeout:
                    Record(method, url, null, stopwatch, "timeout", rule.ToString(), DiagnosticSeverity.Warning);
                    throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing (simulated by Workbench).",
                        new TimeoutException("Simulated timeout."));
            }

            HttpResponseMessage response;
            if (rule.Handler is { } handler)
            {
                response = await handler(request, _session.Context!).ConfigureAwait(false);
            }
            else
            {
                // The request stays pending until the component has consumed the body, so the preview cannot report
                // "ready" between the response and the render it causes (see TrackedContent).
                var content = new TrackedContent(Encoding.UTF8.GetBytes(rule.Body ?? ""), activity);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(rule.ContentType ?? (rule.Body is null ? "text/plain" : "application/json")) { CharSet = "utf-8" };
                activity = TrackedContent.Transferred;
                response = new HttpResponseMessage(rule.Status)
                {
                    RequestMessage = request,
                    Content = content,
                };
                foreach (var (name, value) in rule.Headers)
                {
                    if (!response.Headers.TryAddWithoutValidation(name, value))
                        response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            response.RequestMessage ??= request;
            var code = (int)response.StatusCode;
            Record(method, url, code, stopwatch, code >= 400 ? "error status" : "ok", rule.ToString(), code >= 500 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info);
            return response;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Record(method, url, null, stopwatch, "canceled", null, DiagnosticSeverity.Info);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private HttpRule? Match(HttpSimulation? simulation, string method, Uri uri)
    {
        if (simulation is null)
            return null;

        lock (_lock)
        {
            foreach (var rule in simulation.Rules)
            {
                if (!HttpRuleMatcher.IsMatch(rule, method, uri))
                    continue;
                var hits = _hits.GetValueOrDefault(rule);
                if (rule.Times is { } times && hits >= times)
                    continue;
                _hits[rule] = hits + 1;
                return rule;
            }
        }

        return null;
    }

    private void Record(string method, string url, int? status, Stopwatch stopwatch, string outcome, string? rule, DiagnosticSeverity severity, string? message = null)
    {
        WorkbenchTelemetry.HttpRequests.Add(1, new KeyValuePair<string, object?>("workbench.outcome", outcome));
        if (Activity.Current is { } current && current.Source == WorkbenchTelemetry.ActivitySource)
        {
            if (status is { } code) current.SetTag("http.response.status_code", code);
            current.SetTag("workbench.outcome", outcome);
            if (rule is not null) current.SetTag("workbench.rule", rule);
        }

        var exchange = new HttpExchange(DateTimeOffset.UtcNow, method, url, status, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1), outcome, rule);
        lock (_lock)
        {
            _exchanges.Add(exchange);
            if (_exchanges.Count > 200)
                _exchanges.RemoveAt(0);
        }

        _session.Diagnostics.Add(DiagnosticSource.Http, severity, _session.ScenarioId,
            message ?? $"{method} {url} → {(status?.ToString(CultureInfo.InvariantCulture) ?? outcome)} ({exchange.DurationMs.ToString(CultureInfo.InvariantCulture)} ms){(rule is null ? "" : $" [{rule}]")}",
            data: new Dictionary<string, string> { ["method"] = method, ["url"] = url, ["outcome"] = outcome });
        Exchanged?.Invoke(exchange);
    }

    private static string ToMethodName(string method) => method.ToUpperInvariant() switch
    {
        "GET" => "Get",
        "POST" => "Post",
        "PUT" => "Put",
        "PATCH" => "Patch",
        "DELETE" => "Delete",
        _ => "Any",
    };
}

/// <summary>
/// Response content that ends its pending activity when the body has been read, disposed, or after a short grace
/// period (for callers that never read the body).
/// </summary>
internal sealed class TrackedContent : ByteArrayContent
{
    /// <summary>A no-op handle used after ownership of the activity moved to the content.</summary>
    public static IDisposable Transferred { get; } = new NoOp();

    private readonly IDisposable _activity;

    public TrackedContent(byte[] body, IDisposable activity) : base(body)
    {
        _activity = activity;
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ => _activity.Dispose(), TaskScheduler.Default);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        await base.SerializeToStreamAsync(stream, context).ConfigureAwait(false);
        _activity.Dispose();
    }

    protected override async Task<Stream> CreateContentReadStreamAsync() =>
        new EndTrackingStream(await base.CreateContentReadStreamAsync().ConfigureAwait(false), _activity);

    protected override void Dispose(bool disposing)
    {
        _activity.Dispose();
        base.Dispose(disposing);
    }

    private sealed class NoOp : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class EndTrackingStream(Stream inner, IDisposable activity) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Track(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Track(int read)
        {
            if (read == 0)
                activity.Dispose();
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            activity.Dispose();
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> whose clients use the session's fake backend for every client name.</summary>
public sealed class WorkbenchHttpClientFactory(ScenarioSession session) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(session.Http, disposeHandler: false) { BaseAddress = new Uri(session.Scenario?.Environment.Http?.BaseAddress ?? HttpSimulation.DefaultBaseAddress) };
}

/// <summary>
/// Supplies the scenario's simulated user. Before activation (and when the scenario does not specify a user) it reports
/// the state set by the host, which is anonymous in a Workbench host. Simulated principals never leave the preview.
/// </summary>
public sealed class WorkbenchAuthenticationStateProvider : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> s_anonymous = Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())));
    private readonly ScenarioSession _session;
    private Task<AuthenticationState>? _host;

    internal WorkbenchAuthenticationStateProvider(ScenarioSession session) => _session = session;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        _session.Scenario?.Environment.User is { } user
            ? Task.FromResult(new AuthenticationState(user.ToPrincipal()))
            : _host ?? s_anonymous;

    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask) => _host = authenticationStateTask;

    internal void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
