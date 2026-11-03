using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench;

/// <summary>
/// A discoverable group of scenarios for a single component type. Implemented by <see cref="ScenarioSet{TComponent}"/>;
/// implement it directly only when the component type is not known at compile time.
/// </summary>
public interface IScenarioSet
{
    /// <summary>The component type the scenarios render.</summary>
    Type ComponentType { get; }

    /// <summary>Builds the scenario definitions. Called once per catalog build.</summary>
    ScenarioSetDefinition Build();
}

/// <summary>
/// Base class for authoring scenarios in C#. Derive from it in any assembly registered with Workbench:
/// <code>
/// public sealed class ButtonScenarios : ScenarioSet&lt;Button&gt;
/// {
///     protected override void Configure(ScenarioSetBuilder&lt;Button&gt; set)
///     {
///         set.Add("Primary", s => s.Parameter(b => b.Variant, ButtonVariant.Primary).ChildContent("Save"));
///     }
/// }
/// </code>
/// </summary>
/// <typeparam name="TComponent">The component (closed generic types are supported).</typeparam>
public abstract class ScenarioSet<TComponent> : IScenarioSet where TComponent : IComponent
{
    /// <inheritdoc />
    public Type ComponentType => typeof(TComponent);

    /// <summary>Declares the scenarios, shared setup, fixtures and documentation for <typeparamref name="TComponent"/>.</summary>
    protected abstract void Configure(ScenarioSetBuilder<TComponent> set);

    /// <inheritdoc />
    public ScenarioSetDefinition Build()
    {
        var builder = new ScenarioSetBuilder<TComponent>(GetType());
        Configure(builder);
        return builder.Build();
    }
}

/// <summary>
/// Supplies scenarios from a custom source (for example generated, data-driven or third-party definitions).
/// Providers in registered assemblies with a public parameterless constructor are discovered automatically;
/// others can be added through <see cref="WorkbenchOptions.ScenarioProviders"/>.
/// </summary>
public interface IScenarioProvider
{
    /// <summary>Returns scenario sets. Exceptions are reported as catalog issues and do not stop discovery.</summary>
    IEnumerable<ScenarioSetDefinition> GetScenarioSets();
}

/// <summary>Excludes a component or scenario set from automatic discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WorkbenchIgnoreAttribute : Attribute;
