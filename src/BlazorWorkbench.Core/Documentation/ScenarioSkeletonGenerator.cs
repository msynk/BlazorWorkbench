using System.Text;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Documentation;

/// <summary>A suggested scenario derived from parameter metadata (boundary and state coverage).</summary>
public sealed record ScenarioSuggestion(string Name, string Reason, IReadOnlyList<string> Calls);

/// <summary>
/// Suggests missing boundary cases and generates a <see cref="ScenarioSet{TComponent}"/> skeleton from component metadata.
/// Output is a starting point for developers (or coding agents) and must be reviewed; it is validated like any scenario.
/// </summary>
public static class ScenarioSkeletonGenerator
{
    public static IReadOnlyList<ScenarioSuggestion> Suggest(ComponentDto component)
    {
        var suggestions = new List<ScenarioSuggestion>();
        var required = component.Parameters.Where(p => p.IsRequired && p.Kind == ParameterKind.Value).ToList();
        var requiredCalls = required.Select(p => SampleCall(p, "typical")).OfType<string>().ToList();
        if (component.Parameters.Any(p => p.Kind == ParameterKind.ChildContent))
            requiredCalls.Add(".ChildContent(\"Content\")");

        suggestions.Add(new("Default", "Typical usage with required parameters.", requiredCalls));

        foreach (var p in component.Parameters.Where(p => p.Kind == ParameterKind.Value && !p.IsObsolete))
        {
            switch (p.Editor)
            {
                case EditorKind.Boolean when p.DefaultValueDisplay != "true":
                    suggestions.Add(new($"{p.Name} enabled", $"{p.Name} toggles behavior.", [.. requiredCalls, $".Parameter(c => c.{p.Name}, true)"]));
                    break;
                case EditorKind.Enum:
                    foreach (var value in p.EnumValues.Where(v => !string.Equals($"{TypeOnly(p.TypeName)}.{v}", p.DefaultValueDisplay, StringComparison.Ordinal)).Take(6))
                        suggestions.Add(new($"{p.Name} {value}", $"Each {TypeOnly(p.TypeName)} value can render differently.", [.. requiredCalls, $".Parameter(c => c.{p.Name}, {TypeOnly(p.TypeName)}.{value})"]));
                    break;
                case EditorKind.Text or EditorKind.MultilineText:
                    suggestions.Add(new($"Long {p.Name}", "Long text exposes wrapping and overflow problems.",
                        [.. requiredCalls.Where(c => !c.Contains($"c.{p.Name},", StringComparison.Ordinal)), $".Parameter(c => c.{p.Name}, \"{new string('W', 12)} {string.Join(' ', Enumerable.Repeat("long text", 12))}\")"]));
                    if (p.IsNullable || !p.IsRequired)
                        suggestions.Add(new($"Empty {p.Name}", "Empty strings are a common boundary.", [.. requiredCalls.Where(c => !c.Contains($"c.{p.Name},", StringComparison.Ordinal)), $".Parameter(c => c.{p.Name}, \"\")"]));
                    break;
                case EditorKind.Integer or EditorKind.Decimal:
                    suggestions.Add(new($"{p.Name} zero", "Zero and negative values are common boundaries.", [.. requiredCalls, $".Parameter(c => c.{p.Name}, 0)"]));
                    suggestions.Add(new($"{p.Name} large", "Large numbers expose formatting and overflow problems.", [.. requiredCalls, $".Parameter(c => c.{p.Name}, {(p.TypeName.StartsWith("int", StringComparison.Ordinal) ? "int.MaxValue" : "1_000_000")})"]));
                    break;
            }

            if (p.IsNullable && p.Editor is not (EditorKind.Text or EditorKind.Unsupported) && !p.IsRequired)
                suggestions.Add(new($"{p.Name} null", "Null must be handled explicitly.", [.. requiredCalls, $".Parameter(c => c.{p.Name}, null)"]));
        }

        if (component.Dependencies.Any(d => d is "HttpClient" or "IHttpClientFactory"))
        {
            suggestions.Add(new("Loading", "Async loading state held at a gate.", [.. requiredCalls, ".Http(h => h.Any(\"/api/**\").Hold(\"load\"))"]));
            suggestions.Add(new("Server error", "Failure state.", [.. requiredCalls, ".Http(h => h.Any(\"/api/**\").Fail())"]));
            suggestions.Add(new("Offline", "Network failure.", [.. requiredCalls, ".Offline()"]));
        }

        if (component.Dependencies.Contains("AuthenticationStateProvider") || component.Parameters.Any(p => p.TypeName.Contains("AuthenticationState", StringComparison.Ordinal)))
        {
            suggestions.Add(new("Anonymous", "Unauthenticated users.", [.. requiredCalls, ".Anonymous()"]));
            suggestions.Add(new("Signed in", "Authenticated users.", [.. requiredCalls, ".User(\"Test User\")"]));
        }

        suggestions.Add(new("Right to left", "Layouts must mirror for RTL cultures.", [.. requiredCalls, ".Culture(\"ar-SA\")"]));

        var existing = component.Scenarios.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return suggestions.Where(s => !existing.Contains(s.Name)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    public static string GenerateSet(ComponentDto component, IReadOnlyList<ScenarioSuggestion>? suggestions = null)
    {
        suggestions ??= Suggest(component);
        var name = component.Name;
        var className = Naming.ToKebabCase(component.Title.Split('/')[^1]).Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(new StringBuilder(), (sb, part) => sb.Append(char.ToUpperInvariant(part[0])).Append(part[1..])).Append("Scenarios").ToString();

        var sb = new StringBuilder();
        sb.AppendLine("using BlazorWorkbench;");
        if (component.Namespace is { } ns)
            sb.Append("using ").Append(ns).AppendLine(";");
        sb.AppendLine();
        sb.Append("public sealed class ").Append(className).Append(" : ScenarioSet<").Append(name).AppendLine(">");
        sb.AppendLine("{");
        sb.Append("    protected override void Configure(ScenarioSetBuilder<").Append(name).AppendLine("> set)");
        sb.AppendLine("    {");
        sb.Append("        set.Title(\"").Append(component.Title).AppendLine("\");");
        foreach (var suggestion in suggestions)
        {
            sb.AppendLine();
            sb.Append("        // ").AppendLine(suggestion.Reason);
            sb.Append("        set.Add(\"").Append(suggestion.Name).Append("\", s => s");
            if (suggestion.Calls.Count == 0)
            {
                sb.AppendLine(");");
                continue;
            }

            foreach (var call in suggestion.Calls)
                sb.AppendLine().Append("            ").Append(call);
            sb.AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string? SampleCall(ParameterDescriptor p, string flavor) => p.Editor switch
    {
        EditorKind.Text or EditorKind.MultilineText => $".Parameter(c => c.{p.Name}, \"{Humanize(p.Name)}\")",
        EditorKind.Boolean => $".Parameter(c => c.{p.Name}, true)",
        EditorKind.Integer => $".Parameter(c => c.{p.Name}, 1)",
        EditorKind.Decimal => $".Parameter(c => c.{p.Name}, 1.5)",
        EditorKind.Enum when p.EnumValues.Count > 0 => $".Parameter(c => c.{p.Name}, {TypeOnly(p.TypeName)}.{p.EnumValues[0]})",
        EditorKind.DateTime => $".Parameter(c => c.{p.Name}, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero))",
        EditorKind.Date => $".Parameter(c => c.{p.Name}, new DateOnly(2026, 1, 15))",
        _ => $"// TODO: supply {p.Name} ({p.TypeName}) with .ParameterFrom(c => c.{p.Name}, ctx => ...) or a fixture ({flavor})",
    };

    private static string TypeOnly(string typeName) => typeName.TrimEnd('?');

    private static string Humanize(string name)
    {
        var words = Naming.ToKebabCase(name).Replace('-', ' ');
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
