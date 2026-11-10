using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Catalog;

/// <summary>
/// Finds which components render which other components by reading the compiled IL of component types.
/// </summary>
/// <remarks>
/// <para>
/// Razor compiles child components to calls of <c>RenderTreeBuilder.OpenComponent&lt;TComponent&gt;(int)</c>; hand written
/// render code may also call <c>OpenComponent(int, Type)</c> with a <c>typeof(...)</c> argument. Both forms are detected in
/// the component's own methods and constructors, in its nested types (render fragment lambdas, closures, local functions and
/// async state machines are compiler-generated nested types), in its base types (up to the first framework type) and in
/// Razor's generic type inference helpers (<c>__Blazor.*.TypeInference</c>) the component calls.
/// </para>
/// <para>
/// The analysis is static: components chosen at run time (<c>DynamicComponent</c>, <c>Type</c> values computed by code)
/// are not found. A <c>typeof(X)</c> counts when it is followed, in the same method, by a call to
/// <c>OpenComponent(int, Type)</c>. Nested types that are components themselves are not attributed to their outer type.
/// </para>
/// </remarks>
public static class ComponentRelations
{
    private const string RenderTreeBuilderTypeName = "Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder";
    private const string OpenComponentMethodName = "OpenComponent";
    private const string RazorHelperNamespace = "__Blazor";

    // Upper bound of Razor helper methods followed while scanning one component (guards against pathological inputs).
    private const int MaxFollowedHelpers = 1024;

    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly OpCodeTable s_opCodes = OpCodeTable.Create();

    /// <summary>Returns, for each type in <paramref name="componentTypes"/>, the other component types from the same set that it renders.</summary>
    /// <param name="componentTypes">The component types to analyze; only types from this set are reported.</param>
    /// <returns>
    /// One entry per distinct input type. Each list excludes the type itself, has no duplicates and is sorted by
    /// <see cref="Type.FullName"/>. A type that cannot be analyzed gets an empty list; this method does not throw for
    /// individual types.
    /// </returns>
    /// <remarks>
    /// A rendered generic component matches the identical type when the set contains it; otherwise (for example
    /// <c>DataTable&lt;TItem&gt;</c> inside a generic component, or an instantiation the set does not contain) it matches every
    /// type of the set with the same generic type definition.
    /// </remarks>
    public static IReadOnlyDictionary<Type, IReadOnlyList<Type>> FindRenderedComponents(IReadOnlyCollection<Type> componentTypes)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);

        var index = new ComponentIndex(componentTypes);
        var cache = new Dictionary<(Module Module, int Token), MethodBase?>();
        var result = new Dictionary<Type, IReadOnlyList<Type>>(index.Types.Count);
        foreach (var type in index.Types)
        {
            try
            {
                result[type] = new Scanner(index, cache).Scan(type);
            }
            catch (Exception)
            {
                // Never fail the whole analysis because of one type (unloadable dependencies, unusual metadata).
                result[type] = [];
            }
        }

        return result;
    }

    /// <summary>Inverts the result of <see cref="FindRenderedComponents"/>: for each type, the types that render it.</summary>
    /// <param name="uses">For each component type, the component types it renders.</param>
    /// <returns>
    /// One entry for every type that appears as a key or a value of <paramref name="uses"/>; each list is distinct and sorted
    /// by <see cref="Type.FullName"/>.
    /// </returns>
    public static IReadOnlyDictionary<Type, IReadOnlyList<Type>> Invert(IReadOnlyDictionary<Type, IReadOnlyList<Type>> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);

        var inverted = new Dictionary<Type, HashSet<Type>>();
        foreach (var user in uses.Keys)
            inverted.TryAdd(user, []);

        foreach (var (user, used) in uses)
        {
            foreach (var type in used)
            {
                if (!inverted.TryGetValue(type, out var users))
                    inverted[type] = users = [];
                users.Add(user);
            }
        }

        return inverted.ToDictionary(e => e.Key, e => (IReadOnlyList<Type>)Sort(e.Value));
    }

    private static Type[] Sort(IEnumerable<Type> types) =>
        types.OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.AssemblyQualifiedName ?? t.Name, StringComparer.Ordinal)
            .ToArray();

    private static bool IsFrameworkType(Type type)
    {
        var name = type.Assembly.GetName().Name ?? "";
        return name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.StartsWith("System.", StringComparison.Ordinal) ||
               name is "System" or "mscorlib" or "netstandard";
    }

    private static bool IsRazorHelper(Type? type) =>
        type?.Namespace is { } ns &&
        (ns == RazorHelperNamespace || ns.StartsWith(RazorHelperNamespace + ".", StringComparison.Ordinal));

    private static bool IsOpenComponent(MethodBase method) =>
        method.Name == OpenComponentMethodName && method.DeclaringType?.FullName == RenderTreeBuilderTypeName;

    private static bool TakesComponentType(MethodBase method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 2 && parameters[1].ParameterType.FullName == "System.Type";
    }

    // Metadata table of a token: TypeRef, TypeDef or TypeSpec.
    private static bool IsTypeToken(int token) => ((uint)token >> 24) is 0x01 or 0x02 or 0x1B;

    // Nested types of a constructed generic type are reported as generic definitions over the outer type's parameters;
    // close them with the outer type's arguments so their references resolve to the same instantiation.
    private static Type CloseNested(Type nested, Type outer)
    {
        if (!nested.IsGenericTypeDefinition || !outer.IsConstructedGenericType)
            return nested;

        var arguments = outer.GetGenericArguments();
        if (nested.GetGenericArguments().Length != arguments.Length)
            return nested;

        try
        {
            return nested.MakeGenericType(arguments);
        }
        catch (ArgumentException)
        {
            return nested;
        }
    }

    private static int OperandSize(OperandType type, byte[] il, int position) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => SwitchOperandSize(il, position),
        _ => -1,
    };

    // switch: a 4-byte target count followed by count 4-byte targets.
    private static int SwitchOperandSize(byte[] il, int position)
    {
        if (il.Length - position < 4)
            return -1;

        var count = BinaryPrimitives.ReadUInt32LittleEndian(il.AsSpan(position));
        var size = 4L + (count * 4L);
        return size <= il.Length - position ? (int)size : -1;
    }

    /// <summary>The component types being analyzed, with lookups by identity and by generic type definition.</summary>
    private sealed class ComponentIndex
    {
        private readonly HashSet<Type> _types;
        private readonly Dictionary<Type, List<Type>> _byDefinition = [];

        public ComponentIndex(IEnumerable<Type> types)
        {
            Types = types.Where(t => t is not null).Distinct().ToArray();
            _types = [.. Types];
            foreach (var type in Types)
            {
                if (!type.IsGenericType)
                    continue;

                var definition = type.GetGenericTypeDefinition();
                if (!_byDefinition.TryGetValue(definition, out var instantiations))
                    _byDefinition[definition] = instantiations = [];
                instantiations.Add(type);
            }
        }

        public IReadOnlyList<Type> Types { get; }

        public bool IsRelevant(Type type) =>
            _types.Contains(type) || (type.IsGenericType && _byDefinition.ContainsKey(type.GetGenericTypeDefinition()));

        public void Match(Type referenced, HashSet<Type> into)
        {
            if (_types.Contains(referenced))
            {
                into.Add(referenced);
                return;
            }

            if (referenced.IsGenericType && _byDefinition.TryGetValue(referenced.GetGenericTypeDefinition(), out var instantiations))
                into.UnionWith(instantiations);
        }
    }

    /// <summary>Scans the IL reachable from one component type.</summary>
    private sealed class Scanner(ComponentIndex index, Dictionary<(Module Module, int Token), MethodBase?> cache)
    {
        private readonly HashSet<Type> _found = [];
        private readonly HashSet<MethodBase> _followed = [];

        public IReadOnlyList<Type> Scan(Type component)
        {
            ScanTypeTree(component);
            for (var type = component.BaseType; type is not null && !IsFrameworkType(type); type = type.BaseType)
                ScanTypeTree(type);

            _found.Remove(component);
            return Sort(_found);
        }

        private void ScanTypeTree(Type type)
        {
            foreach (var method in type.GetMethods(DeclaredMembers))
                ScanMethod(method);
            foreach (var constructor in type.GetConstructors(DeclaredMembers))
                ScanMethod(constructor);

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                // A nested component is a component of its own, not part of the outer component's rendering.
                if (!typeof(IComponent).IsAssignableFrom(nested))
                    ScanTypeTree(CloseNested(nested, type));
            }
        }

        private void ScanMethod(MethodBase method)
        {
            byte[]? il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or BadImageFormatException or MemberAccessException)
            {
                return;
            }

            if (il is not { Length: > 0 })
                return;

            var module = method.Module;
            var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            List<Type>? loadedTypes = null;

            var position = 0;
            while (position < il.Length)
            {
                if (!s_opCodes.TryRead(il, ref position, out var opCode))
                    return;

                var operandSize = OperandSize(opCode.OperandType, il, position);
                if (operandSize < 0 || operandSize > il.Length - position)
                    return;

                if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
                {
                    var token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
                    if (ResolveMethod(module, token, typeArguments, methodArguments) is { } target)
                        OnCall(target, loadedTypes);
                }
                else if (opCode == OpCodes.Ldtoken)
                {
                    var token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position));
                    if (IsTypeToken(token) && ResolveType(module, token, typeArguments, methodArguments) is { } type && index.IsRelevant(type))
                        (loadedTypes ??= []).Add(type);
                }

                position += operandSize;
            }
        }

        private void OnCall(MethodBase target, List<Type>? loadedTypes)
        {
            if (IsOpenComponent(target))
            {
                if (target.IsGenericMethod)
                {
                    var arguments = target.GetGenericArguments();
                    if (arguments.Length == 1)
                        index.Match(arguments[0], _found);
                }
                else if (loadedTypes is { Count: > 0 } && TakesComponentType(target))
                {
                    foreach (var loaded in loadedTypes)
                        index.Match(loaded, _found);
                    loadedTypes.Clear();
                }
            }
            else if (IsRazorHelper(target.DeclaringType) && _followed.Count < MaxFollowedHelpers && _followed.Add(target))
            {
                // Razor renders generic components with inferred type arguments through generated helper methods.
                ScanMethod(target);
            }
        }

        private MethodBase? ResolveMethod(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
        {
            // Without a generic context the resolution only depends on the token, so it can be shared across components.
            var cacheable = typeArguments is null && methodArguments is null;
            if (cacheable && cache.TryGetValue((module, token), out var cached))
                return cached;

            MethodBase? resolved;
            try
            {
                resolved = module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (Exception)
            {
                resolved = null; // Unresolvable token (missing dependency, unsupported signature): ignore.
            }

            if (cacheable)
                cache[(module, token)] = resolved;
            return resolved;
        }

        private static Type? ResolveType(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
        {
            try
            {
                return module.ResolveType(token, typeArguments, methodArguments);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Decodes IL opcodes (one byte, or two bytes prefixed with <c>0xFE</c>) using <see cref="OpCodes"/>.</summary>
    private sealed class OpCodeTable
    {
        private readonly OpCode?[] _oneByte = new OpCode?[256];
        private readonly OpCode?[] _twoByte = new OpCode?[256];

        public static OpCodeTable Create()
        {
            var table = new OpCodeTable();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode code)
                    continue;

                var value = unchecked((ushort)code.Value);
                if (code.Size == 1)
                    table._oneByte[value & 0xFF] = code;
                else if (code.Size == 2 && (value >> 8) == 0xFE)
                    table._twoByte[value & 0xFF] = code;
            }

            return table;
        }

        public bool TryRead(byte[] il, ref int position, out OpCode code)
        {
            var first = il[position++];
            OpCode? found;
            if (first == 0xFE)
            {
                if (position >= il.Length)
                {
                    code = default;
                    return false;
                }

                found = _twoByte[il[position++]];
            }
            else
            {
                found = _oneByte[first];
            }

            code = found.GetValueOrDefault();
            return found.HasValue;
        }
    }
}
