using System.ComponentModel.DataAnnotations;

namespace BlazorWorkbench.Demo.Components.Commerce;

/// <summary>A line in the shopping cart.</summary>
public sealed record CartLine(string Sku, string Name, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}

/// <summary>How an order is shipped.</summary>
public enum DeliveryOption { Standard, Express }

/// <summary>Contact and shipping details entered at checkout.</summary>
public sealed class CheckoutDetails
{
    [Required(ErrorMessage = "Enter your name.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Enter a shipping address.")]
    public string? Address { get; set; }

    public DeliveryOption Delivery { get; set; }
}

/// <summary>Body of <c>POST api/orders</c>.</summary>
public sealed record OrderRequest(string Name, string Email, string Address, DeliveryOption Delivery, IReadOnlyList<OrderRequestLine> Lines);

/// <summary>One line of an <see cref="OrderRequest"/>.</summary>
public sealed record OrderRequestLine(string Sku, int Quantity);

/// <summary>Response of a successful <c>POST api/orders</c>.</summary>
public sealed record OrderConfirmation(string OrderNumber, DateTimeOffset EstimatedDelivery);

/// <summary>An RFC 9457 problem details response. Validation failures carry <see cref="Errors"/> keyed by field name.</summary>
public sealed record ProblemDetailsResponse(string? Title, string? Detail, Dictionary<string, string[]>? Errors = null);

/// <summary>A product returned by <c>GET api/products/search</c>.</summary>
public sealed record ProductHit(string Sku, string Name, string Category, decimal Price, double Rating, bool InStock);

/// <summary>Response of <c>GET api/products/search</c>.</summary>
public sealed record SearchResults(string Query, int Total, IReadOnlyList<ProductHit> Items);

/// <summary>
/// Store-wide pricing rules. The app shell provides them as a cascading value; the user's culture still decides number
/// formatting (separators, symbol placement), while the store decides the currency and how tax is shown.
/// </summary>
public sealed record StoreSettings(string Name, string CurrencyCode, string CurrencySymbol, int CurrencyDecimals, decimal TaxRate, string TaxName, bool PricesIncludeTax)
{
    public static StoreSettings UnitedStates { get; } = new("US store", "USD", "$", 2, 0.08m, "sales tax", false);
    public static StoreSettings Eurozone { get; } = new("EU store", "EUR", "€", 2, 0.20m, "VAT", true);
    public static StoreSettings UnitedKingdom { get; } = new("UK store", "GBP", "£", 2, 0.20m, "VAT", true);
    public static StoreSettings Japan { get; } = new("Japan store", "JPY", "¥", 0, 0.10m, "consumption tax", true);
}
