using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing;

/// <summary>Thrown for infrastructure failures (host did not start, browser missing). Maps to exit code 3.</summary>
public sealed class WorkbenchInfrastructureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a host reports from <c>/_workbench/api/health</c>.</summary>
/// <param name="HostingModel">Server, WebAssembly or Hybrid.</param>
/// <param name="Version">Workbench version of the host.</param>
/// <param name="AppOrigin">Origin of previews inside a Blazor Hybrid app's WebView (null for web hosts).</param>
/// <param name="DevToolsEndpoint">Chrome DevTools Protocol endpoint of a Blazor Hybrid app's WebView (null for web hosts).</param>
public sealed record WorkbenchHostInfo(string HostingModel, string? Version, Uri? AppOrigin, Uri? DevToolsEndpoint)
{
    public bool IsHybrid => string.Equals(HostingModel, "Hybrid", StringComparison.OrdinalIgnoreCase);

    internal static WorkbenchHostInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        Uri? Url(string name) => Uri.TryCreate(Text(name), UriKind.Absolute, out var url) ? url : null;
        return new WorkbenchHostInfo(Text("hostingModel") ?? "Server", Text("version"), Url("appOrigin"), Url("devToolsEndpoint"));
    }
}

/// <summary>
/// A running Workbench host: either started from a project (<c>dotnet run</c>) on a free loopback port, or an existing
/// URL. Disposing stops a started process tree. Blazor Hybrid hosts (.NET MAUI apps) are started the same way; the
/// runner then drives their WebView through its DevTools endpoint.
/// </summary>
public sealed class WorkbenchHost : IAsyncDisposable
{
    /// <summary>Environment variable with the automation URL of a Blazor Hybrid host (read by BlazorWorkbench.Hybrid).</summary>
    public const string AutomationUrlVariable = "BLAZOR_WORKBENCH_AUTOMATION_URL";

    /// <summary>Environment variable with the WebView DevTools port of a Blazor Hybrid host (read by BlazorWorkbench.Hybrid).</summary>
    public const string DevToolsPortVariable = "BLAZOR_WORKBENCH_CDP_PORT";

    private readonly Process? _process;
    private readonly StringBuilder _log;
    private readonly HttpClient _http;
    private readonly string? _webViewDataFolder;

    private WorkbenchHost(Uri baseAddress, Process? process, StringBuilder log, string? webViewDataFolder = null)
    {
        BaseAddress = baseAddress;
        _process = process;
        _log = log;
        _webViewDataFolder = webViewDataFolder;
        _http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    public Uri BaseAddress { get; }

    /// <summary>The hosting model and, for Blazor Hybrid apps, how to reach the WebView. Known once the host is healthy.</summary>
    public WorkbenchHostInfo Info { get; private set; } = new("Server", null, null, null);

    /// <summary>Host console output (started hosts only).</summary>
    public string Log
    {
        get
        {
            lock (_log)
                return _log.ToString();
        }
    }

    /// <summary>Connects to a host that is already running.</summary>
    public static async Task<WorkbenchHost> AttachAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var host = new WorkbenchHost(url, null, new StringBuilder());
        await host.WaitForHealthAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        return host;
    }

    /// <summary>Builds (unless <paramref name="noBuild"/>) and starts the host project on a free loopback port.</summary>
    /// <param name="projectPath">The host project: an ASP.NET Core Workbench host or a .NET MAUI Blazor Hybrid Workbench app.</param>
    /// <param name="noBuild">Skip the build.</param>
    /// <param name="configuration">Build configuration.</param>
    /// <param name="startupTimeout">How long to wait for the health endpoint.</param>
    /// <param name="onOutput">Receives host console output.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <param name="framework">Target framework for multi-targeted projects (<c>dotnet run -f</c>).</param>
    public static async Task<WorkbenchHost> StartAsync(string projectPath, bool noBuild = false, string configuration = "Debug",
        TimeSpan? startupTimeout = null, Action<string>? onOutput = null, CancellationToken cancellationToken = default, string? framework = null)
    {
        if (!File.Exists(projectPath))
            throw new WorkbenchInfrastructureException($"Workbench host project '{projectPath}' was not found.");

        var port = GetFreePort();
        var url = new Uri($"http://127.0.0.1:{port}");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "run", "--project", Path.GetFullPath(projectPath), "--no-launch-profile", "-c", configuration })
            start.ArgumentList.Add(arg);
        if (noBuild)
            start.ArgumentList.Add("--no-build");
        if (!string.IsNullOrWhiteSpace(framework))
        {
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add(framework);
        }

        start.ArgumentList.Add("--");
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(url.ToString().TrimEnd('/'));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";

        // Blazor Hybrid apps: the automation endpoint and the WebView DevTools endpoint (both loopback only). WebView2
        // reads its variables at startup; a private user data folder keeps the WebView from joining the browser process
        // of another instance, which would ignore the debugging port. Web hosts ignore these variables.
        var devToolsPort = GetFreePort();
        var webViewData = Path.Combine(Path.GetTempPath(), "BlazorWorkbench", "WebView2", $"{Environment.ProcessId}-{devToolsPort}");
        start.Environment[AutomationUrlVariable] = url.ToString().TrimEnd('/');
        start.Environment[DevToolsPortVariable] = devToolsPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
            $"{Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS")} --remote-debugging-port={devToolsPort}".Trim();
        start.Environment["WEBVIEW2_USER_DATA_FOLDER"] = webViewData;

        var log = new StringBuilder();
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        void Append(string? line)
        {
            if (line is null) return;
            lock (log)
            {
                if (log.Length < 2_000_000)
                    log.AppendLine(line);
            }

            onOutput?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new WorkbenchInfrastructureException("The 'dotnet' command could not be started. Is the .NET SDK installed and on PATH?", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var host = new WorkbenchHost(url, process, log, webViewData);
        try
        {
            await host.WaitForHealthAsync(startupTimeout ?? TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<CatalogDto>("_workbench/api/catalog", WorkbenchJson.Options, cancellationToken).ConfigureAwait(false)
        ?? throw new WorkbenchInfrastructureException("The host returned an empty catalog.");

    public async Task<ValidationReportDto> ValidateAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<ValidationReportDto>("_workbench/api/validate", WorkbenchJson.Options, cancellationToken).ConfigureAwait(false)
        ?? throw new WorkbenchInfrastructureException("The host returned an empty validation report.");

    /// <summary>The origin previews load from: the host URL, or the app origin inside the WebView of a Blazor Hybrid app.</summary>
    public Uri PreviewBaseAddress => Info.IsHybrid && Info.AppOrigin is { } origin ? origin : BaseAddress;

    public Uri PreviewUrl(string scenarioId, string? theme = null, string? argsJson = null)
    {
        var query = new List<string>();
        if (theme is not null) query.Add("theme=" + Uri.EscapeDataString(theme));
        if (argsJson is not null) query.Add("args=" + Uri.EscapeDataString(argsJson));
        return new Uri(PreviewBaseAddress, $"_workbench/preview/{Uri.EscapeDataString(scenarioId)}" + (query.Count > 0 ? "?" + string.Join('&', query) : ""));
    }

    private async Task WaitForHealthAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
                throw new WorkbenchInfrastructureException($"The Workbench host exited with code {_process.ExitCode} before it became ready.\n{Tail(Log)}");
            try
            {
                using var response = await _http.GetAsync("_workbench/api/health", cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    try
                    {
                        Info = WorkbenchHostInfo.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    }
                    catch (JsonException)
                    {
                        // Hosts without a JSON health response are Interactive Server hosts (the default).
                    }

                    return;
                }

                last = new HttpRequestException($"Health check returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new WorkbenchInfrastructureException($"The Workbench host at {BaseAddress} did not become healthy within {timeout.TotalSeconds:0} s: {last?.Message}\n{Tail(Log)}", last);
    }

    private static string Tail(string text, int lines = 40) => string.Join('\n', text.Split('\n').TakeLast(lines));

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        if (_process is null)
            return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            _process.Dispose();
            DeleteWebViewData();
        }
    }

    private void DeleteWebViewData()
    {
        if (_webViewDataFolder is null || !Directory.Exists(_webViewDataFolder))
            return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_webViewDataFolder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // WebView2 child processes can hold files for a moment after the app exits.
                Thread.Sleep(200);
            }
        }
    }
}
