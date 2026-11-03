using System.Globalization;
using System.Text;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Values;

namespace BlazorWorkbench.Documentation;

/// <summary>Generates copyable Razor markup for a scenario (optionally with inspector overrides applied).</summary>
public static class RazorSnippetGenerator
{
    public static string Generate(ScenarioDefinition scenario, ComponentMetadata metadata, IReadOnlyDictionary<string, ArgOverride>? overrides = null)
    {
        var attributes = new List<string>();
        var comments = new List<string>();
        string? childText = null;
        var hasChildFragment = false;

        if (metadata.Type.IsGenericType)
        {
            var definition = metadata.Type.GetGenericTypeDefinition().GetGenericArguments();
            var args = metadata.Type.GetGenericArguments();
            for (var i = 0; i < definition.Length; i++)
                attributes.Add($"{definition[i].Name}=\"{Naming.FormatTypeName(args[i])}\"");
        }

        var names = scenario.Parameters.Select(p => p.Name)
            .Concat(overrides?.Keys ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => metadata.FindParameter(n) is { } p ? metadata.Parameters.ToList().IndexOf(p) : int.MaxValue)
            .ThenBy(n => n, StringComparer.Ordinal);

        foreach (var name in names)
        {
            var parameter = metadata.FindParameter(name);
            var assignment = scenario.FindParameter(name);
            ArgOverride? arg = null;
            overrides?.TryGetValue(name, out arg);
            if (arg?.Kind == ArgKind.Unset)
                continue;

            if (parameter?.Kind is ParameterKind.ChildContent)
            {
                if (arg is { Kind: ArgKind.Value, Value: { } v }) childText = v.ToString();
                else if (assignment?.Kind == ParameterValueKind.Text) childText = assignment.Value as string;
                else if (assignment is not null) hasChildFragment = true;
                continue;
            }

            if (arg is not null)
            {
                attributes.Add(arg.Kind switch
                {
                    ArgKind.Null => $"{name}=\"@null\"",
                    ArgKind.Fixture => $"{name}=\"@{ToIdentifier(arg.Fixture!)}\"",
                    _ when arg.Value is { } json && parameter is not null &&
                           ParameterValueConverter.TryConvert(json, parameter.Type, out var converted, out _) => Attribute(name, converted, parameter.Type),
                    _ => $"{name}=\"{Escape(arg.Value?.ToString() ?? "")}\"",
                });
                continue;
            }

            if (assignment is null)
                continue;

            switch (assignment.Kind)
            {
                case ParameterValueKind.Literal:
                    attributes.Add(Attribute(name, assignment.Value, parameter?.Type ?? assignment.Value?.GetType() ?? typeof(object)));
                    break;
                case ParameterValueKind.Text:
                    attributes.Add($"{name}=\"@((RenderFragment)(b => b.AddContent(0, {Literal(assignment.Value as string ?? "")})))\"");
                    break;
                default:
                    var identifier = ToIdentifier(assignment.FixtureName ?? name);
                    attributes.Add($"{name}=\"@{identifier}\"");
                    comments.Add($"@* {identifier}: {assignment.Display ?? parameter?.TypeName ?? "value"} supplied by the scenario *@");
                    break;
            }
        }

        var tag = metadata.Type.IsGenericType ? metadata.Type.Name[..metadata.Type.Name.IndexOf('`', StringComparison.Ordinal)] : metadata.Type.Name;
        var sb = new StringBuilder();
        foreach (var comment in comments)
            sb.AppendLine(comment);

        sb.Append('<').Append(tag);
        if (attributes.Count > 0)
        {
            var single = string.Join(' ', attributes);
            if (single.Length + tag.Length < 90)
                sb.Append(' ').Append(single);
            else
                foreach (var attribute in attributes) sb.AppendLine().Append("    ").Append(attribute);
        }

        if (childText is not null || hasChildFragment)
        {
            sb.Append('>');
            sb.Append(childText is not null ? EscapeContent(childText) : "@* child content *@");
            sb.Append("</").Append(tag).Append('>');
        }
        else
        {
            sb.Append(" />");
        }

        return sb.ToString();
    }

    private static string Attribute(string name, object? value, Type type) => value switch
    {
        null => $"{name}=\"@null\"",
        string s when type == typeof(string) || type == typeof(object) => $"{name}=\"{Escape(s)}\"",
        bool b => $"{name}=\"{(b ? "true" : "false")}\"",
        Enum e => $"{name}=\"{Naming.FormatTypeName(e.GetType())}.{e.ToString().Replace(", ", $" | {Naming.FormatTypeName(e.GetType())}.", StringComparison.Ordinal)}\"",
        decimal d => $"{name}=\"{d.ToString(CultureInfo.InvariantCulture)}m\"",
        float f => $"{name}=\"{f.ToString("R", CultureInfo.InvariantCulture)}f\"",
        double db => $"{name}=\"{db.ToString("R", CultureInfo.InvariantCulture)}\"",
        IFormattable f when value.GetType().IsPrimitive => $"{name}=\"{f.ToString(null, CultureInfo.InvariantCulture)}\"",
        DateTime dt => $"{name}=\"@DateTime.Parse(\"{dt:O}\")\"",
        DateOnly dateOnly => $"{name}=\"@(new DateOnly({dateOnly.Year}, {dateOnly.Month}, {dateOnly.Day}))\"",
        TimeSpan ts => $"{name}=\"@TimeSpan.Parse(\"{ts:c}\")\"",
        Guid g => $"{name}=\"@Guid.Parse(\"{g}\")\"",
        string s => $"{name}=\"@{Literal(s)}\"",
        _ => $"{name}=\"@{ToIdentifier(name)}\"",
    };

    private static string ToIdentifier(string name)
    {
        var parts = Naming.ToKebabCase(name).Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "value";
        var id = parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsDigit(id[0]) ? "_" + id : id;
    }

    private static string Literal(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("@", "@@", StringComparison.Ordinal);

    private static string EscapeContent(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("@", "@@", StringComparison.Ordinal);
}
