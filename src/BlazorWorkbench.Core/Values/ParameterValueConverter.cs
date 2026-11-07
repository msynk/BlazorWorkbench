using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorWorkbench.Values;

/// <summary>How an inspector argument relates to the scenario's declared value.</summary>
public enum ArgKind
{
    /// <summary>An explicit value.</summary>
    Value,

    /// <summary>An explicit <c>null</c>.</summary>
    Null,

    /// <summary>The parameter is not supplied, so the component's own default applies.</summary>
    Unset,

    /// <summary>A named fixture.</summary>
    Fixture,
}

/// <summary>An inspector override of one parameter. Overrides are transient until exported as a scenario.</summary>
public sealed record ArgOverride(ArgKind Kind, JsonElement? Value = null, string? Fixture = null)
{
    public static ArgOverride Null { get; } = new(ArgKind.Null);
    public static ArgOverride Unset { get; } = new(ArgKind.Unset);
    public static ArgOverride FromFixture(string name) => new(ArgKind.Fixture, null, name);
    public static ArgOverride FromValue(JsonElement value) => new(ArgKind.Value, value);
}

/// <summary>
/// Encodes inspector overrides as compact JSON used in URLs and messages:
/// <c>{"Text":"Hi","Count":3,"Icon":null,"Items":{"$fixture":"Many"},"Size":{"$unset":true}}</c>.
/// </summary>
public static class ArgsCodec
{
    public static string Encode(IReadOnlyDictionary<string, ArgOverride> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, arg) in args.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                switch (arg.Kind)
                {
                    case ArgKind.Null:
                        writer.WriteNullValue();
                        break;
                    case ArgKind.Unset:
                        writer.WriteStartObject();
                        writer.WriteBoolean("$unset", true);
                        writer.WriteEndObject();
                        break;
                    case ArgKind.Fixture:
                        writer.WriteStartObject();
                        writer.WriteString("$fixture", arg.Fixture);
                        writer.WriteEndObject();
                        break;
                    default:
                        if (arg.Value is { } v) v.WriteTo(writer);
                        else writer.WriteNullValue();
                        break;
                }
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Dictionary<string, ArgOverride> Decode(string? json)
    {
        var result = new Dictionary<string, ArgOverride>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("Arguments must be a JSON object.");

        foreach (var property in doc.RootElement.EnumerateObject())
            result[property.Name] = FromElement(property.Value);
        return result;
    }

    public static ArgOverride FromElement(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return ArgOverride.Null;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("$fixture", out var fixture) && fixture.ValueKind == JsonValueKind.String)
                return ArgOverride.FromFixture(fixture.GetString()!);
            if (value.TryGetProperty("$unset", out _))
                return ArgOverride.Unset;
        }

        return ArgOverride.FromValue(value.Clone());
    }
}

/// <summary>Converts JSON inspector values to parameter types and back. Only explicitly supported types are converted.</summary>
public static class ParameterValueConverter
{
    // NaN and ±Infinity travel as the strings "NaN", "Infinity" and "-Infinity", which TryConvert parses back.
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>True when values of <paramref name="type"/> can be edited and serialized by the inspector.</summary>
    public static bool IsSupported(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        // IntPtr/UIntPtr are primitives but have no JSON representation.
        return (t.IsPrimitive && t != typeof(nint) && t != typeof(nuint)) || t.IsEnum || t == typeof(string) || t == typeof(decimal) ||
               t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan) ||
               t == typeof(Guid) || t == typeof(Uri);
    }

    public static bool TryConvert(JsonElement element, Type target, out object? value, out string? error)
    {
        value = null;
        error = null;
        var underlying = Nullable.GetUnderlyingType(target);
        var t = underlying ?? target;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (target.IsValueType && underlying is null)
            {
                error = $"{Naming.FormatTypeName(target)} does not accept null.";
                return false;
            }

            return true;
        }

        var text = element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
        try
        {
            if (t == typeof(string)) { value = text; return true; }
            if (t == typeof(char))
            {
                if (text.Length != 1) { error = "Enter exactly one character."; return false; }
                value = text[0];
                return true;
            }

            if (t == typeof(bool))
            {
                if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = element.GetBoolean(); return true; }
                if (bool.TryParse(text, out var b)) { value = b; return true; }
                error = $"'{text}' is not true or false.";
                return false;
            }

            if (t.IsEnum)
                return TryConvertEnum(text, t, out value, out error);

            if (t == typeof(Guid))
                return Parsed(Guid.TryParse(text, out var g), g, "a GUID", text, out value, out error);
            if (t == typeof(DateTime))
                return Parsed(DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt), dt, "an ISO 8601 date and time", text, out value, out error);
            if (t == typeof(DateTimeOffset))
                return Parsed(DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto), dto, "an ISO 8601 date and time", text, out value, out error);
            if (t == typeof(DateOnly))
                return Parsed(DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d), d, "a date (yyyy-MM-dd)", text, out value, out error);
            if (t == typeof(TimeOnly))
                return Parsed(TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var to), to, "a time (HH:mm)", text, out value, out error);
            if (t == typeof(TimeSpan))
                return Parsed(TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts), ts, "a time span (d.hh:mm:ss)", text, out value, out error);
            if (t == typeof(Uri))
                return Parsed(Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var uri), uri, "a URI", text, out value, out error);

            if (t.IsPrimitive || t == typeof(decimal))
            {
                if (string.IsNullOrWhiteSpace(text)) { error = "Enter a number."; return false; }
                value = Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            error = ex is OverflowException
                ? $"'{text}' is outside the range of {Naming.FormatTypeName(t)}."
                : $"'{text}' is not a valid {Naming.FormatTypeName(t)}.";
            return false;
        }

        error = $"{Naming.FormatTypeName(target)} cannot be edited in the inspector.";
        return false;
    }

    /// <summary>Serializes a supported value to JSON for the inspector (returns false for unsupported values).</summary>
    public static bool TrySerialize(object? value, out JsonElement element)
    {
        element = default;
        if (value is null)
        {
            element = JsonSerializer.SerializeToElement<object?>(null, s_json);
            return true;
        }

        if (!IsSupported(value.GetType()))
            return false;

        try
        {
            element = value switch
            {
                DateTime dt => JsonSerializer.SerializeToElement(dt.ToString("O", CultureInfo.InvariantCulture)),
                DateTimeOffset dto => JsonSerializer.SerializeToElement(dto.ToString("O", CultureInfo.InvariantCulture)),
                DateOnly d => JsonSerializer.SerializeToElement(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                TimeOnly t => JsonSerializer.SerializeToElement(t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                TimeSpan ts => JsonSerializer.SerializeToElement(ts.ToString("c", CultureInfo.InvariantCulture)),
                Uri u => JsonSerializer.SerializeToElement(u.OriginalString),
                char c => JsonSerializer.SerializeToElement(c.ToString()),
                _ => JsonSerializer.SerializeToElement(value, value.GetType(), s_json),
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or JsonException)
        {
            // The contract is "false for unsupported values"; callers (catalog API, two-way binding) must never throw.
            element = default;
            return false;
        }
    }

    private static bool TryConvertEnum(string text, Type enumType, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (Enum.TryParse(enumType, text, ignoreCase: true, out var parsed))
        {
            var isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);
            if (isFlags || Enum.IsDefined(enumType, parsed))
            {
                value = parsed;
                return true;
            }
        }

        error = $"'{text}' is not a value of {enumType.Name} ({string.Join(", ", Enum.GetNames(enumType))}).";
        return false;
    }

    private static bool Parsed<T>(bool ok, T parsed, string expected, string text, out object? value, out string? error)
    {
        value = ok ? parsed : null;
        error = ok ? null : $"'{text}' is not {expected}.";
        return ok;
    }
}
