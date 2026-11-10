using System.Net.Http.Json;
using System.Reflection;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using BlazorWorkbench.Contracts;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorWorkbench.WebAssembly;

/// <summary>Registers Blazor Workbench in an Interactive WebAssembly client project.</summary>
public static class WorkbenchWebAssemblyExtensions
{
    /// <summary>
    /// Adds the Workbench runtime to a WebAssembly client: builds the catalog in the browser (XML documentation is fetched
    /// from the host), makes scenario services overridable, and connects the Visual panel to the host. Call it after the
    /// client's own service registrations and pair it with <c>Interactivity = WebAssembly</c> in the host.
    /// </summary>
    public static async Task<WebAssemblyHostBuilder> AddBlazorWorkbenchAsync(this WebAssemblyHostBuilder builder, Action<WorkbenchOptions>? configure = null)
    {
        var options = new WorkbenchOptions();
        if (Assembly.GetEntryAssembly() is { } entry)
            options.ScenarioAssemblies.Add(entry);
        configure?.Invoke(options);

        var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
        using (var http = new HttpClient { BaseAddress = baseAddress })
        {
            foreach (var assembly in options.Assemblies.Concat(options.ScenarioAssemblies).Distinct())
            {
                try
                {
                    var name = assembly.GetName().Name;
                    using var response = await http.GetAsync($"_workbench/api/xmldoc/{Uri.EscapeDataString(name ?? "")}").ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        XmlDocumentation.Register(assembly, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                catch (HttpRequestException)
                {
                    // Documentation is optional; the catalog still works without it.
                }
            }
        }

        var catalog = CatalogBuilder.Build(options);
        var overridable = ServiceOverrides.CollectTypes(catalog).ToHashSet();
        builder.Services.AddBlazorWorkbenchRuntime(options, catalog, overridable);
        builder.Services.AddScoped<IVisualResultsProvider>(_ => new HttpVisualResultsProvider(baseAddress));
        builder.Services.AddScoped<ITestResultsProvider>(_ => new HttpTestResultsProvider(baseAddress));
        builder.ConfigureContainer(new DefaultServiceProviderFactory(), services => ServiceOverrides.Apply(services, overridable));
        return builder;
    }
}

/// <summary>Reads the last run's scenario statuses from the host's <c>/_workbench/api/results</c> endpoint.</summary>
internal sealed class HttpTestResultsProvider(Uri baseAddress) : ITestResultsProvider
{
    public async Task<TestRunSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { BaseAddress = baseAddress };
        try
        {
            return await http.GetFromJsonAsync<TestRunSummary>("_workbench/api/results", WorkbenchJson.Options, cancellationToken).ConfigureAwait(false) ?? TestRunSummary.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return TestRunSummary.Empty;
        }
    }
}

/// <summary>Reads visual results from the host's <c>/_workbench/api/visual/{id}</c> endpoint.</summary>
internal sealed class HttpVisualResultsProvider(Uri baseAddress) : IVisualResultsProvider
{
    public async Task<IReadOnlyList<VisualComparisonView>> GetAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { BaseAddress = baseAddress };
        try
        {
            return await http.GetFromJsonAsync<List<VisualComparisonView>>($"_workbench/api/visual/{Uri.EscapeDataString(scenarioId)}", WorkbenchJson.Options, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (HttpRequestException ex)
        {
            return [new VisualComparisonView("report", "Error", null, null, null, null, $"Visual results could not be loaded: {ex.Message}", null)];
        }
    }

    public string Describe() => "Results and baselines served by the Workbench host.";
}
