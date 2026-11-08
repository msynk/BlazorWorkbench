using System.Net;
using BlazorWorkbench;
using BlazorWorkbench.Demo.Components.Analytics;
using BlazorWorkbench.Demo.Fakes;

namespace BlazorWorkbench.Demo.Scenarios;

public sealed class KpiCardScenarios : ScenarioSet<KpiCard>
{
    protected override void Configure(ScenarioSetBuilder<KpiCard> set)
    {
        set.Title("Analytics/KPI card").Tags("charts");
        set.Documentation("Whether a change is good news depends on the metric. `HigherIsBetter = false` turns a falling refund rate green.");

        set.Add("Revenue up", s => s
            .Parameter(k => k.Label, "Revenue")
            .Parameter(k => k.Value, 47_600m)
            .Parameter(k => k.Format, "C0")
            .Parameter(k => k.Change, 0.08)
            .Parameter(k => k.Trend, (IReadOnlyList<double>)[31.2, 33.4, 36.1, 41.8, 38.9, 44.1, 47.6]));
        set.Add("Conversion down", s => s
            .Parameter(k => k.Label, "Conversion rate")
            .Parameter(k => k.Value, 0.031m)
            .Parameter(k => k.Format, "P1")
            .Parameter(k => k.Change, -0.052)
            .Parameter(k => k.Trend, (IReadOnlyList<double>)[3.4, 3.3, 3.5, 3.2, 3.3, 3.2, 3.1]));
        set.Add("Refunds down (good news)", s => s
            .Parameter(k => k.Label, "Refund rate")
            .Parameter(k => k.Value, 0.018m)
            .Parameter(k => k.Format, "P1")
            .Parameter(k => k.Change, -0.12)
            .Parameter(k => k.HigherIsBetter, false)
            .Interaction("a falling refund rate is shown as good", i => i.ExpectVisible(By.Css(".kpi-change-good"))));
        set.Add("No change", s => s.Parameter(k => k.Label, "Active users").Parameter(k => k.Value, 812m).Parameter(k => k.Change, 0.0));
        set.Add("Loading", s => s.Parameter(k => k.Label, "Orders").Parameter(k => k.Loading, true));
    }
}

public sealed class RevenueChartScenarios : ScenarioSet<RevenueChart>
{
    protected override void Configure(ScenarioSetBuilder<RevenueChart> set)
    {
        set.Title("Analytics/Revenue chart")
           .Tags("charts", "visual", "showcase")
           .Documentation("""
               An SVG chart is exactly the kind of component where a screenshot says more than any assertion. Visual
               regression catches a shifted axis, a clipped label or a bar of the wrong height.

               - *Seeded data* draws random revenue from the scenario's seeded `Random` (`.Seed(7)`): different data per seed,
                 identical data on every run and machine, so the baseline never flakes.
               - *Light and dark* is captured in both themes (`.Visual(v => v.Themes("light", "dark"))`).
               - *Persian calendar* shows period labels in the `fa-IR` culture, which uses the Persian calendar.
               """);

        set.Setup(s => s.Parameter(c => c.Title, "Monthly revenue").Parameter(c => c.Points, StoreData.Revenue));

        set.Add("Twelve months", s => s
            .Interaction("draws one bar per month", i => i.ExpectCount(By.Css(".chart-bar"), 12)));
        set.Add("Seeded data", s => s
            .Seed(7)
            .ParameterFrom(c => c.Points, ctx => StoreData.RandomRevenue(ctx.Random, 12), "Random revenue (seed 7)"));
        set.Add("Light and dark", s => s.Visual(v => v.Themes("light", "dark")));
        set.Add("Persian calendar", s => s.Culture("fa-IR"));
        set.Add("Two years", s => s
            .Seed(24)
            .Parameter(c => c.Title, "Revenue, last 24 months")
            .ParameterFrom(c => c.Points, ctx => StoreData.RandomRevenue(ctx.Random, 24), "Random revenue (24 months)")
            .Description("Labels are thinned out so they never overlap."));
        set.Add("No data", s => s.Parameter(c => c.Points, (IReadOnlyList<RevenuePoint>)[]));
    }
}

public sealed class SalesDashboardScenarios : ScenarioSet<SalesDashboard>
{
    protected override void Configure(ScenarioSetBuilder<SalesDashboard> set)
    {
        set.Title("Analytics/Sales dashboard")
           .Tags("http", "composition", "showcase")
           .Documentation("""
               A whole page built from library components (`KpiCard`, `RevenueChart`, `DataTable`, `Badge`, `DateLabel`).
               Each widget calls its own endpoint, which is what makes partial failures and staggered loading worth testing.

               | Scenario | What it shows |
               | --- | --- |
               | Loaded | Three endpoints answered by the fake backend; the greeting follows the simulated user and clock |
               | Loading (gated) | Every request held at the `dashboard` gate: skeletons can be reviewed and screenshotted |
               | Partial outage | Orders answer 500 while the rest loads: only that widget shows an error |
               | Slow network | 1.2 s latency on every request; readiness still waits for the data, without sleeps |
               | Offline | Every request fails with a connection error |
               | Phone | The `mobile` viewport: the grid stacks into one column |
               | Evening, dark theme | A different clock and theme, nothing else changed |

               **Try it:** change **Period** to *Last 6 months*. The request goes to `api/dashboard/revenue?range=6m`, which
               a more specific fake rule answers (query parameters match as a subset).
               """);

        set.Setup(s => s
            .Clock(StoreData.Now)
            .User("Ada Lovelace", "Admin")
            .Http(h =>
            {
                h.Get("/api/dashboard/kpis").RespondJson(StoreData.Kpis);
                h.Get("/api/dashboard/revenue?range=6m").RespondJson(StoreData.RevenueLastSixMonths);
                h.Get("/api/dashboard/revenue").RespondJson(StoreData.Revenue);
                h.Get("/api/orders/recent").RespondJson(StoreData.RecentOrders);
            }));

        set.Add("Loaded", s => s
            .Budget(readyWithin: TimeSpan.FromSeconds(3))
            .Interaction("shows every widget", i => i
                .ExpectText(By.Css(".dashboard-title"), "Good morning, Ada")
                .ExpectCount(By.TestId("kpi-value"), 4)
                .ExpectCount(By.Css(".chart-bar"), 12)
                .ExpectCount(By.Css("table.table tbody tr"), 5)
                .ExpectNoErrors())
            .Interaction("switching the period reloads the chart", i => i
                .ExpectCount(By.Css(".chart-bar"), 12)
                .Select(By.Label("Period"), "6m")
                .ExpectCount(By.Css(".chart-bar"), 6)));

        set.Add("Loading (gated)", s => s
            .Http(h =>
            {
                h.Get("/api/dashboard/kpis").Hold("dashboard").RespondJson(StoreData.Kpis);
                h.Get("/api/dashboard/revenue").Hold("dashboard").RespondJson(StoreData.Revenue);
                h.Get("/api/orders/recent").Hold("dashboard").RespondJson(StoreData.RecentOrders);
            })
            .Description("Every request waits at the 'dashboard' gate. Release it in Environment to load all widgets at once.")
            .Interaction("loads after the gate opens", i => i
                .ExpectCount(By.Css(".kpi-loading"), 4)
                .ReleaseGate("dashboard")
                .ExpectCount(By.TestId("kpi-value"), 4)
                .ExpectCount(By.Css("table.table tbody tr"), 5)));

        set.Add("Partial outage", s => s
            .Http(h => h.Get("/api/orders/recent").Fail(HttpStatusCode.InternalServerError))
            .Interaction("only the orders widget fails", i => i
                .ExpectText(By.Role("alert"), "Recent orders could not be loaded")
                .ExpectCount(By.TestId("kpi-value"), 4)
                .ExpectCount(By.Css(".chart-bar"), 12)));

        set.Add("Slow network", s => s
            .Http(h => h.Latency(TimeSpan.FromMilliseconds(1200)))
            .Description("Every request takes 1.2 s. The preview turns ready only after the data arrived: no sleeps in tests."));

        set.Add("Offline", s => s.Offline());

        set.Add("Phone", s => s.Viewport("mobile"));

        set.Add("Evening, dark theme", s => s
            .Clock(StoreData.Now.AddHours(10))
            .Theme("dark")
            .Visual(v => v.Themes("dark")));
    }
}
