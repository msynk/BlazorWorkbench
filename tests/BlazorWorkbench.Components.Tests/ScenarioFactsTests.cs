using System.Text.RegularExpressions;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Components.Shell;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using BlazorWorkbench.Demo;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Scenarios;

namespace BlazorWorkbench.Components.Tests;

public class ScenarioFactsTests
{
    private static readonly Lazy<(WorkbenchOptions Options, WorkbenchCatalog Catalog)> Demo = new(() =>
    {
        var options = new WorkbenchOptions();
        DemoCatalog.Configure(options);
        options.ScenarioAssemblies.Add(typeof(CheckoutScenarios).Assembly);
        return (options, CatalogBuilder.Build(options));
    });

    private static List<string> Facts(string scenarioId, ScenarioFactGroup? group = null)
    {
        var (options, catalog) = Demo.Value;
        var scenario = catalog.FindScenario(scenarioId) ?? throw new InvalidOperationException($"Scenario {scenarioId} not found.");
        return ScenarioFacts.Describe(scenario, options).Where(f => group is null || f.Group == group).Select(f => $"{f.Kind}: {f.Text}").ToList();
    }

    [Fact]
    public void Shows_the_rule_that_answers_and_hides_shadowed_shared_rules()
    {
        var facts = Facts("commerce-checkout--card-declined");
        Assert.Contains("HTTP: POST /api/orders → 402 Payment Required", facts);
        // The shared 201 rule from Setup can never match: the scenario's rule answers every POST.
        Assert.DoesNotContain(facts, f => f.Contains("201", StringComparison.Ordinal));
        Assert.Contains("User: Ada Lovelace · email=ada@example.com", facts);
    }

    [Fact]
    public void Shows_rules_limited_by_times_followed_by_the_fallback()
    {
        var facts = Facts("commerce-checkout--flaky-network");
        Assert.Contains("HTTP: POST /api/orders → network error, first request only", facts);
        Assert.Contains("HTTP: POST /api/orders → 201 Created after 300 ms", facts);
    }

    [Fact]
    public void Shows_gates_handlers_and_latency()
    {
        Assert.Contains("HTTP: POST /api/orders → 201 Created, held at gate 'payment'", Facts("commerce-checkout--placing-order-gated"));
        Assert.Contains("HTTP: GET /api/products/search → custom handler", Facts("commerce-product-search--start-typing"));
        Assert.Contains("HTTP: 1.2 s latency", Facts("analytics-sales-dashboard--slow-network"));
        Assert.Equal(new[] { "Network: offline, every request fails" }, Facts("data-weather-forecast--offline", ScenarioFactGroup.Simulates).Where(f => f.StartsWith("Network", StringComparison.Ordinal)));
    }

    [Fact]
    public void Shows_clock_services_device_cascades_and_environment()
    {
        Assert.Contains("Clock: frozen at Mon 15 Jun 2026, 10:00 UTC", Facts("account-session-timeout--active"));
        Assert.Contains("Services: IFeatureFlags", Facts("layout-app-header--rewards-enabled"));
        Assert.Contains("Device: simulated .NET MAUI Essentials", Facts("device-location-card--permission-denied"));
        Assert.Contains("Cascade: StoreSettings", Facts("commerce-price-tag--eu-store-vat-included"));
        Assert.Contains("Culture: fr-FR", Facts("commerce-price-tag--eu-store-vat-included"));
        Assert.Contains("Viewport: mobile (375×667)", Facts("layout-app-header--phone"));
        Assert.Contains("Random: seed 7", Facts("analytics-revenue-chart--seeded-data"));
        Assert.Contains("User: signed out", Facts("account-workspace-settings--signed-out"));
    }

    [Fact]
    public void Shows_what_the_scenario_verifies()
    {
        var primary = Facts("inputs-button--primary", ScenarioFactGroup.Verifies);
        Assert.Contains("Tests: 1 interaction test", primary);
        Assert.Contains("Budget: ready within 3 s", primary);
        Assert.Contains("Budget: at most 20 DOM updates", primary);
        Assert.Contains("Visual: screenshots: light, dark", Facts("analytics-revenue-chart--light-and-dark"));
        Assert.Contains("Accessibility: excluded from automated scans", Facts("display-card--dont-icon-actions-without-names"));
        Assert.Contains("Tests: excluded from test runs", Facts("feedback-flaky-widget--throws-while-rendering"));
    }

    [Fact]
    public void A_plain_scenario_has_no_facts() =>
        Assert.Empty(Facts("inputs-button--secondary"));

    [Fact]
    public void Every_link_of_the_demo_tour_opens_an_existing_scenario_panel_or_page()
    {
        var (_, catalog) = Demo.Value;
        var panels = new[] { "controls", "environment", "actions", "diagnostics", "interactions", "accessibility", "visual", "design", "html", "storage", "code" };
        var links = Regex.Matches(DemoTour.Markdown, @"\]\((?<url>(scenario/|docs/|pages/|tokens)[^)]*)\)").Select(m => m.Groups["url"].Value).ToList();
        Assert.True(links.Count >= 25, $"Expected the tour to link to at least 25 stops, found {links.Count}.");
        foreach (var link in links)
        {
            var parts = link.Split('?', 2);
            var path = parts[0].Split('/');
            var id = path.Length > 1 ? path[1] : "";
            switch (path[0])
            {
                case "scenario":
                    Assert.True(catalog.FindScenario(id) is not null, $"The tour links to scenario '{id}', which does not exist.");
                    break;
                case "docs":
                    Assert.True(catalog.FindComponent(id) is not null, $"The tour links to component '{id}', which does not exist.");
                    break;
                case "pages":
                    Assert.True(catalog.FindPage(id) is not null, $"The tour links to page '{id}', which does not exist.");
                    break;
            }

            // Every query parameter must be one the Workbench understands, with a valid value.
            foreach (var pair in parts.Length == 2 ? parts[1].Split('&') : [])
            {
                var (key, value) = (pair.Split('=')[0], pair.Split('=')[1]);
                var valid = key switch
                {
                    "panel" => panels.Contains(value),
                    "mode" => value is "canvas" or "compare" or "gallery",
                    "user" => catalog.Options.FindUser(value) is not null || value == "anonymous",
                    "network" => catalog.Options.FindNetworkProfile(value) is not null,
                    "vision" => PreviewTools.VisionModes.Any(v => v.Key == value),
                    "text" => PreviewTools.TextScales.Contains(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
                    "grid" or "outline" or "measure" or "tabs" or "spacing" or "updates" or "rotate" => value == "1",
                    "viewport" => catalog.Options.FindViewport(value) is not null,
                    _ => false,
                };
                Assert.True(valid, $"The tour link '{link}' has an unknown or invalid parameter '{pair}'.");
            }
        }
    }
}

public class OverviewTests : BunitContext
{
    [Fact]
    public void Renders_the_introduction_above_the_catalog()
    {
        var options = new WorkbenchOptions { Title = "Acme UI", Introduction = "Welcome to **Acme**. Start with [the button](scenario/inputs-button--primary).\n\n<script>alert(1)</script>" };
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();
        var catalog = CatalogBuilder.Build(options);

        var cut = Render<Overview>(p => p.Add(o => o.Catalog, catalog));

        var intro = cut.Find(".wb-intro");
        Assert.Contains("<strong>Acme</strong>", intro.InnerHtml);
        Assert.Equal("scenario/inputs-button--primary", intro.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll(".wb-intro script"));
        Assert.Equal("Catalog", cut.Find(".wb-intro + h2").TextContent);
    }

    [Fact]
    public void Without_an_introduction_shows_only_the_catalog()
    {
        var options = new WorkbenchOptions();
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();

        var cut = Render<Overview>(p => p.Add(o => o.Catalog, CatalogBuilder.Build(options)));

        Assert.Empty(cut.FindAll(".wb-intro"));
        Assert.NotEmpty(cut.FindAll(".wb-stats"));
    }
}

public class ScenarioFactListTests : BunitContext
{
    private static readonly ScenarioFact[] Sample =
    [
        new(ScenarioFactGroup.Simulates, "HTTP", "GET /api/x → 200 OK", "environment"),
        new(ScenarioFactGroup.Simulates, "Culture", "fr-FR", null),
        new(ScenarioFactGroup.Verifies, "Tests", "1 interaction test", "interactions"),
    ];

    [Fact]
    public void Chips_open_their_panel()
    {
        string? opened = null;
        var cut = Render<ScenarioFactList>(p => p.Add(l => l.Facts, Sample).Add(l => l.OnOpen, (string panel) => opened = panel));

        Assert.Equal(new[] { "Simulates", "Verifies" }, cut.FindAll(".wb-facts-label").Select(l => l.TextContent));
        Assert.Single(cut.FindAll("span.wb-fact"));
        cut.FindAll("button.wb-fact")[1].Click();
        Assert.Equal("interactions", opened);
    }

    [Fact]
    public void Without_a_handler_chips_link_to_the_scenario_panel()
    {
        var cut = Render<ScenarioFactList>(p => p.Add(l => l.Facts, Sample).Add(l => l.ScenarioId, "inputs-button--primary"));

        Assert.Equal(new[] { "scenario/inputs-button--primary?panel=environment", "scenario/inputs-button--primary?panel=interactions" },
            cut.FindAll("a.wb-fact").Select(a => a.GetAttribute("href")));
    }
}
