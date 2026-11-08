namespace BlazorWorkbench.Demo.Components.Analytics;

/// <summary>Revenue of one period (a month) and an optional target.</summary>
public sealed record RevenuePoint(DateOnly Period, decimal Revenue, decimal? Target = null);

/// <summary>
/// A key metric returned by <c>GET api/dashboard/kpis</c>. <see cref="Change"/> is relative to the previous period
/// (0.08 means +8 %); <see cref="Trend"/> feeds the sparkline.
/// </summary>
public sealed record KpiSnapshot(string Label, decimal Value, string Format, double? Change, bool HigherIsBetter, IReadOnlyList<double> Trend);

/// <summary>An order returned by <c>GET api/orders/recent</c>.</summary>
public sealed record RecentOrder(string Number, string Customer, decimal Total, OrderStatus Status, DateTimeOffset PlacedAt);

/// <summary>Fulfillment state of a <see cref="RecentOrder"/>.</summary>
public enum OrderStatus { Pending, Paid, Shipped, Refunded }
