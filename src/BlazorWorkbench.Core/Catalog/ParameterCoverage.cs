using System.Reflection;
using System.Text.Json.Serialization;

namespace BlazorWorkbench.Catalog;

/// <summary>How the coverage of a parameter is measured.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParameterCoverageKind>))]
public enum ParameterCoverageKind
{
    /// <summary>The parameter has a finite value domain (booleans and non-flags enums); each value is covered or missing.</summary>
    Values,

    /// <summary>The parameter has no finite value domain; it counts as covered when at least one scenario sets it.</summary>
    Presence,
}

/// <summary>
/// Which values of a component parameter the hand-written scenarios of the component exercise, for example
/// <c>Variant: Primary ✓ Secondary ✓ Danger ✗</c>.
/// </summary>
/// <param name="Parameter">The parameter name.</param>
/// <param name="TypeName">The parameter type formatted as C# (<c>bool?</c>, <c>ButtonVariant</c>).</param>
/// <param name="Kind">Whether <paramref name="Covered"/> and <paramref name="Missing"/> list values or only presence is measured.</param>
/// <param name="Covered">
/// Display strings (enum member names, <c>true</c>/<c>false</c>, <c>null</c>) of the domain values at least one scenario renders,
/// in domain order. Empty for <see cref="ParameterCoverageKind.Presence"/>.
/// </param>
/// <param name="Missing">Display strings of the domain values no scenario renders, in domain order. Empty for <see cref="ParameterCoverageKind.Presence"/>.</param>
/// <param name="ScenariosSettingIt">The number of scenarios that assign the parameter (any value kind); defaults are not counted.</param>
public sealed record ParameterCoverage(
    string Parameter,
    string TypeName,
    ParameterCoverageKind Kind,
    IReadOnlyList<string> Covered,
    IReadOnlyList<string> Missing,
    int ScenariosSettingIt)
{
    /// <summary>True when no value of the domain is missing (always true for <see cref="ParameterCoverageKind.Presence"/>).</summary>
    public bool IsComplete => Missing.Count == 0;
}

/// <summary>Computes <see cref="ParameterCoverage"/> for the parameters of a catalog component.</summary>
public static class ParameterCoverageAnalyzer
{
    private const string NullDisplay = "null";

    // Stands for a value that matches no domain entry (an unknown value or a value of an unexpected type).
    private static readonly object s_noMatch = new();

    /// <summary>
    /// Returns the coverage of the value, child content, render fragment and template parameters of
    /// <paramref name="component"/> (obsolete parameters are skipped), in the order of
    /// <see cref="ComponentMetadata.Parameters"/>. Only hand-written scenarios count; a component with generated scenarios only
    /// yields an empty list.
    /// </summary>
    /// <remarks>
    /// Booleans have the domain <c>true, false</c> (plus <c>null</c> when nullable); non-flags enums every declared member in
    /// declaration order (plus <c>null</c> when nullable). Other types are measured by presence. A literal assignment covers its
    /// value; factory and text assignments count as setting the parameter without a known value; a scenario that does not
    /// assign the parameter covers the component default (the value read from a fresh instance, or <c>default(T)</c>).
    /// </remarks>
    /// <param name="component">The catalog component.</param>
    public static IReadOnlyList<ParameterCoverage> Analyze(ComponentEntry component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var scenarios = component.Scenarios.Where(s => !s.IsGenerated).ToArray();
        if (scenarios.Length == 0)
            return [];

        var metadata = component.Metadata;
        var result = new List<ParameterCoverage>();
        foreach (var parameter in metadata.Parameters)
        {
            if (parameter.IsObsolete || parameter.Kind is not (ParameterKind.Value or ParameterKind.ChildContent or ParameterKind.RenderFragment or ParameterKind.Template))
                continue;

            var domain = parameter.Kind == ParameterKind.Value ? ValueDomain.For(parameter.Type) : null;
            var covered = new HashSet<string>(StringComparer.Ordinal);
            var settingIt = 0;
            foreach (var scenario in scenarios)
            {
                var assignment = FindAssignment(scenario, metadata, parameter);
                if (assignment is not null)
                    settingIt++;

                if (domain is null)
                    continue;
                if (assignment is null)
                    domain.Mark(DefaultOf(parameter), covered);
                else if (assignment.Kind == ParameterValueKind.Literal)
                    domain.Mark(assignment.Value, covered);
            }

            result.Add(domain is null
                ? new ParameterCoverage(parameter.Name, parameter.TypeName, ParameterCoverageKind.Presence, [], [], settingIt)
                : new ParameterCoverage(parameter.Name, parameter.TypeName, ParameterCoverageKind.Values,
                    domain.Names.Where(covered.Contains).ToArray(),
                    domain.Names.Where(n => !covered.Contains(n)).ToArray(),
                    settingIt));
        }

        return result;
    }

    /// <summary>
    /// The share of covered items: every value of a <see cref="ParameterCoverageKind.Values"/> entry is one item, and every
    /// <see cref="ParameterCoverageKind.Presence"/> entry is one item (covered when a scenario sets it). Returns 1 when there
    /// are no items.
    /// </summary>
    /// <param name="coverage">Coverage entries, typically from <see cref="Analyze"/>.</param>
    public static double Ratio(IEnumerable<ParameterCoverage> coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        int total = 0, covered = 0;
        foreach (var entry in coverage)
        {
            if (entry.Kind == ParameterCoverageKind.Values)
            {
                total += entry.Covered.Count + entry.Missing.Count;
                covered += entry.Covered.Count;
            }
            else
            {
                total++;
                if (entry.ScenariosSettingIt > 0)
                    covered++;
            }
        }

        return total == 0 ? 1d : (double)covered / total;
    }

    // Matches assignments the way validation does (exact name first, then case-insensitive); the last assignment wins.
    private static ParameterAssignment? FindAssignment(ScenarioDefinition scenario, ComponentMetadata metadata, ParameterDescriptor parameter)
    {
        ParameterAssignment? found = null;
        foreach (var assignment in scenario.Parameters)
        {
            if (ReferenceEquals(metadata.FindParameter(assignment.Name), parameter))
                found = assignment;
        }

        return found;
    }

    private static object? DefaultOf(ParameterDescriptor parameter)
    {
        if (parameter.HasDefaultValue)
            return parameter.DefaultValue;

        var type = parameter.Type;
        if (Nullable.GetUnderlyingType(type) is not null || !type.IsValueType)
            return null;
        if (type == typeof(bool))
            return false;
        return type.IsEnum ? Enum.ToObject(type, 0) : null;
    }

    /// <summary>The finite value domain of a boolean or (non-flags) enum parameter.</summary>
    private sealed class ValueDomain
    {
        private readonly Type _type;
        private readonly (string Name, object? Value)[] _entries;

        private ValueDomain(Type type, (string Name, object? Value)[] entries)
        {
            _type = type;
            _entries = entries;
            Names = entries.Select(e => e.Name).ToArray();
        }

        public IReadOnlyList<string> Names { get; }

        public static ValueDomain? For(Type parameterType)
        {
            var underlying = Nullable.GetUnderlyingType(parameterType);
            var type = underlying ?? parameterType;
            List<(string Name, object? Value)> entries;
            if (type == typeof(bool))
            {
                entries = [("true", true), ("false", false)];
            }
            else if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
            {
                entries = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .OrderBy(f => f.MetadataToken) // Declaration order.
                    .Select(f => (Name: f.Name, Value: f.GetValue(null)))
                    .ToList();
            }
            else
            {
                return null;
            }

            if (underlying is not null)
                entries.Add((NullDisplay, null));
            return new ValueDomain(type, [.. entries]);
        }

        /// <summary>Adds the names of the entries equal to <paramref name="value"/> (enum aliases share a value).</summary>
        public void Mark(object? value, HashSet<string> covered)
        {
            var normalized = Normalize(value);
            foreach (var (name, entryValue) in _entries)
            {
                if (Equals(entryValue, normalized))
                    covered.Add(name);
            }
        }

        private object? Normalize(object? value)
        {
            if (value is null)
                return null;
            if (value.GetType() == _type)
                return value;
            if (!_type.IsEnum)
                return s_noMatch;

            if (value is string name)
                return _entries.FirstOrDefault(e => e.Value is not null && string.Equals(e.Name, name, StringComparison.Ordinal)).Value ?? s_noMatch;

            return value is sbyte or byte or short or ushort or int or uint or long or ulong ? Enum.ToObject(_type, value) : s_noMatch;
        }
    }
}
