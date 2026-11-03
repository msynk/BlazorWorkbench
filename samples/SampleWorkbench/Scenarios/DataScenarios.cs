using System.Net;
using BlazorWorkbench;
using Microsoft.AspNetCore.Components;
using SampleComponents;
using SampleComponents.Data;
using SampleWorkbench.Fakes;

namespace SampleWorkbench.Scenarios;

/// <summary>A closed generic component: <c>DataTable&lt;Person&gt;</c> with templates and fixtures.</summary>
public sealed class PeopleTableScenarios : ScenarioSet<DataTable<Person>>
{
    private static readonly RenderFragment Header = b =>
    {
        b.AddMarkupContent(0, "<th scope=\"col\">Name</th><th scope=\"col\">Email</th><th scope=\"col\">Age</th><th scope=\"col\">Country</th>");
    };

    private static readonly RenderFragment<Person> Row = person => b =>
    {
        b.OpenElement(0, "td"); b.AddContent(1, person.Name); b.CloseElement();
        b.OpenElement(2, "td"); b.AddContent(3, person.Email); b.CloseElement();
        b.OpenElement(4, "td"); b.AddContent(5, person.Age); b.CloseElement();
        b.OpenElement(6, "td"); b.AddContent(7, person.Country); b.CloseElement();
    };

    protected override void Configure(ScenarioSetBuilder<DataTable<Person>> set)
    {
        set.Title("Data/Data table").Tags("generic", "templates");
        set.Fixture<IReadOnlyList<Person>>("Three people", ctx => SampleData.People(ctx.Random, 3), "Seeded, deterministic sample");
        set.Fixture<IReadOnlyList<Person>>("Fifty people", ctx => SampleData.People(ctx.Random, 50));
        set.Fixture<IReadOnlyList<Person>>("Nobody", _ => []);
        set.Setup(s => s
            .Fragment(t => t.HeaderTemplate, Header, "Name/Email/Age/Country headers")
            .Template(t => t.RowTemplate, Row, "Person row")
            .Parameter(t => t.Caption, "Team members"));

        set.Add("Few rows", s => s
            .Fixture(t => t.Items, "Three people")
            .Interaction("renders three rows", i => i.ExpectCount(By.Css("tbody tr"), 3)));
        set.Add("Many rows", s => s.Fixture(t => t.Items, "Fifty people").Visual(v => v.FullPage()));
        set.Add("Empty", s => s
            .Fixture(t => t.Items, "Nobody")
            .Parameter(t => t.EmptyText, "No team members yet.")
            .Interaction("shows the empty text", i => i.ExpectText(By.Css(".table-empty"), "No team members yet.")));
    }
}

public sealed class WeatherScenarios : ScenarioSet<WeatherForecast>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly Forecast Sunny = new("Tehran", 31.5, "Sunny", Now.AddMinutes(-20));

    protected override void Configure(ScenarioSetBuilder<WeatherForecast> set)
    {
        set.Title("Data/Weather forecast")
           .Tags("http", "async")
           .Documentation("""
               Loads data with an injected `HttpClient`. Every scenario uses Workbench's fake HTTP backend, so no request
               ever leaves the machine. Unmatched requests return 404 and appear in **Diagnostics**.
               """);
        set.Setup(s => s.Parameter(w => w.City, "Tehran").Clock(Now));

        set.Add("Loaded", s => s
            .Http(h => h.Get("/api/weather?city=Tehran").RespondJson(Sunny).Delay(TimeSpan.FromMilliseconds(150)))
            .Interaction("shows the temperature", i => i.ExpectText(By.TestId("temperature"), "31.5 °C").ExpectNoErrors()));

        set.Add("Loading", s => s
            .Http(h => h.Get("/api/weather").Hold("weather").RespondJson(Sunny))
            .Description("The request is held at the 'weather' gate: release it from the Environment panel.")
            .Interaction("completes after the gate opens", i => i
                .ExpectVisible(By.Text("Loading forecast"))
                .ReleaseGate("weather")
                .ExpectText(By.TestId("temperature"), "31.5")));

        // Shared fallback response. Scenario rules are evaluated before shared (Setup) rules.
        set.Setup(s => s.Http(h => h.Get("/api/weather").RespondJson(Sunny)));

        set.Add("Empty", s => s.Http(h => h.Get("/api/weather").Respond(HttpStatusCode.NoContent)));

        set.Add("Server error", s => s
            .Http(h => h.Get("/api/weather").Fail(HttpStatusCode.InternalServerError))
            .Interaction("shows an error with retry", i => i.ExpectVisible(By.Role("alert")).ExpectVisible(By.Role("button", "Retry"))));

        set.Add("Timeout", s => s.Http(h => h.Get("/api/weather").Timeout()));

        set.Add("Offline", s => s.Offline());

        set.Add("Recovers after retry", s => s
            .Http(h => h.Get("/api/weather").Times(1).Fail(HttpStatusCode.ServiceUnavailable))
            .Description("The first request fails and the second succeeds, exercising the retry path.")
            .Interaction("retry loads the forecast", i => i
                .Click(By.Role("button", "Retry"))
                .ExpectText(By.TestId("temperature"), "31.5")
                .ExpectNoErrors()));

        set.Add("Persian", s => s
            .Culture("fa-IR")
            .Parameter(w => w.City, "تهران")
            .Http(h => h.Get("/api/weather").RespondJson(Sunny with { City = "تهران", Summary = "آفتابی" })));
    }
}

public sealed class ProductListScenarios : ScenarioSet<ProductList>
{
    protected override void Configure(ScenarioSetBuilder<ProductList> set)
    {
        set.Title("Data/Product list").Tags("dependency-injection");
        set.Documentation("Replaces `IProductService` per scenario. Each activation gets a fresh fake, so state never leaks between scenarios.");

        set.Add("In stock", s => s
            .Service<IProductService>(_ => new FakeProductService(SampleData.Products))
            .Interaction("adding a product raises OnAdd", i => i
                .Click(By.Role("button", "Add Mechanical keyboard to cart"))
                .ExpectCallback("OnAdd", times: 1)));

        set.Add("Empty", s => s.Service<IProductService>(_ => new FakeProductService([])));

        set.Add("Loading (gated)", s => s
            .Service<IProductService>(ctx => new FakeProductService(SampleData.Products, ctx.Gates, "products"))
            .Interaction("loads after the gate opens", i => i
                .ExpectVisible(By.Text("Loading products"))
                .ReleaseGate("products")
                .ExpectCount(By.Css(".product"), 3)));

        set.Add("Euro prices", s => s.Culture("fr-FR").Service<IProductService>(_ => new FakeProductService(SampleData.Products)));
    }
}
