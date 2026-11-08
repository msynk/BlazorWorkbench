using System.Net;
using System.Net.Http.Json;
using System.Web;
using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;
using BlazorWorkbench.Demo.Components.Commerce;

namespace BlazorWorkbench.Demo.Fakes;

/// <summary>
/// Custom fake HTTP handlers (<c>.Http(h => h.Get(...).Handle(...))</c>). Use them when a canned response is not enough:
/// the answer depends on the query string or the request body, like a real API.
/// </summary>
public static class FakeBackends
{
    /// <summary><c>GET api/products/search?q=&amp;sort=</c>: filters and sorts <see cref="StoreData.Catalog"/>.</summary>
    public static Task<HttpResponseMessage> SearchProducts(HttpRequestMessage request, ScenarioContext context)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri?.Query ?? "");
        var text = (query["q"] ?? "").Trim();
        var matches = StoreData.Catalog.Where(p =>
            p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || p.Category.Contains(text, StringComparison.OrdinalIgnoreCase));
        matches = query["sort"] switch
        {
            "price-asc" => matches.OrderBy(p => p.Price),
            "price-desc" => matches.OrderByDescending(p => p.Price),
            _ => matches.OrderByDescending(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ThenByDescending(p => p.Rating),
        };
        var items = matches.ToArray();
        return Task.FromResult(Json(HttpStatusCode.OK, new SearchResults(text, items.Length, items.Take(8).ToArray())));
    }

    /// <summary>
    /// <c>POST api/orders</c> with server-side validation: rejects PO boxes with a 422 problem response, accepts everything else.
    /// </summary>
    public static async Task<HttpResponseMessage> PlaceOrder(HttpRequestMessage request, ScenarioContext context)
    {
        var order = request.Content is null ? null : await request.Content.ReadFromJsonAsync<OrderRequest>();
        if (order is null)
            return Json(HttpStatusCode.BadRequest, new ProblemDetailsResponse("The order could not be read.", null));
        if (order.Address.Contains("PO Box", StringComparison.OrdinalIgnoreCase))
        {
            return Json(HttpStatusCode.UnprocessableEntity, new ProblemDetailsResponse("Check your shipping address.", null,
                new() { ["address"] = ["We can't deliver to PO boxes. Enter a street address."] }));
        }

        return Json(HttpStatusCode.Created, StoreData.Confirmation);
    }

    public static HttpResponseMessage Json<T>(HttpStatusCode status, T value) => new(status) { Content = JsonContent.Create(value) };
}

/// <summary>Every feature switched on (used with <c>.Service&lt;IFeatureFlags, AllFeaturesEnabled&gt;()</c>).</summary>
public sealed class AllFeaturesEnabled : IFeatureFlags
{
    public bool IsEnabled(string feature) => true;
}
