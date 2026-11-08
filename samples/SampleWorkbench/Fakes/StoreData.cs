using SampleComponents.Analytics;
using SampleComponents.Commerce;

namespace SampleWorkbench.Fakes;

/// <summary>
/// Deterministic data for the store scenarios. Everything is fixed (or derived from the scenario's seeded random source),
/// so previews, screenshots and tests look the same on every machine and every run.
/// </summary>
public static class StoreData
{
    /// <summary>The instant most store scenarios freeze the clock at: Monday 15 June 2026, 09:30 UTC.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    public static IReadOnlyList<CartLine> Cart { get; } =
    [
        new("TNT-2P", "Summit 2P tent", 1, 349.00m),
        new("HDL-400", "Headlamp 400", 2, 45.00m),
    ];

    public static IReadOnlyList<CartLine> SmallCart { get; } = [new("MUG-INS", "Insulated mug", 2, 29.00m)];

    public static OrderConfirmation Confirmation { get; } = new("TH-10432", new DateTimeOffset(2026, 6, 19, 18, 0, 0, TimeSpan.Zero));

    /// <summary>The product catalog behind <c>GET api/products/search</c>.</summary>
    public static IReadOnlyList<ProductHit> Catalog { get; } =
    [
        new("TNT-2P", "Summit 2P tent", "Tents", 349.00m, 4.7, true),
        new("TNT-4P", "Ridgeline 4P family tent", "Tents", 529.00m, 4.5, true),
        new("TNT-BVY", "Ultralight bivy", "Tents", 189.00m, 4.2, false),
        new("BPK-28", "Trail 28 L daypack", "Backpacks", 89.00m, 4.6, true),
        new("BPK-65", "Expedition 65 L pack", "Backpacks", 259.00m, 4.8, true),
        new("BPK-VST", "Hydration vest", "Backpacks", 119.00m, 4.4, true),
        new("JKT-SHL", "Alpine shell jacket", "Jackets", 299.00m, 4.9, true),
        new("JKT-DWN", "Packable down jacket", "Jackets", 199.00m, 4.7, true),
        new("JKT-RN", "Rain jacket", "Jackets", 129.00m, 4.3, true),
        new("JKT-FLC", "Fleece midlayer", "Jackets", 79.00m, 4.5, false),
        new("FTW-TRS", "Trail running shoes", "Footwear", 139.00m, 4.6, true),
        new("FTW-BTS", "Waterproof hiking boots", "Footwear", 189.00m, 4.8, true),
        new("FTW-APR", "Approach shoes", "Footwear", 159.00m, 4.1, false),
        new("FTW-SND", "Camp sandals", "Footwear", 49.00m, 4.0, true),
        new("CK-POT", "Titanium cook pot", "Cooking", 59.00m, 4.6, true),
        new("CK-STV", "Canister stove", "Cooking", 69.00m, 4.7, true),
        new("MUG-INS", "Insulated mug", "Cooking", 29.00m, 4.4, true),
        new("HDL-400", "Headlamp 400", "Lighting", 45.00m, 4.5, true),
        new("HDL-900", "Rechargeable headlamp 900", "Lighting", 89.00m, 4.8, true),
        new("LGT-SLR", "Solar lantern", "Lighting", 39.00m, 4.2, true),
        new("SLP-DWN", "Down sleeping bag −5 °C", "Sleeping", 279.00m, 4.8, true),
        new("SLP-SUM", "Summer sleeping bag", "Sleeping", 99.00m, 4.3, true),
        new("SLP-PAD", "Insulated sleeping pad", "Sleeping", 129.00m, 4.6, true),
        new("SLP-PLW", "Camp pillow", "Sleeping", 35.00m, 4.1, true),
    ];

    public static IReadOnlyList<KpiSnapshot> Kpis { get; } =
    [
        new("Revenue", 47_600m, "C0", 0.08, true, [31.2, 33.4, 36.1, 41.8, 38.9, 44.1, 47.6]),
        new("Orders", 1_284m, "N0", 0.042, true, [1010, 1090, 1150, 1230, 1180, 1240, 1284]),
        new("Conversion rate", 0.031m, "P1", -0.052, true, [3.4, 3.3, 3.5, 3.2, 3.3, 3.2, 3.1]),
        new("Refund rate", 0.018m, "P1", -0.12, false, [2.4, 2.2, 2.3, 2.0, 2.1, 1.9, 1.8]),
    ];

    /// <summary>Revenue of the twelve months up to the frozen <see cref="Now"/>, with a target.</summary>
    public static IReadOnlyList<RevenuePoint> Revenue { get; } = Months(
        31_200, 28_900, 33_400, 36_100, 41_800, 52_300, 29_800, 30_500, 35_200, 38_900, 44_100, 47_600);

    public static IReadOnlyList<RevenuePoint> RevenueLastSixMonths { get; } = [.. Revenue.Skip(6)];

    public static IReadOnlyList<RecentOrder> RecentOrders { get; } =
    [
        new("TH-10431", "Grace Hopper", 349.00m, OrderStatus.Paid, Now.AddMinutes(-12)),
        new("TH-10430", "Linus Torvalds", 89.00m, OrderStatus.Shipped, Now.AddHours(-2)),
        new("TH-10429", "Margaret Hamilton", 528.50m, OrderStatus.Paid, Now.AddHours(-5)),
        new("TH-10428", "Alan Turing", 139.00m, OrderStatus.Refunded, Now.AddHours(-7)),
        new("TH-10427", "Radia Perlman", 59.00m, OrderStatus.Pending, Now.AddHours(-8)),
    ];

    /// <summary>Monthly revenue drawn from the scenario's seeded random source: different per seed, identical per run.</summary>
    public static IReadOnlyList<RevenuePoint> RandomRevenue(Random random, int months) =>
        [.. Enumerable.Range(0, months).Select(i => new RevenuePoint(
            DateOnly.FromDateTime(Now.UtcDateTime).AddMonths(i - months + 1),
            random.Next(18, 60) * 1_000m,
            40_000m))];

    private static RevenuePoint[] Months(params int[] values)
    {
        var first = DateOnly.FromDateTime(Now.UtcDateTime).AddMonths(-values.Length + 1);
        return [.. values.Select((v, i) => new RevenuePoint(new DateOnly(first.Year, first.Month, 1).AddMonths(i), v, 40_000m))];
    }
}
