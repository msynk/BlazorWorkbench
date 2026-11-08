namespace SampleComponents.Layout;

/// <summary>A link in <see cref="AppHeader"/>.</summary>
public sealed record NavLinkItem(string Text, string Href)
{
    public static IReadOnlyList<NavLinkItem> Defaults { get; } =
    [
        new("Shop", "/shop"),
        new("Trails", "/trails"),
        new("Orders", "/orders"),
        new("Help", "/help"),
    ];
}
