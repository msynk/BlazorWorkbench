using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench;

/// <summary>
/// Declares the axes of a scenario matrix (<see cref="ScenarioSetBuilder{TComponent}.Matrix"/>): the parameters to vary and the
/// values each one takes. Without <see cref="Pairwise"/>, every combination becomes a scenario.
/// </summary>
public sealed class MatrixBuilder<TComponent> where TComponent : IComponent
{
    /// <summary>Combinations above this number are rejected; split the matrix or use <see cref="Pairwise"/>.</summary>
    public const int MaxCombinations = 256;

    private readonly List<Axis> _axes = [];
    private bool _pairwise;

    /// <summary>
    /// Varies a parameter over <paramref name="values"/>. Without values, <c>bool</c> parameters take <c>false</c> and
    /// <c>true</c>, enums every declared value, and nullable versions of both also <c>null</c>.
    /// </summary>
    public MatrixBuilder<TComponent> Vary<TValue>(Expression<Func<TComponent, TValue>> parameter, params TValue[] values)
    {
        var name = Naming.GetMemberName(parameter);
        if (_axes.Any(a => a.Parameter == name))
            throw new ArgumentException($"The matrix already varies {name}.", nameof(parameter));

        object?[] domain = values.Length > 0 ? values.Cast<object?>().ToArray() : DefaultValues(typeof(TValue), name);
        if (domain.Length == 0)
            throw new ArgumentException($"Pass the values of {name} to vary.", nameof(values));
        _axes.Add(new Axis(name, domain.Distinct().ToArray(), null));
        return this;
    }

    /// <summary>Varies a parameter over values with display labels, for values whose text is not meaningful (colors, objects).</summary>
    /// <example><c>.Vary(b => b.Accent, [("none", null), ("purple", new HexColor("#a21caf"))])</c></example>
    public MatrixBuilder<TComponent> Vary<TValue>(Expression<Func<TComponent, TValue>> parameter, IEnumerable<(string Label, TValue Value)> labeledValues)
    {
        ArgumentNullException.ThrowIfNull(labeledValues);
        var values = labeledValues.ToArray();
        var name = Naming.GetMemberName(parameter);
        if (_axes.Any(a => a.Parameter == name))
            throw new ArgumentException($"The matrix already varies {name}.", nameof(parameter));
        if (values.Length == 0)
            throw new ArgumentException($"Pass the values of {name} to vary.", nameof(labeledValues));
        _axes.Add(new Axis(name, values.Select(v => (object?)v.Value).ToArray(), values.Select(v => v.Label).ToArray()));
        return this;
    }

    /// <summary>
    /// Keeps only enough combinations to cover every pair of values of any two parameters (all-pairs testing). Three
    /// parameters with four values each need 16 scenarios instead of 64, and most interaction defects involve two parameters.
    /// </summary>
    public MatrixBuilder<TComponent> Pairwise() { _pairwise = true; return this; }

    internal IReadOnlyList<IReadOnlyList<MatrixChoice>> Combinations()
    {
        if (_axes.Count == 0)
            throw new InvalidOperationException("A matrix needs at least one axis: call Vary(...).");

        var sizes = _axes.Select(a => a.Values.Length).ToArray();
        var indexes = _pairwise && _axes.Count > 2 ? PairwiseIndexes(sizes) : CartesianIndexes(sizes);
        if (indexes.Count > MaxCombinations)
            throw new InvalidOperationException(
                $"The matrix has {indexes.Count} combinations (more than {MaxCombinations}). Use Pairwise() or split it into smaller matrices.");

        return indexes.Select(row => (IReadOnlyList<MatrixChoice>)row.Select((value, axis) => Choice(_axes[axis], value)).ToArray()).ToArray();
    }

    private static MatrixChoice Choice(Axis axis, int index)
    {
        var value = axis.Values[index];
        var display = axis.Labels?[index] ?? Display(value);
        // Enum values read on their own ("Primary"); other values carry the parameter name ("Disabled: true").
        var label = value is Enum || axis.Labels is not null ? display : $"{axis.Parameter}: {display}";
        return new MatrixChoice(axis.Parameter, value, display, label);
    }

    private static string Display(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static object?[] DefaultValues(Type type, string name)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var core = underlying ?? type;
        object?[] values;
        if (core == typeof(bool))
            values = [false, true];
        else if (core.IsEnum && !core.IsDefined(typeof(FlagsAttribute), false))
            values = Enum.GetValues(core).Cast<object?>().ToArray();
        else
            throw new ArgumentException($"{name} is {Naming.FormatTypeName(type)}; pass the values to vary.", nameof(type));
        return underlying is not null ? [.. values, null] : values;
    }

    private static List<int[]> CartesianIndexes(int[] sizes)
    {
        var total = sizes.Aggregate(1L, (a, b) => a * b);
        if (total > MaxCombinations * 16L)
            throw new InvalidOperationException($"The matrix has {total} combinations. Use Pairwise() or split it into smaller matrices.");
        var result = new List<int[]>((int)total);
        var current = new int[sizes.Length];
        for (var n = 0; n < total; n++)
        {
            result.Add((int[])current.Clone());
            for (var i = sizes.Length - 1; i >= 0; i--)
            {
                if (++current[i] < sizes[i]) break;
                current[i] = 0;
            }
        }

        return result;
    }

    /// <summary>Greedy all-pairs selection: repeatedly takes the combination covering the most uncovered pairs. Deterministic.</summary>
    internal static List<int[]> PairwiseIndexes(int[] sizes)
    {
        var candidates = CartesianIndexes(sizes);
        var uncovered = new HashSet<(int, int, int, int)>();
        for (var a = 0; a < sizes.Length; a++)
        for (var b = a + 1; b < sizes.Length; b++)
        for (var va = 0; va < sizes[a]; va++)
        for (var vb = 0; vb < sizes[b]; vb++)
            uncovered.Add((a, va, b, vb));

        int Gain(int[] row)
        {
            var gain = 0;
            for (var a = 0; a < row.Length; a++)
            for (var b = a + 1; b < row.Length; b++)
                if (uncovered.Contains((a, row[a], b, row[b])))
                    gain++;
            return gain;
        }

        var result = new List<int[]>();
        while (uncovered.Count > 0)
        {
            int[]? best = null;
            var bestGain = 0;
            foreach (var candidate in candidates)
            {
                var gain = Gain(candidate);
                if (gain > bestGain)
                {
                    best = candidate;
                    bestGain = gain;
                }
            }

            if (best is null)
                break;
            result.Add(best);
            for (var a = 0; a < best.Length; a++)
            for (var b = a + 1; b < best.Length; b++)
                uncovered.Remove((a, best[a], b, best[b]));
        }

        // Keep the declaration order of values, which reads better in the explorer than selection order.
        result.Sort((x, y) =>
        {
            for (var i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                    return x[i].CompareTo(y[i]);
            }

            return 0;
        });
        return result;
    }

    private sealed record Axis(string Parameter, object?[] Values, string[]? Labels);
}

/// <summary>One parameter value of a matrix combination.</summary>
internal sealed record MatrixChoice(string Parameter, object? Value, string Display, string Label);
