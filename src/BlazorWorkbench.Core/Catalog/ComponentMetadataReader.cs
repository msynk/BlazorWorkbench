using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Catalog;

/// <summary>Extracts <see cref="ComponentMetadata"/> from component types using reflection, XML documentation and portable PDBs.</summary>
public sealed class ComponentMetadataReader
{
    private readonly ConcurrentDictionary<Type, ComponentMetadata> _cache = new();
    private readonly Func<Type, bool> _hasCustomEditor;
    private readonly bool _readDefaults;
    private readonly bool _resolveSources;

    public ComponentMetadataReader(Func<Type, bool>? hasCustomEditor = null, bool readDefaults = true, bool resolveSources = true)
    {
        _hasCustomEditor = hasCustomEditor ?? (_ => false);
        _readDefaults = readDefaults;
        _resolveSources = resolveSources;
    }

    public ComponentMetadata Read(Type componentType) => _cache.GetOrAdd(componentType, ReadCore);

    private ComponentMetadata ReadCore(Type type)
    {
        var docs = XmlDocumentation.For(type.Assembly);
        var docType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var instance = _readDefaults ? TryCreate(type) : null;
        var obsolete = type.GetCustomAttribute<ObsoleteAttribute>();

        var properties = GetAllProperties(type);
        var parameterProps = properties
            .Where(p => p.IsDefined(typeof(ParameterAttribute), true) || p.IsDefined(typeof(CascadingParameterAttribute), true))
            .ToList();
        var names = new HashSet<string>(parameterProps.Select(p => p.Name), StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();

        var parameters = parameterProps
            .Select(p => ReadParameter(p, docs, instance, names, nullability))
            .OrderBy(p => p.Kind == ParameterKind.Cascading ? 1 : 0)
            .ThenBy(p => p.IsRequired ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var injections = properties
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<InjectAttribute>(true)))
            .Where(x => x.Attribute is not null)
            .Select(x => new InjectionDescriptor(x.Property.Name, x.Property.PropertyType, Naming.FormatTypeName(x.Property.PropertyType), x.Attribute!.Key))
            .OrderBy(i => i.PropertyName, StringComparer.Ordinal)
            .Concat(ReadConstructorDependencies(type))
            .ToArray();

        return new ComponentMetadata
        {
            Type = type,
            Name = Naming.FormatTypeName(type),
            FullName = Naming.FormatTypeName(type, fullName: true),
            Namespace = type.Namespace,
            AssemblyName = type.Assembly.GetName().Name ?? "",
            Summary = docs?.GetSummary("T:" + XmlDocumentation.TypeId(docType)),
            Remarks = docs?.GetRemarks("T:" + XmlDocumentation.TypeId(docType)),
            IsGeneric = type.IsGenericType,
            GenericArguments = type.IsGenericType ? type.GetGenericArguments().Select(a => Naming.FormatTypeName(a)).ToArray() : [],
            IsObsolete = obsolete is not null,
            ObsoleteMessage = obsolete?.Message,
            Route = type.GetCustomAttributes<RouteAttribute>(false).FirstOrDefault()?.Template,
            BaseType = type.BaseType is { } b && b != typeof(object) ? Naming.FormatTypeName(b) : null,
            Parameters = parameters,
            Injections = injections,
            Source = _resolveSources ? SourceLocator.Find(type) : null,
        };
    }

    private ParameterDescriptor ReadParameter(PropertyInfo property, XmlDocumentation? docs, object? instance, HashSet<string> names, NullabilityInfoContext nullability)
    {
        var type = property.PropertyType;
        var parameterAttribute = property.GetCustomAttribute<ParameterAttribute>(true);
        var cascading = property.GetCustomAttribute<CascadingParameterAttribute>(true);
        var obsolete = property.GetCustomAttribute<ObsoleteAttribute>(true);
        var kind = ClassifyKind(property, type, parameterAttribute, cascading);
        var (editor, unsupported) = ClassifyEditor(kind, type);
        var hints = ReadEditorHints(property);
        if (hints.DataType is { } dataType && kind == ParameterKind.Value && (Nullable.GetUnderlyingType(type) ?? type) == typeof(string))
        {
            if (dataType == DataType.MultilineText) editor = EditorKind.MultilineText;
            else if (hints.IsColor) editor = EditorKind.Color;
        }

        var isNullable = Nullable.GetUnderlyingType(type) is not null;
        if (!type.IsValueType)
        {
            try { isNullable = nullability.Create(property).WriteState != NullabilityState.NotNull; }
            catch (InvalidOperationException) { isNullable = true; }
        }

        object? defaultValue = null;
        var hasDefault = false;
        if (instance is not null && kind is ParameterKind.Value && property.GetMethod is not null)
        {
            try
            {
                defaultValue = property.GetValue(instance);
                hasDefault = true;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or NotSupportedException)
            {
                // Some getters depend on initialization; the default is simply unknown.
            }
        }

        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        var declaring = property.DeclaringType is { } d && d.IsGenericType ? d.GetGenericTypeDefinition() : property.DeclaringType;
        string? boundTo = null;
        if (kind == ParameterKind.EventCallback && property.Name.EndsWith("Changed", StringComparison.Ordinal) &&
            names.Contains(property.Name[..^"Changed".Length]))
            boundTo = property.Name[..^"Changed".Length];
        else if (kind == ParameterKind.Value && names.Contains(property.Name + "Changed"))
            boundTo = property.Name + "Changed";

        return new ParameterDescriptor
        {
            Name = property.Name,
            Type = type,
            TypeName = Naming.FormatTypeName(type),
            Kind = kind,
            IsRequired = property.IsDefined(typeof(EditorRequiredAttribute), true),
            IsNullable = isNullable,
            IsObsolete = obsolete is not null,
            ObsoleteMessage = obsolete?.Message,
            Summary = (declaring is null ? null : docs?.GetSummary($"P:{XmlDocumentation.TypeId(declaring)}.{property.Name}")
                      ?? XmlDocumentation.For(declaring.Assembly)?.GetSummary($"P:{XmlDocumentation.TypeId(declaring)}.{property.Name}"))
                      ?? hints.Description,
            DisplayName = hints.DisplayName,
            Group = hints.Group,
            Order = hints.Order,
            IsHidden = hints.IsHidden,
            Minimum = hints.Minimum,
            Maximum = hints.Maximum,
            MaxLength = hints.MaxLength,
            DefaultValue = defaultValue,
            HasDefaultValue = hasDefault,
            DefaultValueDisplay = hasDefault ? FormatDefault(defaultValue) : null,
            // Registered custom editors win over built-in ones (for value parameters).
            Editor = kind == ParameterKind.Value && _hasCustomEditor(enumType) ? EditorKind.Custom : editor,
            UnsupportedReason = editor == EditorKind.Unsupported && !(kind == ParameterKind.Value && _hasCustomEditor(enumType)) ? unsupported : null,
            EnumValues = enumType.IsEnum ? Enum.GetNames(enumType) : [],
            BoundTo = boundTo,
            CascadingName = cascading?.Name,
            DeclaredBy = property.DeclaringType is { } dt && dt != property.ReflectedType ? Naming.FormatTypeName(dt) : null,
        };
    }

    private sealed record EditorHints(
        string? DisplayName, string? Description, string? Group, int? Order, bool IsHidden,
        double? Minimum, double? Maximum, int? MaxLength, DataType? DataType, bool IsColor);

    /// <summary>
    /// Reads the standard .NET attributes that describe how a value is edited, so parameters get a label, a section, an order,
    /// a slider or a text area without Workbench-specific code: <c>[Display]</c>, <c>[DisplayName]</c>, <c>[Description]</c>,
    /// <c>[Category]</c>, <c>[EditorBrowsable]</c>, <c>[Browsable]</c>, <c>[Range]</c>, <c>[StringLength]</c>,
    /// <c>[MaxLength]</c> and <c>[DataType]</c>. Attributes that cannot be read are ignored.
    /// </summary>
    private static EditorHints ReadEditorHints(PropertyInfo property)
    {
        try
        {
            var display = property.GetCustomAttribute<DisplayAttribute>(true);
            var range = property.GetCustomAttribute<RangeAttribute>(true);
            var dataType = property.GetCustomAttribute<DataTypeAttribute>(true);
            var browsable = property.GetCustomAttribute<EditorBrowsableAttribute>(true);
            var hidden = browsable?.State == EditorBrowsableState.Never || property.GetCustomAttribute<BrowsableAttribute>(true)?.Browsable == false;
            var maxLength = property.GetCustomAttribute<StringLengthAttribute>(true)?.MaximumLength
                            ?? property.GetCustomAttribute<MaxLengthAttribute>(true)?.Length;
            return new EditorHints(
                DisplayName: NullIfEmpty(display?.GetName()) ?? NullIfEmpty(property.GetCustomAttribute<DisplayNameAttribute>(true)?.DisplayName),
                Description: NullIfEmpty(display?.GetDescription()) ?? NullIfEmpty(property.GetCustomAttribute<DescriptionAttribute>(true)?.Description),
                Group: NullIfEmpty(display?.GetGroupName()) ?? NullIfEmpty(property.GetCustomAttribute<CategoryAttribute>(true)?.Category),
                Order: display?.GetOrder(),
                IsHidden: hidden,
                Minimum: ToDouble(range?.Minimum),
                Maximum: ToDouble(range?.Maximum),
                MaxLength: maxLength is > 0 ? maxLength : null,
                DataType: dataType?.DataType,
                IsColor: dataType is { DataType: DataType.Custom, CustomDataType: { } custom } && string.Equals(custom, "Color", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or TypeLoadException or CustomAttributeFormatException)
        {
            return new EditorHints(null, null, null, null, false, null, null, null, null, false);
        }

        static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        static double? ToDouble(object? value) => value switch
        {
            null => null,
            IConvertible c when value is not string => c.ToDouble(CultureInfo.InvariantCulture) is var d && double.IsFinite(d) ? d : null,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) => parsed,
            _ => null,
        };
    }

    private static ParameterKind ClassifyKind(PropertyInfo property, Type type, ParameterAttribute? parameter, CascadingParameterAttribute? cascading)
    {
        if (cascading is not null && parameter is null)
            return ParameterKind.Cascading;
        if (parameter?.CaptureUnmatchedValues == true)
            return ParameterKind.CapturedAttributes;
        if (type == typeof(RenderFragment))
            return property.Name == "ChildContent" ? ParameterKind.ChildContent : ParameterKind.RenderFragment;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RenderFragment<>))
            return ParameterKind.Template;
        if (type == typeof(EventCallback) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EventCallback<>)))
            return ParameterKind.EventCallback;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Linq.Expressions.Expression<>))
            return ParameterKind.Expression;
        return ParameterKind.Value;
    }

    /// <summary>Selects the inspector editor for a parameter type.</summary>
    public static (EditorKind Editor, string? UnsupportedReason) ClassifyEditor(ParameterKind kind, Type type)
    {
        switch (kind)
        {
            case ParameterKind.ChildContent:
            case ParameterKind.RenderFragment:
                return (EditorKind.MultilineText, null);
            case ParameterKind.Template:
                return (EditorKind.Unsupported, "Templates (RenderFragment<T>) are supplied by scenarios.");
            case ParameterKind.EventCallback:
                return (EditorKind.Callback, null);
            case ParameterKind.Expression:
                return (EditorKind.Unsupported, "Value expressions are generated by @bind and supplied automatically.");
            case ParameterKind.CapturedAttributes:
                return (EditorKind.Unsupported, "Additional attributes are supplied by scenarios with Parameter(name, value).");
            case ParameterKind.Cascading:
                return (EditorKind.Unsupported, "Cascading values are supplied by scenarios with Cascade(...).");
        }

        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(bool)) return (EditorKind.Boolean, null);
        if (t == typeof(string) || t == typeof(char) || t == typeof(Uri)) return (EditorKind.Text, null);
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte))
            return (EditorKind.Integer, null);
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return (EditorKind.Decimal, null);
        if (t.IsEnum) return (t.IsDefined(typeof(FlagsAttribute), false) ? EditorKind.FlagsEnum : EditorKind.Enum, null);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return (EditorKind.DateTime, null);
        if (t == typeof(DateOnly)) return (EditorKind.Date, null);
        if (t == typeof(TimeOnly)) return (EditorKind.Time, null);
        if (t == typeof(TimeSpan)) return (EditorKind.TimeSpan, null);
        if (t == typeof(Guid)) return (EditorKind.Guid, null);
        if (typeof(Delegate).IsAssignableFrom(t))
            return (EditorKind.Unsupported, "Delegates are supplied by scenarios.");
        if (Values.ParameterValueConverter.IsJsonEditable(t))
            return (EditorKind.Json, null);
        return (EditorKind.Unsupported, $"No editor for {Naming.FormatTypeName(type)}. Declare fixtures with set.Fixture<{Naming.FormatTypeName(type)}>(...) or register a custom editor.");
    }

    private static string FormatDefault(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ when value.GetType().IsPrimitive || value is Enum or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or DateOnly or TimeOnly
            => Naming.FormatValue(value),
        _ => $"new {Naming.FormatTypeName(value.GetType())}(...)",
    };

    private static object? TryCreate(Type type)
    {
        if (type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) is null)
            return null;
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    // .NET 9+ activates components with constructor injection; list those dependencies too.
    private static IEnumerable<InjectionDescriptor> ReadConstructorDependencies(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        return constructor is null
            ? []
            : constructor.GetParameters().Select(p => new InjectionDescriptor("ctor:" + p.Name, p.ParameterType, Naming.FormatTypeName(p.ParameterType), null));
    }

    private static List<PropertyInfo> GetAllProperties(Type type)
    {
        var result = new List<PropertyInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t is not null && t != typeof(object) && t != typeof(ComponentBase); t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length == 0 && !p.IsDefined(typeof(CompilerGeneratedAttribute)) && seen.Add(p.Name))
                    result.Add(p);
            }
        }

        return result;
    }
}
