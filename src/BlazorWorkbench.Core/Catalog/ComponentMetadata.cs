using System.Text.Json.Serialization;

namespace BlazorWorkbench.Catalog;

/// <summary>How the parameter participates in rendering.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParameterKind>))]
public enum ParameterKind
{
    Value,
    ChildContent,
    RenderFragment,
    Template,
    EventCallback,
    Expression,
    CapturedAttributes,
    Cascading,
}

/// <summary>Editor used by the parameter inspector.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorKind>))]
public enum EditorKind
{
    Unsupported,
    Boolean,
    Text,
    MultilineText,
    Integer,
    Decimal,
    Enum,
    FlagsEnum,
    Date,
    DateTime,
    Time,
    TimeSpan,
    Guid,
    Color,
    Fixture,
    Custom,
    Callback,
}

/// <summary>Reflection metadata of a component parameter.</summary>
public sealed class ParameterDescriptor
{
    public required string Name { get; init; }

    /// <summary>The parameter type (not serialized; available in-process only).</summary>
    [JsonIgnore] public Type Type { get; init; } = typeof(object);

    public required string TypeName { get; init; }
    public required ParameterKind Kind { get; init; }
    public bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public bool IsObsolete { get; init; }
    public string? ObsoleteMessage { get; init; }
    public string? Summary { get; init; }

    /// <summary>Default value read from a freshly constructed component (when it could be constructed safely).</summary>
    [JsonIgnore] public object? DefaultValue { get; init; }

    public bool HasDefaultValue { get; init; }
    public string? DefaultValueDisplay { get; init; }
    public EditorKind Editor { get; init; }

    /// <summary>Why the inspector cannot edit the parameter (when <see cref="Editor"/> is <see cref="EditorKind.Unsupported"/>).</summary>
    public string? UnsupportedReason { get; init; }

    public IReadOnlyList<string> EnumValues { get; init; } = [];

    /// <summary>For bound parameters (<c>Value</c> + <c>ValueChanged</c>), the name of the paired parameter.</summary>
    public string? BoundTo { get; init; }

    public string? CascadingName { get; init; }

    /// <summary>The declaring type (for inherited parameters).</summary>
    public string? DeclaredBy { get; init; }
}

/// <summary>A dependency injected into the component with <c>[Inject]</c> / <c>@inject</c>.</summary>
public sealed record InjectionDescriptor(string PropertyName, [property: JsonIgnore] Type ServiceType, string ServiceTypeName, object? Key);

/// <summary>Reflection metadata of a component type.</summary>
public sealed class ComponentMetadata
{
    [JsonIgnore] public required Type Type { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string? Namespace { get; init; }
    public required string AssemblyName { get; init; }
    public string? Summary { get; init; }
    public string? Remarks { get; init; }
    public bool IsGeneric { get; init; }
    public IReadOnlyList<string> GenericArguments { get; init; } = [];
    public bool IsObsolete { get; init; }
    public string? ObsoleteMessage { get; init; }
    public string? Route { get; init; }
    public string? BaseType { get; init; }
    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
    public IReadOnlyList<InjectionDescriptor> Injections { get; init; } = [];
    public SourceLocation? Source { get; init; }

    public ParameterDescriptor? FindParameter(string name) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
        ?? Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool CapturesUnmatchedValues => Parameters.Any(p => p.Kind == ParameterKind.CapturedAttributes);
}

/// <summary>A source file location resolved from portable PDB data.</summary>
public sealed record SourceLocation(string Path, int Line);
