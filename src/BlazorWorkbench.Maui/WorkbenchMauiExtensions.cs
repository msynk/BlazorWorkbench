using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace BlazorWorkbench.Maui;

/// <summary>Registers Blazor Workbench in a .NET MAUI Blazor Hybrid app.</summary>
public static class WorkbenchMauiExtensions
{
    /// <summary>
    /// Makes this app a Workbench host: uses <see cref="WorkbenchApplication"/> (one window with the scenario picker and a
    /// preview BlazorWebView) and calls <see cref="UseBlazorWorkbench"/>. Use it in a dedicated Workbench app project.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MauiAppBuilder UseBlazorWorkbenchApp(this MauiAppBuilder builder, Action<WorkbenchHybridOptions>? configure = null, bool registerEssentials = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseMauiApp<WorkbenchApplication>();
        return Use(builder, Assembly.GetCallingAssembly(), configure, registerEssentials);
    }

    /// <summary>
    /// Registers the Workbench runtime, the preview page (<see cref="WorkbenchPage"/>) and the BlazorWebView services without
    /// changing the app's <c>Application</c>, so an existing app can show <see cref="WorkbenchPage"/> (for example in debug builds).
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="configure">Configures discovery and previews; the calling assembly is scanned for scenarios.</param>
    /// <param name="registerEssentials">
    /// Registers the platform implementations of common MAUI Essentials interfaces (<c>IConnectivity</c>, <c>IGeolocation</c>,
    /// <c>IShare</c>, ...) unless the app already registered them, so components can inject them and scenarios can replace them.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static MauiAppBuilder UseBlazorWorkbench(this MauiAppBuilder builder, Action<WorkbenchHybridOptions>? configure = null, bool registerEssentials = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Use(builder, Assembly.GetCallingAssembly(), configure, registerEssentials);
    }

    private static MauiAppBuilder Use(MauiAppBuilder builder, Assembly caller, Action<WorkbenchHybridOptions>? configure, bool registerEssentials)
    {
        var options = new WorkbenchHybridOptions
        {
            Title = caller.GetName().Name ?? "Blazor Workbench",
            AppOrigin = OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() ? new Uri("app://0.0.0.1/") : new Uri("https://0.0.0.1/"),
        };
        options.ScenarioAssemblies.Add(caller);
        configure?.Invoke(options);
        options.ApplyEnvironment();
        EnableWebViewDevTools(options);

        var services = builder.Services;
        services.AddMauiBlazorWebView();
        services.AddBlazorWebViewDeveloperTools();
        if (registerEssentials)
            AddEssentials(services);

        var host = services.AddBlazorWorkbenchHybrid(options);
        services.AddTransient<WorkbenchPage>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IMauiInitializeService, WorkbenchAutomationInitializer>());

        // Rewrite overridable registrations right before the container is built, after every app registration.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(), collection => ServiceOverrides.Apply(collection, host.OverridableTypes));
        return builder;
    }

    /// <summary>
    /// When the runner asked for a DevTools port (<c>BLAZOR_WORKBENCH_CDP_PORT</c>), makes sure WebView2 exposes it on
    /// loopback. WebView2 reads these variables when the WebView is created, after this point.
    /// </summary>
    private static void EnableWebViewDevTools(WorkbenchHybridOptions options)
    {
        if (!OperatingSystem.IsWindows() || options.DevToolsPort is not { } port)
            return;
        var value = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var arguments = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? "";
        if (!arguments.Contains("--remote-debugging-port", StringComparison.Ordinal))
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", $"{arguments} --remote-debugging-port={value}".Trim());
        // A private user data folder: a WebView joining the browser process of another instance would ignore the port.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Path.GetTempPath(), "BlazorWorkbench", "WebView2", value));
    }

    /// <summary>Registers the platform implementations of common MAUI Essentials interfaces unless they are already registered.</summary>
    public static IServiceCollection AddMauiEssentialsServices(this IServiceCollection services)
    {
        AddEssentials(services);
        return services;
    }

    private static void AddEssentials(IServiceCollection services)
    {
        services.TryAddSingleton(_ => AppInfo.Current);
        services.TryAddSingleton(_ => Battery.Default);
        services.TryAddSingleton(_ => Browser.Default);
        services.TryAddSingleton(_ => Clipboard.Default);
        services.TryAddSingleton(_ => Connectivity.Current);
        services.TryAddSingleton(_ => DeviceDisplay.Current);
        services.TryAddSingleton(_ => DeviceInfo.Current);
        services.TryAddSingleton(_ => Email.Default);
        services.TryAddSingleton(_ => FilePicker.Default);
        services.TryAddSingleton(_ => FileSystem.Current);
        services.TryAddSingleton(_ => Geolocation.Default);
        services.TryAddSingleton(_ => HapticFeedback.Default);
        services.TryAddSingleton(_ => Launcher.Default);
        services.TryAddSingleton(_ => Map.Default);
        services.TryAddSingleton(_ => MediaPicker.Default);
        services.TryAddSingleton(_ => PhoneDialer.Default);
        services.TryAddSingleton(_ => Preferences.Default);
        services.TryAddSingleton(_ => SecureStorage.Default);
        services.TryAddSingleton(_ => SemanticScreenReader.Default);
        services.TryAddSingleton(_ => Share.Default);
        services.TryAddSingleton(_ => Sms.Default);
        services.TryAddSingleton(_ => TextToSpeech.Default);
        services.TryAddSingleton(_ => Vibration.Default);
    }
}

/// <summary>Starts the automation endpoint when the app was launched by <c>workbench test</c>.</summary>
internal sealed class WorkbenchAutomationInitializer : IMauiInitializeService
{
    private static HybridAutomationServer? s_server;

    public void Initialize(IServiceProvider services)
    {
        var host = services.GetRequiredService<WorkbenchHybridHost>();
        var catalogs = services.GetRequiredService<WorkbenchCatalogAccessor>();
        s_server ??= HybridAutomation.StartIfConfigured(host, catalogs);
    }
}
