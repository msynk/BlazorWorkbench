using System.Globalization;
using BlazorWorkbench.Contracts;
using Microsoft.Playwright;

namespace BlazorWorkbench.Testing;

/// <summary>
/// The WebView of a Blazor Hybrid Workbench app, driven through its Chrome DevTools Protocol endpoint (WebView2 on
/// Windows). The app has a single page, so loads are serialized; each load emulates the variant's viewport, color scheme,
/// reduced motion, locale and time zone through a DevTools session that stays attached for the whole run.
/// </summary>
internal sealed class HybridWebView
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IPage _page;
    private readonly ICDPSession _cdp;
    private string? _locale;

    private HybridWebView(IPage page, ICDPSession cdp)
    {
        _page = page;
        _cdp = cdp;
    }

    public static async Task<IBrowser> ConnectAsync(IPlaywright playwright, WorkbenchHostInfo info, CancellationToken cancellationToken)
    {
        if (info.DevToolsEndpoint is not { } endpoint)
            throw new WorkbenchInfrastructureException(
                "The Blazor Hybrid host exposes no WebView DevTools endpoint. Automated runs need Windows (WebView2) and an app started by " +
                "'workbench test' (or BLAZOR_WORKBENCH_CDP_PORT and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>).");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                return await playwright.Chromium.ConnectOverCDPAsync(endpoint.ToString(), new BrowserTypeConnectOverCDPOptions { Timeout = 10_000 }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (DateTime.UtcNow < deadline)
            {
                _ = ex; // The WebView may still be starting.
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                var hint = OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess
                    ? " This process runs elevated: WebView2 Runtime 150 and later ignore the WEBVIEW2_* variables for elevated apps. Set the " +
                      @"HKLM\SOFTWARE\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments policy for the app's executable to " +
                      $"--remote-debugging-port=<port> and set {WorkbenchHost.DevToolsPortVariable} to the same port (see docs/hybrid.md)."
                    : "";
                throw new WorkbenchInfrastructureException($"Could not connect to the WebView at {endpoint}: {ex.Message.Split('\n')[0]}.{hint}", ex);
            }
        }
    }

    public static async Task<HybridWebView> OpenAsync(IBrowser browser, Uri appOrigin, CancellationToken cancellationToken)
    {
        var origin = appOrigin.GetLeftPart(UriPartial.Authority);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var page = browser.Contexts.SelectMany(c => c.Pages).FirstOrDefault(p => p.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase));
            if (page is not null)
            {
                var cdp = await page.Context.NewCDPSessionAsync(page).ConfigureAwait(false);
                var view = new HybridWebView(page, cdp);
                await view.SendAsync("Emulation.setTimezoneOverride", new() { ["timezoneId"] = "UTC" }).ConfigureAwait(false);
                return view;
            }

            if (DateTime.UtcNow > deadline)
                throw new WorkbenchInfrastructureException($"The WebView has no page at {origin}. Is the app a Blazor Workbench host (UseBlazorWorkbenchApp)?");
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Waits for exclusive use of the page and applies the variant. Call <see cref="Release"/> when done.</summary>
    public async Task<IPage> AcquireAsync(ViewportPreset viewport, ThemeDto theme, string locale)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SendAsync("Emulation.setDeviceMetricsOverride", new()
            {
                ["width"] = viewport.Width,
                ["height"] = viewport.Height,
                ["deviceScaleFactor"] = viewport.DeviceScaleFactor,
                ["mobile"] = viewport.IsMobile,
            }).ConfigureAwait(false);
            // Mobile presets also emulate touch, so (hover: none), (pointer: coarse) and touch events behave like a phone.
            await SendAsync("Emulation.setTouchEmulationEnabled", viewport.IsMobile
                ? new() { ["enabled"] = true, ["maxTouchPoints"] = 5 }
                : new() { ["enabled"] = false }).ConfigureAwait(false);
            await SendAsync("Emulation.setEmulatedMedia", new()
            {
                ["features"] = new object[]
                {
                    new Dictionary<string, object> { ["name"] = "prefers-color-scheme", ["value"] = theme.ColorScheme == ColorScheme.Dark ? "dark" : "light" },
                    new Dictionary<string, object> { ["name"] = "prefers-reduced-motion", ["value"] = "reduce" },
                },
            }).ConfigureAwait(false);
            if (!string.Equals(_locale, locale, StringComparison.OrdinalIgnoreCase))
            {
                // A locale override must be cleared before it can change.
                await SendAsync("Emulation.setLocaleOverride", []).ConfigureAwait(false);
                await SendAsync("Emulation.setLocaleOverride", new() { ["locale"] = CultureInfo.GetCultureInfo(locale).Name }).ConfigureAwait(false);
                _locale = locale;
            }

            return _page;
        }
        catch
        {
            _lock.Release();
            throw;
        }
    }

    public void Release() => _lock.Release();

    /// <summary>Changes the emulated viewport size of the acquired page, keeping the variant's scale factor and mobile mode.</summary>
    public Task ResizeAsync(ViewportPreset viewport, int width, int height) =>
        SendAsync("Emulation.setDeviceMetricsOverride", new()
        {
            ["width"] = width,
            ["height"] = height,
            ["deviceScaleFactor"] = viewport.DeviceScaleFactor,
            ["mobile"] = viewport.IsMobile,
        });

    private async Task SendAsync(string method, Dictionary<string, object> args)
    {
        try
        {
            await _cdp.SendAsync(method, args).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            throw new WorkbenchInfrastructureException($"The WebView rejected {method}: {ex.Message.Split('\n')[0]}", ex);
        }
    }
}
