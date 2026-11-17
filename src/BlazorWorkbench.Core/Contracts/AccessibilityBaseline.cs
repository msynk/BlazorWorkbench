using System.Text.Json;

namespace BlazorWorkbench.Contracts;

/// <summary>
/// Known accessibility violations of a scenario, approved with <c>workbench approve --accessibility</c> (Chromatic's
/// accessibility baselines). A run then fails only on violations that are not in the baseline, so a library that cannot
/// reach zero findings at once can stop new ones from creeping in, and fix the old ones over time. Baselines are stored in
/// <c>{baselines}/accessibility/{scenario-id}.json</c>; axe-core results depend on the DOM, not on the platform, so one file
/// serves every operating system and hosting model.
/// </summary>
public sealed class AccessibilityBaseline
{
    public const string FolderName = "accessibility";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The scenario.</summary>
    public required string ScenarioId { get; init; }

    /// <summary>One entry per violating element: the axe rule and the element's selector.</summary>
    public IReadOnlyList<KnownViolation> Violations { get; init; } = [];

    /// <summary>An approved violation: an axe-core rule id and the selector of the element it was found on.</summary>
    public sealed record KnownViolation(string Rule, string Target);

    /// <summary>The file of <paramref name="scenarioId"/>'s baseline in <paramref name="baselineDirectory"/>.</summary>
    public static string PathFor(string baselineDirectory, string scenarioId) =>
        Path.Combine(Path.GetFullPath(baselineDirectory), FolderName, Sanitize(scenarioId) + ".json");

    // The same characters on every operating system, so a committed baseline has one file name everywhere.
    private static readonly char[] s_invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static string Sanitize(string value) => string.Concat(value.Select(c => c < 32 || s_invalid.Contains(c) ? '_' : c));

    /// <summary>Reads the baseline of a scenario, or null when there is none (or it cannot be read).</summary>
    public static AccessibilityBaseline? Load(string baselineDirectory, string scenarioId)
    {
        var path = PathFor(baselineDirectory, scenarioId);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<AccessibilityBaseline>(File.ReadAllText(path), s_json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the violations of <paramref name="findings"/> (not the <i>needs review</i> items) as the scenario's baseline,
    /// or deletes the baseline when there are none. Returns the number of known violations written.
    /// </summary>
    public static int Save(string baselineDirectory, string scenarioId, IEnumerable<AccessibilityFinding> findings)
    {
        var violations = findings.Where(f => !f.NeedsReview)
            .SelectMany(f => f.Targets.Select(t => new KnownViolation(f.RuleId, t)))
            .Distinct()
            .OrderBy(v => v.Rule, StringComparer.Ordinal).ThenBy(v => v.Target, StringComparer.Ordinal)
            .ToArray();
        var path = PathFor(baselineDirectory, scenarioId);
        if (violations.Length == 0)
        {
            if (File.Exists(path))
                File.Delete(path);
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new AccessibilityBaseline { ScenarioId = scenarioId, Violations = violations }, s_json));
        return violations.Length;
    }

    /// <summary>
    /// Marks the findings that <paramref name="baseline"/> covers as known (not blocking, <see cref="AccessibilityFinding.Baselined"/>).
    /// Returns how many blocking findings became known and how many known violations are gone.
    /// </summary>
    public static (int Known, int Fixed) Apply(AccessibilityBaseline? baseline, List<AccessibilityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (baseline is null)
            return (0, 0);
        var known = 0;
        for (var n = 0; n < findings.Count; n++)
        {
            if (findings[n] is { NeedsReview: false } finding && baseline.Covers(finding))
            {
                known += finding.Blocking ? 1 : 0;
                findings[n] = finding with { Blocking = false, Baselined = true };
            }
        }

        return (known, baseline.Fixed(findings).Count);
    }

    /// <summary>True when every element of <paramref name="finding"/> is a known violation of the same rule.</summary>
    public bool Covers(AccessibilityFinding finding) =>
        finding.Targets.Count > 0 && finding.Targets.All(t => Violations.Contains(new KnownViolation(finding.RuleId, t)));

    /// <summary>Known violations that <paramref name="findings"/> no longer contain: fixed since the baseline was approved.</summary>
    public IReadOnlyList<KnownViolation> Fixed(IEnumerable<AccessibilityFinding> findings)
    {
        var current = findings.Where(f => !f.NeedsReview).SelectMany(f => f.Targets.Select(t => new KnownViolation(f.RuleId, t))).ToHashSet();
        return Violations.Where(v => !current.Contains(v)).ToArray();
    }
}
