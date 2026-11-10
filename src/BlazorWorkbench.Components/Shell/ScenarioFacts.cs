using System.Globalization;
using System.Net;
using System.Text;

namespace BlazorWorkbench.Components.Shell;

/// <summary>Whether a <see cref="ScenarioFact"/> describes the simulated world or what the scenario checks.</summary>
public enum ScenarioFactGroup
{
    /// <summary>Something the scenario controls around the component: the backend, the user, the clock, services and so on.</summary>
    Simulates,

    /// <summary>Something the scenario checks beyond rendering: interaction tests, screenshots, accessibility and budgets.</summary>
    Verifies,
}

/// <summary>One fact about a scenario's setup, shown as a chip under the scenario title and on documentation pages.</summary>
/// <param name="Group">Simulated environment or verification.</param>
/// <param name="Kind">Short category label, such as <c>HTTP</c>, <c>User</c> or <c>Clock</c>.</param>
/// <param name="Text">What is simulated or checked.</param>
/// <param name="Panel">Key of the Workbench panel that shows or controls it (<c>environment</c>, <c>interactions</c>, ...), or null.</param>
public sealed record ScenarioFact(ScenarioFactGroup Group, string Kind, string Text, string? Panel);

/// <summary>
/// Summarizes what a scenario declares beyond parameters, so the simulated environment is visible without opening a panel:
/// fake HTTP rules, the user, the clock, replaced services, cascading values, culture, theme, viewport, seed, and the checks
/// the scenario adds (interactions, screenshots, accessibility rules, budgets).
/// </summary>
public static class ScenarioFacts
{
    /// <summary>HTTP rules listed individually; the rest are summarized as "+N more".</summary>
    public const int MaxHttpRules = 3;

    /// <summary>Service types listed by name; the rest are summarized as "+N".</summary>
    public const int MaxServices = 2;

    public static IReadOnlyList<ScenarioFact> Describe(ScenarioDefinition scenario, WorkbenchOptions options)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);
        var facts = new List<ScenarioFact>();
        var env = scenario.Environment;

        void Simulates(string kind, string text, string? panel = "environment") => facts.Add(new(ScenarioFactGroup.Simulates, kind, text, panel));
        void Verifies(string kind, string text, string? panel) => facts.Add(new(ScenarioFactGroup.Verifies, kind, text, panel));

        // The backend.
        if (env.Offline)
        {
            Simulates("Network", "offline, every request fails");
        }
        else if (env.Http is { } http)
        {
            var rules = EffectiveRules(http.Rules);
            foreach (var rule in rules.Take(MaxHttpRules))
                Simulates("HTTP", $"{rule.Method} {rule.Pattern} → {Response(rule)}");
            if (rules.Count > MaxHttpRules)
                Simulates("HTTP", $"+{rules.Count - MaxHttpRules} more rules");
            if (http.Latency > TimeSpan.Zero)
                Simulates("HTTP", $"{Milliseconds(http.Latency)} latency");
        }

        // Who, when and where.
        if (env.User is { } user)
            Simulates("User", user.IsAuthenticated ? DescribeUser(user) : "signed out");
        if (env.Clock is { } clock)
            Simulates("Clock", "frozen at " + clock.ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + Offset(clock));

        var services = scenario.ServiceTypes;
        if (services.Any(IsDeviceService))
            Simulates("Device", "simulated .NET MAUI Essentials");
        var replaced = services.Where(t => !IsDeviceService(t)).Select(t => Naming.FormatTypeName(t)).ToList();
        if (replaced.Count > 0)
            Simulates("Services", string.Join(", ", replaced.Take(MaxServices)) + (replaced.Count > MaxServices ? $" +{replaced.Count - MaxServices}" : ""));

        foreach (var cascade in scenario.CascadingValues)
            Simulates("Cascade", Naming.FormatTypeName(cascade.ValueType) + (cascade.Name is null ? "" : $" '{cascade.Name}'"));

        if (env.Culture is { } culture)
            Simulates("Culture", culture + (env.Direction == TextDirection.Auto ? "" : ", " + env.Direction.ToString().ToLowerInvariant()), panel: null);
        else if (env.Direction != TextDirection.Auto)
            Simulates("Direction", env.Direction.ToString().ToLowerInvariant(), panel: null);
        if (env.Theme is { } theme)
            Simulates("Theme", theme, panel: null);
        if (env.Viewport is { } viewport)
            Simulates("Viewport", options.FindViewport(viewport) is { } preset ? $"{preset.Name} ({preset.Width}×{preset.Height})" : viewport, panel: null);
        if (env.Seed is { } seed)
            Simulates("Random", "seed " + seed.ToString(CultureInfo.InvariantCulture));

        // What the scenario checks beyond rendering without errors.
        if (scenario.SkipReason is not null)
            Verifies("Tests", "excluded from test runs", panel: null);
        if (scenario.Interactions.Count > 0)
            Verifies("Tests", scenario.Interactions.Count == 1 ? "1 interaction test" : $"{scenario.Interactions.Count} interaction tests", "interactions");

        var visual = scenario.Visual;
        if (visual.Skip)
            Verifies("Visual", "no screenshot", "visual");
        else if (visual.Themes.Count > 0 || visual.Viewports.Count > 0 || visual.FullPage)
            Verifies("Visual", "screenshots: " + string.Join(", ", visual.Themes.Concat(visual.Viewports).Concat(visual.FullPage ? ["full page"] : [])), "visual");

        var a11y = scenario.Accessibility;
        if (a11y.Skip)
            Verifies("Accessibility", "excluded from automated scans", "accessibility");
        foreach (var rule in a11y.DisabledRules)
            Verifies("Accessibility", $"rule {rule} disabled", "accessibility");

        var budget = scenario.Budget;
        if (budget.ReadyWithin is { } ready)
            Verifies("Budget", $"ready within {Milliseconds(ready)}", panel: null);
        if (budget.InteractionWithin is { } interaction)
            Verifies("Budget", $"interactions within {Milliseconds(interaction)}", panel: null);
        if (budget.MaxDomUpdates is { } updates)
            Verifies("Budget", $"at most {updates.ToString(CultureInfo.InvariantCulture)} DOM updates", panel: null);

        return facts;
    }

    /// <summary>
    /// Rules in evaluation order, without rules that can never match: a rule without <c>Times(n)</c> answers every request
    /// to its method and pattern, so later rules for the same method and pattern (usually shared ones from <c>Setup</c>) are hidden.
    /// </summary>
    internal static IReadOnlyList<HttpRule> EffectiveRules(IEnumerable<HttpRule> rules)
    {
        var exhaustive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<HttpRule>();
        foreach (var rule in rules)
        {
            var key = rule.Method + " " + rule.Pattern;
            if (exhaustive.Contains(key) || exhaustive.Contains("* " + rule.Pattern))
                continue;
            result.Add(rule);
            if (rule.Times is null)
                exhaustive.Add(key);
        }

        return result;
    }

    internal static string Response(HttpRule rule)
    {
        var text = rule.Handler is not null ? "custom handler"
            : rule.Fault == HttpFault.NetworkError ? "network error"
            : rule.Fault == HttpFault.Timeout ? "timeout"
            : $"{(int)rule.Status} {ReasonPhrase(rule.Status)}".TrimEnd();
        if (rule.Latency is { } latency && latency > TimeSpan.Zero)
            text += $" after {Milliseconds(latency)}";
        if (rule.Times is { } times)
            text += times == 1 ? ", first request only" : $", first {times.ToString(CultureInfo.InvariantCulture)} requests";
        if (rule.Gate is { } gate)
            text += $", held at gate '{gate}'";
        return text;
    }

    private static string DescribeUser(SimulatedUser user)
    {
        var parts = new List<string> { user.Name ?? "unnamed user" };
        parts.AddRange(user.Roles);
        parts.AddRange(user.Claims.Select(c => $"{c.Key}={c.Value}"));
        return string.Join(" · ", parts);
    }

    private static bool IsDeviceService(Type type) => type.Namespace?.StartsWith("Microsoft.Maui.", StringComparison.Ordinal) == true;

    private static string Offset(DateTimeOffset clock) =>
        clock.Offset == TimeSpan.Zero ? " UTC" : " " + clock.ToString("zzz", CultureInfo.InvariantCulture);

    private static string Milliseconds(TimeSpan value) =>
        value.TotalMilliseconds >= 1000 && value.TotalMilliseconds % 100 == 0
            ? value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
            : value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";

    /// <summary>"PaymentRequired" becomes "Payment Required"; "OK" stays "OK".</summary>
    private static string ReasonPhrase(HttpStatusCode status)
    {
        var name = status.ToString();
        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return "";
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                sb.Append(' ');
            sb.Append(name[i]);
        }

        return sb.ToString();
    }
}
