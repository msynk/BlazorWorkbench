using System.ComponentModel.DataAnnotations;

namespace SampleComponents;

public enum ButtonVariant { Primary, Secondary, Danger, Ghost }

public enum Size { Small, Medium, Large }

public enum Tone { Neutral, Info, Success, Warning, Danger }

public enum AlertSeverity { Info, Success, Warning, Error }

/// <summary>A CSS hex color such as <c>#4f46e5</c>.</summary>
public readonly record struct HexColor(string Value)
{
    public override string ToString() => Value;
}

/// <summary>A row in <see cref="Data.DataTable{TItem}"/> samples.</summary>
public sealed record Person(string Name, string Email, int Age, string Country);

/// <summary>Weather data returned by <c>GET /api/weather</c>.</summary>
public sealed record Forecast(string City, double TemperatureC, string Summary, DateTimeOffset ObservedAt);

/// <summary>A product shown by <see cref="Data.ProductList"/>.</summary>
public sealed record Product(int Id, string Name, decimal Price, bool InStock);

/// <summary>Loads products. Workbench scenarios replace it with fakes.</summary>
public interface IProductService
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Model of <see cref="Forms.SignupForm"/>.</summary>
public sealed class SignupModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name must be at most 50 characters.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms.")]
    public bool AcceptTerms { get; set; }
}
