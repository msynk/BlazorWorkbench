using System.Net;
using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Commerce;
using BlazorWorkbench.Demo.Fakes;

namespace BlazorWorkbench.Demo.Scenarios;

// Feature-level components of a store. Each one depends on things that are hard to reproduce in a running app (a payment
// API that declines, a sale that ends in 45 seconds, a store in another currency), which is where scenarios pay off.

public sealed class CheckoutScenarios : ScenarioSet<CheckoutForm>
{
    protected override void Configure(ScenarioSetBuilder<CheckoutForm> set)
    {
        set.Title("Commerce/Checkout")
           .Tags("http", "forms", "showcase")
           .Documentation("""
               Checkout posts the order to `POST api/orders` and must handle every answer the payment service can give.
               In a running app most of these answers are hard to produce on demand; here each one is a scenario.

               | Scenario | Fake backend |
               | --- | --- |
               | Ready to pay | `201 Created` after 300 ms |
               | Placing order (gated) | Held at the `payment` gate until you release it |
               | Card declined | `402` with a problem response |
               | Item sold out | `409` with a problem detail |
               | Server-side validation | A custom handler that reads the request body |
               | Flaky network | The first request fails, the retry succeeds |
               | Payment timeout | The request times out |

               **Try it**

               - Open *Placing order (gated)*, click **Place order**, then release the `payment` gate in **Environment**.
               - Open *Server-side validation* and submit: the PO box is rejected by the "server". Change the address and submit again.
               - Switch the culture in the toolbar to `ja-JP`: yen have no decimals, and the totals follow.
               - Run **Interactions**: the gated test also records visual checkpoints (`submitting`, `confirmed`) in `--visual` runs.
               """);

        set.Fixture<IReadOnlyList<CartLine>>("Tent and headlamps", _ => StoreData.Cart, "Free standard shipping");
        set.Fixture<IReadOnlyList<CartLine>>("Two mugs", _ => StoreData.SmallCart, "Below the free shipping threshold");
        set.Fixture<IReadOnlyList<CartLine>>("Empty cart", _ => []);
        set.Setup(s => s
            .Fixture(c => c.Lines, "Tent and headlamps")
            .User(u => u.Name("Ada Lovelace").Claim("email", "ada@example.com"))
            .ParameterFrom(c => c.Initial, _ => new CheckoutDetails { Address = "12 Lake Road, Springfield" }, "Saved address")
            .Http(h => h.Post("/api/orders").RespondJson(StoreData.Confirmation, HttpStatusCode.Created).Delay(TimeSpan.FromMilliseconds(300))));

        set.Add("Ready to pay", s => s
            .Description("Name and email come from the simulated user's claims. Click Place order: the fake backend answers 201 after 300 ms.")
            .Interaction("places the order", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectVisible(By.Role("heading", "Order placed"))
                .ExpectText(By.TestId("order-number"), "TH-10432")
                .ExpectCallback("OnOrderPlaced", times: 1)
                .ExpectNoErrors())
            .Interaction("express delivery updates the total", i => i
                .ExpectText(By.TestId("total"), "$439.00")
                .Select(By.Label("Delivery"), "Express")
                .ExpectText(By.TestId("total"), "$454.00")));

        set.Add("Guest checkout", s => s
            .Anonymous()
            .Description("Signed out, below the free shipping threshold. Click Place order with empty fields to see client-side validation.")
            .Parameter(c => c.Initial, null)
            .Fixture(c => c.Lines, "Two mugs")
            .Interaction("requires contact details", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectVisible(By.Text("Enter your name."))
                .ExpectCount(By.Css(".validation-message"), 3)
                .ExpectCallback("OnOrderPlaced", times: 0))
            .Interaction("offers sign in", i => i
                .Click(By.Role("button", "Sign in"))
                .ExpectCallback("OnSignIn", times: 1)));

        set.Add("Placing order (gated)", s => s
            .Http(h => h.Post("/api/orders").Hold("payment").RespondJson(StoreData.Confirmation, HttpStatusCode.Created))
            .Description("The order request waits at the 'payment' gate. Click Place order, then release the gate in Environment.")
            .Interaction("shows progress until the payment service answers", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectVisible(By.Text("Placing your order"))
                .ExpectDisabled(By.Role("button", "Placing your order"))
                .Checkpoint("submitting")
                .ReleaseGate("payment")
                .ExpectVisible(By.Role("heading", "Order placed"))
                .ExpectCallback("OnOrderPlaced", times: 1)
                .Checkpoint("confirmed")));

        set.Add("Card declined", s => s
            .Description("Click Place order: the payment API answers 402 with a problem response. The form keeps what the customer typed.")
            .Http(h => h.Post("/api/orders").RespondJson(new ProblemDetailsResponse("Card declined", "Insufficient funds."), HttpStatusCode.PaymentRequired))
            .Interaction("explains the decline and keeps the form", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectText(By.Role("alert"), "Your card was declined")
                .ExpectVisible(By.Label("Full name"))
                .ExpectCallback("OnOrderPlaced", times: 0)));

        set.Add("Item sold out", s => s
            .Description("Click Place order: 409 Conflict, and the server's explanation is shown as is.")
            .Http(h => h.Post("/api/orders").RespondJson(
                new ProblemDetailsResponse("Out of stock", "Headlamp 400 sold out while you were checking out. Remove it to continue."),
                HttpStatusCode.Conflict))
            .Interaction("shows the server's explanation", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectText(By.Role("alert"), "Headlamp 400 sold out")));

        set.Add("Server-side validation", s => s
            .ParameterFrom(c => c.Initial, _ => new CheckoutDetails { Address = "PO Box 42, Springfield" }, "PO box address")
            .Http(h => h.Post("/api/orders").Handle(FakeBackends.PlaceOrder))
            .Description("A custom handler reads the posted JSON and rejects PO boxes with 422, like the real API.")
            .Interaction("fixes a rejected address", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectText(By.Css(".validation-message"), "can't deliver to PO boxes")
                .Fill(By.Label("Shipping address"), "12 Lake Road, Springfield")
                .Click(By.Role("button", "Place order"))
                .ExpectVisible(By.Role("heading", "Order placed"))));

        set.Add("Flaky network", s => s
            .Http(h => h.Post("/api/orders").Times(1).NetworkError())
            .Description("The first request fails with a connection error; the retry reaches the shared 201 rule.")
            .Interaction("succeeds on retry", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectText(By.Role("alert"), "offline")
                .Click(By.Role("button", "Place order"))
                .ExpectVisible(By.Role("heading", "Order placed"))
                .ExpectCallback("OnOrderPlaced", times: 1)));

        set.Add("Payment timeout", s => s
            .Description("Click Place order: the request times out, and the message tells the customer they were not charged.")
            .Http(h => h.Post("/api/orders").Timeout())
            .Interaction("reassures the customer", i => i
                .Click(By.Role("button", "Place order"))
                .ExpectText(By.Role("alert"), "You have not been charged")));

        set.Add("Empty cart", s => s
            .Fixture(c => c.Lines, "Empty cart")
            .Description("Nothing to pay for: Place order is disabled. Pick another cart under Lines in Controls.")
            .Interaction("cannot be submitted", i => i
                .ExpectVisible(By.Text("Your cart is empty."))
                .ExpectDisabled(By.Role("button", "Place order"))));

        set.Add("Yen (Japanese)", s => s.Culture("ja-JP").Description("The same cart in ja-JP: yen have no decimals, and every total follows."));
        set.Add("Euro (French)", s => s
            .Culture("fr-FR")
            .Fixture(c => c.Lines, "Two mugs")
            .Description("fr-FR: decimal comma, the euro sign after the amount, and shipping is charged below the threshold."));
    }
}

public sealed class ProductSearchScenarios : ScenarioSet<ProductSearch>
{
    protected override void Configure(ScenarioSetBuilder<ProductSearch> set)
    {
        set.Title("Commerce/Product search")
           .Tags("http", "showcase")
           .Documentation("""
               Search calls `GET api/products/search?q=...&sort=...` through `IHttpClientFactory`. Instead of one canned
               response per query, the scenarios use a **custom handler** (`.Handle(FakeBackends.SearchProducts)`) that filters
               and sorts a fixed catalog of 24 products, so the component can be explored freely, like against a real API.

               **Try it**

               - Type `jacket`, then sort by price. Every request appears in **Diagnostics** with its URL and the matched rule.
               - Open *Slow search (gated)*: the request waits at the `search` gate, so the *Searching…* state can be reviewed.
               - Open *Search unavailable* and click **Retry**: the first request fails with 503, the retry succeeds.
               """);

        set.Setup(s => s.Http(h => h
            .Latency(TimeSpan.FromMilliseconds(120))
            .Get("/api/products/search").Handle(FakeBackends.SearchProducts)));

        set.Add("Start typing", s => s
            .Description("Type 'jacket', then sort by price. A custom handler answers every request; Diagnostics lists each one.")
            .Interaction("finds products by name or category", i => i
                .Fill(By.Label("Search products"), "jacket")
                .ExpectText(By.TestId("summary"), "4 results for “jacket”")
                .ExpectCount(By.Css(".search-hit"), 4))
            .Interaction("sorts by price", i => i
                .Fill(By.Label("Search products"), "jacket")
                .ExpectCount(By.Css(".search-hit"), 4)
                .Select(By.Label("Sort by"), "price-asc")
                .ExpectText(By.Css(".search-hit:first-child"), "Fleece midlayer"))
            .Interaction("escape clears the search", i => i
                .Fill(By.Label("Search products"), "tent")
                .ExpectCount(By.Css(".search-hit"), 3)
                .Press("Escape", By.Label("Search products"))
                .ExpectCount(By.Css(".search-hit"), 0)));

        set.Add("Results", s => s
            .Parameter(p => p.Query, "tent")
            .Description("Starts with 'tent' typed. Click View on a product and check Actions for OnSelect.")
            .Interaction("opening a product raises OnSelect", i => i
                .Click(By.Role("button", "View Summit 2P tent"))
                .ExpectCallback("OnSelect", times: 1)));

        set.Add("Single result", s => s.Parameter(p => p.Query, "boots"));

        set.Add("No results", s => s
            .Parameter(p => p.Query, "kayak")
            .Description("The store sells no kayaks: the handler returns an empty result.")
            .Interaction("suggests a broader term", i => i.ExpectText(By.TestId("summary"), "No results for “kayak”")));

        set.Add("Slow search (gated)", s => s
            .Parameter(p => p.Query, "headlamp")
            .Description("The request waits at the 'search' gate, so 'Searching…' stays on screen. Release the gate in Environment.")
            .Http(h => h.Get("/api/products/search").Hold("search").Handle(FakeBackends.SearchProducts))
            .Interaction("shows progress, then results", i => i
                .ExpectText(By.TestId("summary"), "Searching…")
                .ReleaseGate("search")
                .ExpectCount(By.Css(".search-hit"), 2)));

        set.Add("Search unavailable", s => s
            .Parameter(p => p.Query, "stove")
            .Description("The first request answers 503. Click Retry: the second one reaches the search handler.")
            .Http(h => h.Get("/api/products/search").Times(1).Fail(HttpStatusCode.ServiceUnavailable))
            .Interaction("recovers on retry", i => i
                .ExpectText(By.Role("alert"), "Search is unavailable")
                .Click(By.Role("button", "Retry"))
                .ExpectCount(By.Css(".search-hit"), 1)));

        set.Add("Swedish prices", s => s
            .Parameter(p => p.Query, "sleeping")
            .Culture("sv-SE")
            .Description("sv-SE is not in the toolbar's culture list, but any culture works in a scenario: kronor, space as the group separator."));
    }
}

public sealed class FlashSaleScenarios : ScenarioSet<FlashSaleBanner>
{
    protected override void Configure(ScenarioSetBuilder<FlashSaleBanner> set)
    {
        set.Title("Commerce/Flash sale banner")
           .Tags("clock", "timers", "showcase")
           .Documentation("""
               The countdown reads `TimeProvider` and ticks with a `TimeProvider` timer. Every scenario freezes the clock with
               `.Clock(...)`, so each phase of the sale is one click away and screenshots never flake on a ticking second.

               **Try it**

               - Open *Last minute* and use **Environment → Clock → +1 s** a few times, then **+1 min**: the banner ends the sale.
               - Open *Starts tomorrow* and advance the clock by **+1 day**: the sale goes live, with the timer still running.
               - Without the fake clock, reviewing *ended* or *last minute* would mean waiting for real time to pass.
               """);

        set.Setup(s => s
            .Clock(StoreData.Now)
            .Parameter(b => b.Title, "Summer trail sale")
            .Parameter(b => b.Offer, "Up to 30% off tents, packs and sleeping bags.")
            .Parameter(b => b.StartsAt, StoreData.Now.AddDays(-1))
            .Parameter(b => b.EndsAt, StoreData.Now.AddHours(2)));

        set.Add("Live", s => s
            .Description("Two hours left on a frozen clock. Press +1 h twice in Environment and the sale ends.")
            .Interaction("shop now raises OnShop", i => i
                .ExpectText(By.TestId("countdown"), "02")
                .Click(By.Role("button", "Shop the sale"))
                .ExpectCallback("OnShop", times: 1)));
        set.Add("Several days left", s => s
            .Parameter(b => b.EndsAt, StoreData.Now.AddDays(3).AddHours(5))
            .Description("More than a day left: the countdown starts with days."));
        set.Add("Last minute", s => s
            .Parameter(b => b.EndsAt, StoreData.Now.AddSeconds(45))
            .Description("45 seconds left: the banner uses its urgent style. Advance the clock to end the sale."));
        set.Add("Ended", s => s
            .Parameter(b => b.EndsAt, StoreData.Now.AddHours(-3))
            .Interaction("no longer offers the sale", i => i
                .ExpectText(By.Css(".sale"), "This sale ended")
                .ExpectCount(By.Role("button", "Shop the sale"), 0)));
        set.Add("Starts tomorrow", s => s
            .Parameter(b => b.StartsAt, StoreData.Now.AddDays(1))
            .Parameter(b => b.EndsAt, StoreData.Now.AddDays(3))
            .Description("Press +1 day in Environment: the sale goes live, and the component's own timer keeps running on the fake clock."));
        set.Add("Starts tomorrow (German)", s => s
            .Culture("de-DE")
            .Parameter(b => b.StartsAt, StoreData.Now.AddDays(1))
            .Parameter(b => b.EndsAt, StoreData.Now.AddDays(3)));
    }
}

public sealed class PriceTagScenarios : ScenarioSet<PriceTag>
{
    protected override void Configure(ScenarioSetBuilder<PriceTag> set)
    {
        set.Title("Commerce/Price tag")
           .Tags("cascading-values", "localization")
           .Documentation("""
               `PriceTag` reads the store's currency and tax rules from a **cascading** `StoreSettings`, which the app shell
               provides. Scenarios supply it with `.Cascade(...)`. Remove the cascade from a scenario and `workbench validate`
               reports `WB1004` (a cascading parameter without a value), long before anyone sees a wrong price.

               The user's culture still decides separators and symbol placement: compare *UK store, German UI* with *UK store*.
               """);

        set.Setup(s => s.Parameter(p => p.Amount, 129.00m).Parameter(p => p.Size, Size.Large).Cascade(StoreSettings.UnitedStates));

        set.Add("US store (tax excluded)", s => s.Interaction("shows the net price", i => i.ExpectText(By.TestId("price"), "$129.00")));
        set.Add("EU store (VAT included)", s => s
            .Cascade(StoreSettings.Eurozone)
            .Culture("fr-FR")
            .Interaction("adds 20 % VAT", i => i.ExpectText(By.TestId("price"), "154,80")));
        set.Add("Japan store (no decimals)", s => s.Cascade(StoreSettings.Japan).Culture("ja-JP").Parameter(p => p.Amount, 18_000m));
        set.Add("UK store", s => s.Cascade(StoreSettings.UnitedKingdom).Culture("en-GB"));
        set.Add("UK store, German UI", s => s
            .Cascade(StoreSettings.UnitedKingdom)
            .Culture("de-DE")
            .Description("Pounds from the cascading store settings, decimal comma and symbol placement from the de-DE culture."));
        set.Add("On sale", s => s
            .Parameter(p => p.Amount, 90.30m)
            .Parameter(p => p.CompareAt, 129.00m)
            .Interaction("shows the original price and discount", i => i
                .ExpectText(By.Css(".price-tag-was"), "$129.00")
                .ExpectText(By.Css(".price-tag-discount"), "30")));
    }
}
