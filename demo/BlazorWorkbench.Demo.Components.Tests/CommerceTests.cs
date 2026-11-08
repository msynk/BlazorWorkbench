using Bunit;
using BlazorWorkbench.Testing.Bunit;
using SampleWorkbench.Scenarios;

namespace SampleComponents.Tests;

/// <summary>Store components tested through their scenarios: the fake backend, gates and culture come from the scenario.</summary>
public class CommerceTests : SampleTestContext
{
    [Fact]
    public void A_declined_card_is_explained_and_nothing_is_ordered()
    {
        var cut = this.RenderScenario<CheckoutScenarios>("card-declined");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Your card was declined", cut.Find("[role=alert]").TextContent));
        Assert.Equal(0, cut.CallbackCount("OnOrderPlaced"));
        Assert.Equal(402, Assert.Single(cut.Session().Http.Exchanges).Status);
    }

    [Fact]
    public void A_held_payment_shows_progress_until_the_gate_opens()
    {
        var cut = this.RenderScenario<CheckoutScenarios>("placing-order-gated");

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Placing your order", cut.Find("button[type=submit]").TextContent));

        cut.Session().Gates.Release("payment");

        cut.WaitForAssertion(() => Assert.Equal("TH-10432", cut.Find("[data-testid=order-number]").TextContent));
        Assert.Equal(1, cut.CallbackCount("OnOrderPlaced"));
    }

    [Fact]
    public void Server_side_validation_errors_appear_next_to_the_field()
    {
        var cut = this.RenderScenario<CheckoutScenarios>("server-side-validation");

        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("PO boxes", cut.Find(".validation-message").TextContent));

        cut.Find("#checkout-address").Change("12 Lake Road, Springfield");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("TH-10432", cut.Find("[data-testid=order-number]").TextContent));
    }

    [Fact]
    public void Yen_totals_have_no_decimals()
    {
        var cut = this.RenderScenario<CheckoutScenarios>("yen-japanese");

        var total = cut.Find("[data-testid=total]").TextContent;
        Assert.Contains("439", total);
        Assert.DoesNotContain(".00", total);
    }

    [Fact]
    public void Search_results_come_from_the_query_string()
    {
        var cut = this.RenderScenario<ProductSearchScenarios>("start-typing");

        cut.Find("input[type=search]").Input("jacket");
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".search-hit").Count));

        cut.Find("select").Change("price-asc");
        cut.WaitForAssertion(() => Assert.Contains("Fleece midlayer", cut.Find(".search-hit").TextContent));
        Assert.Contains(cut.Session().Http.Exchanges, e => e.Url.Contains("sort=price-asc", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failing_endpoint_only_breaks_its_own_widget()
    {
        var cut = this.RenderScenario<SalesDashboardScenarios>("partial-outage");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Recent orders could not be loaded", cut.Find("[role=alert]").TextContent);
            Assert.Equal(4, cut.FindAll("[data-testid=kpi-value]").Count);
        });
    }
}
