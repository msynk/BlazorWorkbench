using BlazorWorkbench;
using BlazorWorkbench.Demo.Components;

namespace BlazorWorkbench.Demo.Fakes;

/// <summary>In-memory product service. Optionally waits at a gate so loading states can be inspected and tested.</summary>
public sealed class FakeProductService(IReadOnlyList<Product> products, ScenarioGates? gates = null, string? gate = null) : IProductService
{
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        if (gates is not null && gate is not null)
            await gates.WaitAsync(gate, cancellationToken);
        return products;
    }
}

public static class DemoData
{
    public static IReadOnlyList<Product> Products { get; } =
    [
        new(1, "Mechanical keyboard", 129.00m, true),
        new(2, "Wireless mouse", 49.50m, true),
        new(3, "4K monitor", 399.99m, false),
    ];

    private static readonly string[] Names = ["Ada", "Grace", "Linus", "Margaret", "Alan", "Barbara", "Dennis", "Frances", "Ken", "Radia"];
    private static readonly string[] Countries = ["IR", "US", "DE", "JP", "BR", "IN", "FR", "CA"];

    /// <summary>Deterministic people generated from the scenario's seeded random source.</summary>
    public static IReadOnlyList<Person> People(Random random, int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var name = Names[random.Next(Names.Length)];
            return new Person($"{name} {(char)('A' + random.Next(26))}.", $"{name.ToLowerInvariant()}{i}@example.com", random.Next(18, 80), Countries[random.Next(Countries.Length)]);
        }).ToArray();
}
