using BlazorWorkbench.Catalog;
using BlazorWorkbench.Core.Tests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorWorkbench.Core.Tests;

// Components for the analyzer tests. [WorkbenchIgnore] keeps them out of the catalogs other tests build by scanning this
// assembly; the tests pass them (or scenario sets for them) explicitly.

[WorkbenchIgnore]
public class RelationsLeaf : ComponentBase;

[WorkbenchIgnore]
public class RelationsOther : ComponentBase;

/// <summary>Not part of the analyzed set.</summary>
[WorkbenchIgnore]
public class RelationsOutside : ComponentBase;

[WorkbenchIgnore]
public class RelationsDirect : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<RelationsLeaf>(0);
        builder.CloseComponent();
        builder.OpenComponent<RelationsOutside>(1);
        builder.CloseComponent();
        builder.OpenComponent<CascadingValue<string>>(2);
        builder.CloseComponent();
    }
}

[WorkbenchIgnore]
public class RelationsLambda : ComponentBase
{
    [Parameter] public string Text { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var text = Text;
        RenderFragment closure = inner =>
        {
            inner.OpenComponent<RelationsLeaf>(0);
            inner.AddComponentParameter(1, nameof(Text), text);
            inner.CloseComponent();
        };
        RenderFragment cached = static inner =>
        {
            inner.OpenComponent<RelationsOther>(0);
            inner.CloseComponent();
        };
        builder.AddContent(0, closure);
        builder.AddContent(1, cached);
    }
}

[WorkbenchIgnore]
public class RelationsTypeof : ComponentBase
{
    /// <summary>A <c>typeof</c> that is not followed by <c>OpenComponent(int, Type)</c> in the same method.</summary>
    public static Type Unrendered => typeof(RelationsLeaf);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent(0, typeof(RelationsOther));
        builder.CloseComponent();
    }
}

[WorkbenchIgnore]
public abstract class RelationsBase : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<RelationsLeaf>(0);
        builder.CloseComponent();
    }
}

[WorkbenchIgnore]
public class RelationsDerived : RelationsBase;

[WorkbenchIgnore]
public class RelationsRecursive : ComponentBase
{
    [Parameter] public int Depth { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Depth > 0)
        {
            builder.OpenComponent<RelationsRecursive>(0);
            builder.AddComponentParameter(1, nameof(Depth), Depth - 1);
            builder.CloseComponent();
        }

        builder.OpenComponent<RelationsLeaf>(2);
        builder.CloseComponent();
    }
}

[WorkbenchIgnore]
public class RelationsAsync : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => _ = RenderLaterAsync(builder);

    private static async Task RenderLaterAsync(RenderTreeBuilder builder)
    {
        await Task.Yield();
        builder.OpenComponent<RelationsLeaf>(0);
        builder.CloseComponent();
    }
}

[WorkbenchIgnore]
public class RelationsContainer : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<RelationsLeaf>(0);
        builder.CloseComponent();
    }

    /// <summary>A nested component renders on its own behalf, not on behalf of its container.</summary>
    [WorkbenchIgnore]
    public class Item : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<RelationsOther>(0);
            builder.CloseComponent();
        }
    }
}

[WorkbenchIgnore]
public class RelationsGenericChild<TItem> : ComponentBase
{
    [Parameter] public TItem? Item { get; set; }
}

[WorkbenchIgnore]
public class RelationsGeneric<TItem> : ComponentBase
{
    [Parameter] public TItem? Item { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var item = Item;
        RenderFragment fragment = inner =>
        {
            inner.OpenComponent<RelationsGenericChild<TItem>>(0);
            inner.AddComponentParameter(1, nameof(Item), item);
            inner.CloseComponent();
        };
        builder.AddContent(0, fragment);
    }
}

[WorkbenchIgnore]
public class RelationsUsesClosedGeneric : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<RelationsGenericChild<string>>(0);
        builder.CloseComponent();
    }
}

public enum CoverageVariant { Primary, Secondary, Danger }

public enum CoverageLevel { Low = 1, High = 2 }

[WorkbenchIgnore]
public class CoverageButton : ComponentBase
{
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool? Pressed { get; set; }
    [Parameter] public CoverageVariant Variant { get; set; } = CoverageVariant.Secondary;
    [Parameter] public CoverageVariant? Accent { get; set; }
    [Parameter] public CoverageLevel Level { get; set; }
    [Parameter] public string? Label { get; set; }
    [Parameter] public Styles Styles { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment<int>? ItemTemplate { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter, Obsolete("Use Variant.")] public bool Legacy { get; set; }
    [CascadingParameter] public Theme? Theme { get; set; }
}

public class ComponentRelationsTests
{
    private static readonly Type[] s_components =
    [
        typeof(RelationsLeaf), typeof(RelationsOther), typeof(RelationsDirect), typeof(RelationsLambda), typeof(RelationsTypeof),
        typeof(RelationsDerived), typeof(RelationsRecursive), typeof(RelationsAsync), typeof(RelationsContainer),
        typeof(RelationsContainer.Item), typeof(RelationsGeneric<int>), typeof(RelationsGenericChild<int>),
        typeof(RelationsGenericChild<string>), typeof(RelationsUsesClosedGeneric),
    ];

    private static IReadOnlyDictionary<Type, IReadOnlyList<Type>> Uses() => ComponentRelations.FindRenderedComponents(s_components);

    [Fact]
    public void Returns_an_entry_for_every_component()
    {
        var uses = Uses();
        Assert.Equal(s_components.Length, uses.Count);
        Assert.All(s_components, t => Assert.Contains(t, uses.Keys));
        Assert.Empty(uses[typeof(RelationsLeaf)]);
    }

    [Fact]
    public void Finds_direct_open_component_calls_and_ignores_types_outside_the_set() =>
        Assert.Equal([typeof(RelationsLeaf)], Uses()[typeof(RelationsDirect)]);

    [Fact]
    public void Finds_components_rendered_in_closures_and_static_lambdas() =>
        Assert.Equal([typeof(RelationsLeaf), typeof(RelationsOther)], Uses()[typeof(RelationsLambda)]);

    [Fact]
    public void Finds_the_typeof_overload_only_when_followed_by_open_component() =>
        Assert.Equal([typeof(RelationsOther)], Uses()[typeof(RelationsTypeof)]);

    [Fact]
    public void Includes_rendering_inherited_from_base_classes() =>
        Assert.Equal([typeof(RelationsLeaf)], Uses()[typeof(RelationsDerived)]);

    [Fact]
    public void Excludes_the_component_itself() =>
        Assert.Equal([typeof(RelationsLeaf)], Uses()[typeof(RelationsRecursive)]);

    [Fact]
    public void Finds_components_rendered_in_async_state_machines() =>
        Assert.Equal([typeof(RelationsLeaf)], Uses()[typeof(RelationsAsync)]);

    [Fact]
    public void Nested_components_are_not_attributed_to_their_container()
    {
        var uses = Uses();
        Assert.Equal([typeof(RelationsLeaf)], uses[typeof(RelationsContainer)]);
        Assert.Equal([typeof(RelationsOther)], uses[typeof(RelationsContainer.Item)]);
    }

    [Fact]
    public void Generic_components_match_the_rendered_instantiation()
    {
        var uses = Uses();
        Assert.Equal([typeof(RelationsGenericChild<int>)], uses[typeof(RelationsGeneric<int>)]);
        Assert.Equal([typeof(RelationsGenericChild<string>)], uses[typeof(RelationsUsesClosedGeneric)]);
    }

    [Fact]
    public void Open_generic_references_match_on_the_generic_type_definition()
    {
        var uses = ComponentRelations.FindRenderedComponents([typeof(RelationsGeneric<>), typeof(RelationsGenericChild<int>), typeof(RelationsGenericChild<string>)]);
        var rendered = uses[typeof(RelationsGeneric<>)];
        Assert.Equal(2, rendered.Count);
        Assert.Contains(typeof(RelationsGenericChild<int>), rendered);
        Assert.Contains(typeof(RelationsGenericChild<string>), rendered);
    }

    [Fact]
    public void Results_are_sorted_and_deterministic()
    {
        var first = Uses();
        var second = ComponentRelations.FindRenderedComponents(s_components.Reverse().ToArray());
        foreach (var (type, rendered) in first)
        {
            Assert.Equal(rendered.OrderBy(t => t.FullName, StringComparer.Ordinal), rendered);
            Assert.Equal(rendered, second[type]);
        }
    }

    [Fact]
    public void Unusual_types_yield_empty_lists_instead_of_throwing()
    {
        var uses = ComponentRelations.FindRenderedComponents([typeof(int), typeof(IComponent), typeof(RelationsBase), typeof(RelationsLeaf), typeof(RelationsLeaf)]);
        Assert.Equal(4, uses.Count);
        Assert.Equal([typeof(RelationsLeaf)], uses[typeof(RelationsBase)]);
        Assert.Empty(uses[typeof(int)]);
        Assert.Empty(uses[typeof(IComponent)]);
    }

    [Fact]
    public void Invert_lists_the_components_that_render_each_component()
    {
        var usedBy = ComponentRelations.Invert(Uses());
        Assert.Equal(s_components.Length, usedBy.Count);
        Assert.Equal(
            [typeof(RelationsAsync), typeof(RelationsContainer), typeof(RelationsDerived), typeof(RelationsDirect), typeof(RelationsLambda), typeof(RelationsRecursive)],
            usedBy[typeof(RelationsLeaf)]);
        Assert.Equal([typeof(RelationsContainer.Item), typeof(RelationsLambda), typeof(RelationsTypeof)], usedBy[typeof(RelationsOther)]);
        Assert.Equal([typeof(RelationsGeneric<int>)], usedBy[typeof(RelationsGenericChild<int>)]);
        Assert.Empty(usedBy[typeof(RelationsDirect)]);
    }

    [Fact]
    public void Invert_adds_entries_for_types_that_only_appear_as_values()
    {
        var usedBy = ComponentRelations.Invert(new Dictionary<Type, IReadOnlyList<Type>> { [typeof(RelationsDirect)] = [typeof(RelationsLeaf)] });
        Assert.Equal([typeof(RelationsDirect)], usedBy[typeof(RelationsLeaf)]);
        Assert.Empty(usedBy[typeof(RelationsDirect)]);
    }
}

public class ParameterCoverageTests
{
    private sealed class CoverageScenarios : ScenarioSet<CoverageButton>
    {
        protected override void Configure(ScenarioSetBuilder<CoverageButton> set)
        {
            set.Add("Default");
            set.Add("Disabled primary", s => s.Parameter(c => c.Disabled, true).Parameter(c => c.Variant, CoverageVariant.Primary).Parameter(c => c.Label, "Save"));
            set.Add("Pressed", s => s.Parameter(c => c.Pressed, true).ChildContent("Press me"));
            set.Add("Computed variant", s => s.ParameterFrom(c => c.Variant, _ => CoverageVariant.Danger));
        }
    }

    private static ComponentEntry Build(bool readDefaults = true)
    {
        var options = new WorkbenchOptions { ReadParameterDefaults = readDefaults };
        options.AddScenarios(new CoverageScenarios());
        return CatalogBuilder.Build(options).FindComponent(typeof(CoverageButton))!;
    }

    private static Dictionary<string, ParameterCoverage> Analyze(bool readDefaults = true) =>
        ParameterCoverageAnalyzer.Analyze(Build(readDefaults)).ToDictionary(c => c.Parameter);

    [Fact]
    public void Lists_supported_parameters_in_metadata_order()
    {
        var component = Build();
        var coverage = ParameterCoverageAnalyzer.Analyze(component);
        Assert.Equal(["Accent", "ChildContent", "Disabled", "ItemTemplate", "Label", "Level", "Pressed", "Styles", "Variant"], coverage.Select(c => c.Parameter));
        Assert.Equal(
            component.Metadata.Parameters.Select(p => p.Name).Where(n => coverage.Any(c => c.Parameter == n)),
            coverage.Select(c => c.Parameter));
    }

    [Fact]
    public void Bool_covers_assigned_values_and_defaults()
    {
        var disabled = Analyze()["Disabled"];
        Assert.Equal(ParameterCoverageKind.Values, disabled.Kind);
        Assert.Equal("bool", disabled.TypeName);
        Assert.Equal(["true", "false"], disabled.Covered);
        Assert.Empty(disabled.Missing);
        Assert.True(disabled.IsComplete);
        Assert.Equal(1, disabled.ScenariosSettingIt);
    }

    [Fact]
    public void Nullable_bool_includes_null()
    {
        var pressed = Analyze()["Pressed"];
        Assert.Equal(["true", "null"], pressed.Covered);
        Assert.Equal(["false"], pressed.Missing);
        Assert.False(pressed.IsComplete);
    }

    [Fact]
    public void Enum_uses_member_names_and_factories_count_as_set_without_a_value()
    {
        var variant = Analyze()["Variant"];
        Assert.Equal(["Primary", "Secondary"], variant.Covered); // Secondary is the component default.
        Assert.Equal(["Danger"], variant.Missing); // Only produced by a factory.
        Assert.Equal(2, variant.ScenariosSettingIt);
    }

    [Fact]
    public void Nullable_enum_appends_null()
    {
        var accent = Analyze()["Accent"];
        Assert.Equal("CoverageVariant?", accent.TypeName);
        Assert.Equal(["null"], accent.Covered);
        Assert.Equal(["Primary", "Secondary", "Danger"], accent.Missing);
        Assert.Equal(0, accent.ScenariosSettingIt);
    }

    [Fact]
    public void Undeclared_zero_enum_default_covers_nothing()
    {
        var level = Analyze()["Level"];
        Assert.Empty(level.Covered);
        Assert.Equal(["Low", "High"], level.Missing);
    }

    [Fact]
    public void Default_of_T_is_used_when_defaults_are_not_read()
    {
        var coverage = Analyze(readDefaults: false);
        Assert.Equal(["Primary"], coverage["Variant"].Covered);
        Assert.Equal(["Secondary", "Danger"], coverage["Variant"].Missing);
        Assert.Equal(["true", "false"], coverage["Disabled"].Covered);
        Assert.Equal(["true", "null"], coverage["Pressed"].Covered);
    }

    [Fact]
    public void Other_types_are_measured_by_presence()
    {
        var coverage = Analyze();
        foreach (var (name, setting) in new[] { ("Label", 1), ("ChildContent", 1), ("Styles", 0), ("ItemTemplate", 0) })
        {
            Assert.Equal(ParameterCoverageKind.Presence, coverage[name].Kind);
            Assert.Empty(coverage[name].Covered);
            Assert.Empty(coverage[name].Missing);
            Assert.Equal(setting, coverage[name].ScenariosSettingIt);
        }
    }

    [Fact]
    public void Skips_callbacks_cascading_and_obsolete_parameters()
    {
        var coverage = Analyze();
        Assert.DoesNotContain("OnClick", coverage.Keys);
        Assert.DoesNotContain("Theme", coverage.Keys);
        Assert.DoesNotContain("Legacy", coverage.Keys);
    }

    [Fact]
    public void Components_with_only_generated_scenarios_have_no_coverage()
    {
        var options = new WorkbenchOptions();
        options.AddComponent<CoverageButton>();
        var component = CatalogBuilder.Build(options).FindComponent(typeof(CoverageButton))!;
        Assert.True(component.HasOnlyGeneratedScenarios);
        Assert.Empty(ParameterCoverageAnalyzer.Analyze(component));
    }

    [Fact]
    public void Ratio_counts_values_and_presence_items()
    {
        // Values: Disabled 2/2, Pressed 2/3, Variant 2/3, Accent 1/4, Level 0/2. Presence: Label and ChildContent set, Styles and ItemTemplate not.
        Assert.Equal(9d / 18d, ParameterCoverageAnalyzer.Ratio(ParameterCoverageAnalyzer.Analyze(Build())), 6);
        Assert.Equal(1d, ParameterCoverageAnalyzer.Ratio([]));
        Assert.Equal(0.5, ParameterCoverageAnalyzer.Ratio(
        [
            new ParameterCoverage("A", "bool", ParameterCoverageKind.Values, ["true"], ["false"], 1),
            new ParameterCoverage("B", "string", ParameterCoverageKind.Presence, [], [], 2),
            new ParameterCoverage("C", "string", ParameterCoverageKind.Presence, [], [], 0),
        ]));
    }
}
