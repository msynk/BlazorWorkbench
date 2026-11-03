using System.Globalization;
using System.Text;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Documentation;

/// <summary>Turns a scenario plus inspector overrides into C# scenario code that can be pasted into a <see cref="ScenarioSet{TComponent}"/>.</summary>
public static class ScenarioExporter
{
    public static string ToCSharp(ScenarioDefinition scenario, ComponentMetadata metadata, IReadOnlyDictionary<string, ArgOverride> overrides, string? name = null)
    {
        var sb = new StringBuilder();
        sb.Append("set.Add(").Append(Literal(name ?? $"{scenario.Name} (edited)")).Append(", s => s");
        var lines = new List<string>();

        var names = scenario.Parameters.Select(p => p.Name).Concat(overrides.Keys).Distinct(StringComparer.Ordinal);
        foreach (var parameterName in names)
        {
            var parameter = metadata.FindParameter(parameterName);
            var member = parameter is null ? null : $"c => c.{parameter.Name}";
            overrides.TryGetValue(parameterName, out var arg);
            var assignment = scenario.FindParameter(parameterName);

            if (arg?.Kind == ArgKind.Unset)
                continue;

            if (arg is null)
            {
                if (assignment is null) continue;
                lines.Add(assignment.Kind switch
                {
                    ParameterValueKind.Literal when member is not null => $".Parameter({member}, {CSharpValue(assignment.Value, parameter!.Type)})",
                    ParameterValueKind.Literal => $".Parameter({Literal(parameterName)}, {CSharpValue(assignment.Value, assignment.Value?.GetType() ?? typeof(object))})",
                    ParameterValueKind.Text when parameterName == "ChildContent" => $".ChildContent({Literal(assignment.Value as string ?? "")})",
                    ParameterValueKind.Text => $".Fragment({member}, {Literal(assignment.Value as string ?? "")})",
                    _ when assignment.FixtureName is { } fixture => $".Fixture({member}, {Literal(fixture)})",
                    _ => $"// {parameterName}: {assignment.Display ?? "value"} is produced by code in the original scenario",
                });
                continue;
            }

            if (parameter is null)
            {
                lines.Add($"// {parameterName}: not a parameter of {metadata.Name}");
                continue;
            }

            switch (arg.Kind)
            {
                case ArgKind.Null:
                    lines.Add($".Parameter({member}, null)");
                    break;
                case ArgKind.Fixture:
                    lines.Add($".Fixture({member}, {Literal(arg.Fixture!)})");
                    break;
                case ArgKind.Value when parameter.Kind is ParameterKind.ChildContent:
                    lines.Add($".ChildContent({Literal(Text(arg))})");
                    break;
                case ArgKind.Value when parameter.Kind is ParameterKind.RenderFragment:
                    lines.Add($".Fragment({member}, {Literal(Text(arg))})");
                    break;
                case ArgKind.Value when arg.Value is { } json && ParameterValueConverter.TryConvert(json, parameter.Type, out var value, out _):
                    lines.Add($".Parameter({member}, {CSharpValue(value, parameter.Type)})");
                    break;
                default:
                    lines.Add($"// {parameterName}: value cannot be expressed as C# literal");
                    break;
            }
        }

        foreach (var line in lines)
            sb.AppendLine().Append("    ").Append(line);
        sb.Append(");");
        return sb.ToString();
    }

    private static string Text(ArgOverride arg) =>
        arg.Value is { ValueKind: System.Text.Json.JsonValueKind.String } v ? v.GetString() ?? "" : arg.Value?.GetRawText() ?? "";

    /// <summary>Formats a supported value as a C# expression.</summary>
    public static string CSharpValue(object? value, Type type) => value switch
    {
        null => "null",
        string s => Literal(s),
        bool b => b ? "true" : "false",
        char c => $"'{(c == '\'' ? "\\'" : c.ToString())}'",
        Enum e => string.Join(" | ", e.ToString().Split(", ").Select(part => $"{Naming.FormatTypeName(e.GetType())}.{part}")),
        decimal d => d.ToString(CultureInfo.InvariantCulture) + "m",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + (d % 1 == 0 ? ".0" : ""),
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
        uint ui => ui.ToString(CultureInfo.InvariantCulture) + "U",
        int or short or byte or sbyte or ushort => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        DateTime dt => $"DateTime.Parse(\"{dt:O}\", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)",
        DateTimeOffset dto => $"DateTimeOffset.Parse(\"{dto:O}\", CultureInfo.InvariantCulture)",
        DateOnly d => $"new DateOnly({d.Year}, {d.Month}, {d.Day})",
        TimeOnly t => $"new TimeOnly({t.Hour}, {t.Minute}, {t.Second})",
        TimeSpan ts => $"TimeSpan.Parse(\"{ts:c}\", CultureInfo.InvariantCulture)",
        Guid g => $"Guid.Parse(\"{g}\")",
        Uri u => $"new Uri({Literal(u.OriginalString)}, UriKind.RelativeOrAbsolute)",
        _ => $"/* {Naming.FormatTypeName(type)} */ default",
    };

    public static string Literal(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        }

        return sb.Append('"').ToString();
    }
}
