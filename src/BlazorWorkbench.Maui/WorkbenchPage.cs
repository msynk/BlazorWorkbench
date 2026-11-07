using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Hybrid;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace BlazorWorkbench.Maui;

/// <summary>
/// The Workbench page of a MAUI app: a native bar with a scenario picker, theme picker, reload and scenario-list buttons,
/// above a <see cref="WorkbenchBlazorWebView"/> that renders one preview at a time.
/// </summary>
public class WorkbenchPage : ContentPage
{
    private readonly WorkbenchBlazorWebView _webView = new();
    private readonly IReadOnlyList<string> _scenarioIds;
    private readonly Picker _scenarios;
    private readonly Picker _themes;

    public WorkbenchPage(WorkbenchHybridHost host, WorkbenchCatalogAccessor catalogs)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(catalogs);
        Title = host.Options.Title;

        var items = catalogs.Catalog.Components
            .SelectMany(c => c.Scenarios.Select(s => (s.Id, Display: $"{c.Title} / {s.Name}")))
            .ToList();
        _scenarioIds = items.Select(i => i.Id).ToList();
        _scenarios = new Picker { Title = "Open a scenario", ItemsSource = items.Select(i => i.Display).ToList(), AutomationId = "workbench-scenario" };
        _scenarios.SelectedIndexChanged += (_, _) => Run(OpenSelectedAsync);
        _themes = new Picker { Title = "Theme", ItemsSource = host.Options.Themes.Select(t => t.Name).ToList(), AutomationId = "workbench-theme" };
        _themes.SelectedIndexChanged += (_, _) => Run(OpenSelectedAsync);

        var reload = new Button { Text = "Reload", AutomationId = "workbench-reload" };
        reload.Clicked += (_, _) => Run(ReloadAsync);
        var list = new Button { Text = "All scenarios", AutomationId = "workbench-index" };
        list.Clicked += (_, _) => Run(() => NavigateAsync(WorkbenchHybridHost.StartPath.TrimStart('/')));

        var bar = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(140)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        bar.Add(_scenarios, 0);
        bar.Add(_themes, 1);
        bar.Add(reload, 2);
        bar.Add(list, 3);

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(bar, 0, 0);
        root.Add(_webView, 0, 1);
        Content = root;
    }

    /// <summary>The preview WebView.</summary>
    public WorkbenchBlazorWebView WebView => _webView;

    /// <summary>Opens a scenario preview (a full document load, so the preview gets a fresh session).</summary>
    public Task<bool> OpenScenarioAsync(string scenarioId, string? theme = null) =>
        NavigateAsync($"_workbench/preview/{Uri.EscapeDataString(scenarioId)}" + (theme is null ? "" : $"?theme={Uri.EscapeDataString(theme)}"));

    /// <summary>Reloads the current document.</summary>
    public Task<bool> ReloadAsync() =>
        _webView.TryDispatchAsync(sp =>
        {
            var navigation = sp.GetRequiredService<NavigationManager>();
            navigation.NavigateTo(navigation.Uri, forceLoad: true);
        });

    private Task<bool> NavigateAsync(string relativeUrl) =>
        _webView.TryDispatchAsync(sp => sp.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl, forceLoad: true));

    // Event handlers must not throw: an exception in an async void handler would terminate the app.
    private static async void Run(Func<Task<bool>> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or NavigationException)
        {
            System.Diagnostics.Debug.WriteLine($"Blazor Workbench: navigation failed: {ex.Message}");
        }
    }

    private Task<bool> OpenSelectedAsync() =>
        _scenarios.SelectedIndex is var index and >= 0
            ? OpenScenarioAsync(_scenarioIds[index], _themes.SelectedItem as string)
            : Task.FromResult(false);
}

/// <summary>A MAUI application with a single window showing <see cref="WorkbenchPage"/>.</summary>
public class WorkbenchApplication(IServiceProvider services) : Application
{
    protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState) =>
        new(services.GetRequiredService<WorkbenchPage>()) { Title = services.GetRequiredService<WorkbenchHybridHost>().Options.Title };
}
