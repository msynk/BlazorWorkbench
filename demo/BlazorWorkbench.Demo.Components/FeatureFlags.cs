namespace BlazorWorkbench.Demo.Components;

/// <summary>Answers whether a feature is switched on. Apps back it with configuration or a flag service.</summary>
public interface IFeatureFlags
{
    bool IsEnabled(string feature);
}

/// <summary>Names of the features the demo components check.</summary>
public static class DemoFeatures
{
    /// <summary>Shows the Rewards link in <see cref="Layout.AppHeader"/>.</summary>
    public const string Rewards = "rewards";
}

/// <summary>A fixed set of enabled features.</summary>
public sealed class StaticFeatureFlags(params string[] enabled) : IFeatureFlags
{
    private readonly HashSet<string> _enabled = new(enabled, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every feature off.</summary>
    public static StaticFeatureFlags None { get; } = new();

    public bool IsEnabled(string feature) => _enabled.Contains(feature);

    public override string ToString() => _enabled.Count == 0 ? "No features" : string.Join(", ", _enabled.Order(StringComparer.Ordinal));
}
