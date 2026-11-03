using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace BlazorWorkbench;

/// <summary>Naming helpers used to produce stable identifiers and readable type names.</summary>
public static class Naming
{
    /// <summary>Converts text such as <c>DataGrid&lt;Person&gt;</c> or <c>Primary Button</c> to <c>data-grid-person</c> / <c>primary-button</c>.</summary>
    public static string ToKebabCase(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        var previous = '-';
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch))
            {
                var boundary = char.IsUpper(ch) && i > 0 && previous != '-' &&
                               (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]) ||
                                (i + 1 < value.Length && char.IsLower(value[i + 1]) && char.IsUpper(value[i - 1])));
                if (boundary)
                    sb.Append('-');
                previous = char.ToLowerInvariant(ch);
                sb.Append(previous);
            }
            else if (previous != '-')
            {
                sb.Append('-');
                previous = '-';
            }
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>Returns the property name of an expression such as <c>c =&gt; c.Title</c>.</summary>
    public static string GetMemberName(LambdaExpression expression)
    {
        var body = expression.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        return body is MemberExpression { Member: { } member } && member.DeclaringType is not null
            ? member.Name
            : throw new ArgumentException($"Expression '{expression}' must select a component parameter property, for example 'c => c.Title'.", nameof(expression));
    }

    /// <summary>Formats a type as C# source (<c>List&lt;Person&gt;</c>, <c>int?</c>, <c>EventCallback&lt;MouseEventArgs&gt;</c>).</summary>
    public static string FormatTypeName(Type type, bool fullName = false)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return FormatTypeName(underlying, fullName) + "?";
        if (type.IsArray)
            return FormatTypeName(type.GetElementType()!, fullName) + "[]";
        if (s_aliases.TryGetValue(type, out var alias))
            return alias;

        var name = fullName && type.Namespace is not null ? $"{type.Namespace}.{type.Name}" : type.Name;
        if (type.IsGenericParameter)
            return type.Name;
        if (!type.IsGenericType)
            return type.IsNested && !fullName ? $"{FormatTypeName(type.DeclaringType!)}.{type.Name}" : name;

        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            name = name[..tick];
        var args = type.GetGenericArguments().Select(a => FormatTypeName(a, fullName));
        return $"{name}<{string.Join(", ", args)}>";
    }

    /// <summary>Formats a value for display (invariant culture, quoted strings, enum names).</summary>
    public static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        Enum e => $"{e.GetType().Name}.{e}",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static readonly Dictionary<Type, string> s_aliases = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(void)] = "void",
    };
}
