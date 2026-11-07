using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Hybrid;

/// <summary>A response of the automation endpoint.</summary>
public sealed record HybridAutomationResponse(int Status, string Body, string ContentType = "application/json; charset=utf-8");

/// <summary>
/// The read-only automation API of a Blazor Hybrid host: the same <c>/_workbench/api</c> health, catalog, validation and
/// scenario endpoints as the ASP.NET Core host, so <c>workbench test</c>, <c>list</c> and <c>validate</c> work unchanged.
/// Previews themselves are driven through the WebView's DevTools endpoint.
/// </summary>
public static class HybridAutomationApi
{
    public const string HostingModel = "Hybrid";

    public static HybridAutomationResponse Handle(string method, string target, WorkbenchCatalogAccessor catalogs, WorkbenchHybridHost host)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(host);
        if (method is not ("GET" or "HEAD"))
            return Json(405, new { error = "Only GET is supported." });

        var path = target.Split('?', 2)[0];
        path = Uri.UnescapeDataString(path).TrimEnd('/');
        const string api = "/_workbench/api/";
        if (!path.StartsWith(api, StringComparison.Ordinal))
            return Json(404, new { error = "Not found." });

        var route = path[api.Length..];
        var catalog = catalogs.Catalog;
        switch (route)
        {
            case "health":
                return Json(host.IsWebViewReady ? 200 : 503, new
                {
                    status = host.IsWebViewReady ? "ok" : "starting",
                    hostingModel = HostingModel,
                    components = catalog.Components.Count,
                    scenarios = catalog.Scenarios.Count,
                    version = WorkbenchHybridHost.Version,
                    appOrigin = host.Options.AppOrigin.ToString(),
                    devToolsEndpoint = host.DevToolsEndpoint?.ToString(),
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                });
            case "catalog":
                return Json(200, CatalogMapper.ToDto(catalog, host.RelativeSource, WorkbenchHybridHost.Version, HostingModel));
            case "validate":
                return Json(200, CatalogMapper.ToValidationReport(catalog));
        }

        if (route.StartsWith("scenarios/", StringComparison.Ordinal))
        {
            var id = route["scenarios/".Length..];
            var scenario = catalog.FindScenario(id);
            if (scenario is null)
                return Json(404, new { error = $"Scenario '{id}' was not found." });
            var component = catalog.GetComponentOf(scenario)!;
            return Json(200, new { scenario = CatalogMapper.ToDto(scenario, component.Metadata), issues = catalog.IssuesFor(scenario) });
        }

        return Json(404, new { error = "Not found." });
    }

    private static HybridAutomationResponse Json(int status, object value) => new(status, JsonSerializer.Serialize(value, WorkbenchJson.Options));
}

/// <summary>
/// A minimal HTTP/1.1 server for <see cref="HybridAutomationApi"/>. It binds to a loopback address only, answers GET
/// requests with <c>Connection: close</c>, rejects foreign <c>Host</c> headers (DNS rebinding) and sends no CORS headers,
/// so web pages cannot read its responses. It needs no URL reservation and works on every .NET MAUI platform.
/// </summary>
public sealed class HybridAutomationServer : IAsyncDisposable
{
    private const int MaxRequestBytes = 16 * 1024;
    private readonly TcpListener _listener;
    private readonly Func<string, string, HybridAutomationResponse> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private HybridAutomationServer(TcpListener listener, Func<string, string, HybridAutomationResponse> handler)
    {
        _listener = listener;
        _handler = handler;
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        Url = new Uri($"http://{(endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{endpoint.Address}]" : endpoint.Address.ToString())}:{endpoint.Port}/");
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The URL the server listens on.</summary>
    public Uri Url { get; }

    /// <summary>Starts the server. <paramref name="url"/> must use http and a loopback host (127.0.0.1, [::1] or localhost); port 0 picks a free port.</summary>
    public static HybridAutomationServer Start(Uri url, Func<string, string, HybridAutomationResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(handler);
        if (url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException($"The automation endpoint supports http only, not '{url.Scheme}'.", nameof(url));
        var address = url.IsLoopback && url.HostNameType == UriHostNameType.Dns ? IPAddress.Loopback
            : IPAddress.TryParse(url.Host.Trim('[', ']'), out var parsed) ? parsed
            : throw new ArgumentException($"'{url.Host}' is not a loopback address.", nameof(url));
        if (!IPAddress.IsLoopback(address))
            throw new ArgumentException($"The automation endpoint listens on loopback addresses only, not '{url.Host}'.", nameof(url));

        var listener = new TcpListener(address, url.Port);
        listener.Start();
        return new HybridAutomationServer(listener, handler);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var stream = client.GetStream();
            var head = await ReadHeadAsync(stream, timeout.Token).ConfigureAwait(false);
            HybridAutomationResponse response;
            var method = "GET";
            if (head is null)
            {
                response = new(400, """{"error":"Malformed request."}""");
            }
            else
            {
                var lines = head.Split("\r\n");
                var parts = lines[0].Split(' ');
                var host = lines.Skip(1).Select(l => l.Split(':', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals("Host", StringComparison.OrdinalIgnoreCase))?[1].Trim();
                if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                    response = new(400, """{"error":"Malformed request."}""");
                else if (!IsLoopbackHost(host))
                    response = new(403, """{"error":"The automation endpoint accepts loopback host names only."}""");
                else
                {
                    method = parts[0];
                    try
                    {
                        response = _handler(method, parts[1]);
                    }
                    catch (Exception ex)
                    {
                        response = new(500, JsonSerializer.Serialize(new { error = ex.Message }, WorkbenchJson.Options));
                    }
                }
            }

            var body = Encoding.UTF8.GetBytes(response.Body);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {response.Status} {Reason(response.Status)}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            if (method != "HEAD")
                await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away or was too slow; nothing to report.
        }
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;
            length += read;
            var end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
                return Encoding.ASCII.GetString(buffer, 0, end);
        }

        return null;
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        var name = host.StartsWith('[') ? host[1..host.IndexOf(']', StringComparison.Ordinal)] : host.Split(':')[0];
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(name, out var ip) && IPAddress.IsLoopback(ip));
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        503 => "Service Unavailable",
        _ => "Error",
    };

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _listener.Dispose();
        _stop.Dispose();
    }
}

/// <summary>Starts the automation endpoint of a hybrid host when <see cref="WorkbenchHybridOptions.AutomationUrl"/> is set.</summary>
public static class HybridAutomation
{
    public static HybridAutomationServer? StartIfConfigured(WorkbenchHybridHost host, WorkbenchCatalogAccessor catalogs)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(catalogs);
        if (host.Options.AutomationUrl is not { } url)
            return null;
        return HybridAutomationServer.Start(url, (method, target) => HybridAutomationApi.Handle(method, target, catalogs, host));
    }
}
