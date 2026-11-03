using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Generates large synthetic component libraries for scale tests and benchmarks.</summary>
public static class SyntheticLibrary
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<(int, int), Assembly> Cache = [];

    public static Assembly Create(int components, int parametersPerComponent)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue((components, parametersPerComponent), out var cached))
                return cached;

            var name = $"Generated.Components{components}x{parametersPerComponent}";
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule(name);
            var parameterAttribute = new CustomAttributeBuilder(typeof(ParameterAttribute).GetConstructor(Type.EmptyTypes)!, []);
            Type[] types = [typeof(string), typeof(int), typeof(bool), typeof(double?)];

            for (var i = 0; i < components; i++)
            {
                var type = module.DefineType($"{name}.Group{i % 25}.Component{i}", TypeAttributes.Public | TypeAttributes.Class, typeof(ComponentBase));
                for (var p = 0; p < parametersPerComponent; p++)
                    DefineProperty(type, $"Parameter{p}", types[p % types.Length], parameterAttribute);
                type.DefineDefaultConstructor(MethodAttributes.Public);
                type.CreateType();
            }

            return Cache[(components, parametersPerComponent)] = assembly;
        }
    }

    private static void DefineProperty(TypeBuilder type, string name, Type propertyType, CustomAttributeBuilder attribute)
    {
        var field = type.DefineField("_" + name, propertyType, FieldAttributes.Private);
        var property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
        const MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = type.DefineMethod("get_" + name, accessor, propertyType, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);

        var setter = type.DefineMethod("set_" + name, accessor, null, [propertyType]);
        il = setter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        property.SetCustomAttribute(attribute);
    }
}
