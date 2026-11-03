using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorWorkbench.Core.Tests.Components;

public enum Tone { Neutral, Danger }

[Flags]
public enum Styles { None = 0, Bold = 1, Italic = 2 }

/// <summary>A documented test button.</summary>
public class TestButton : ComponentBase
{
    /// <summary>The label.</summary>
    [Parameter, EditorRequired] public string Label { get; set; } = "";

    [Parameter] public Tone Tone { get; set; } = Tone.Neutral;
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public int? Count { get; set; }
    [Parameter] public Styles Styles { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback<int> OnClick { get; set; }
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public List<string>? Items { get; set; }
    [CascadingParameter] public Theme? Theme { get; set; }
    [Inject] public IGreeter Greeter { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Label);
}

public sealed record Theme(string Name);

public interface IGreeter
{
    string Greet(string name);
}

public sealed class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello {name}";
}

public class Generic<TItem> : ComponentBase
{
    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }
    [Parameter] public RenderFragment<TItem>? Template { get; set; }
}

public class PlainComponent : ComponentBase
{
    [Parameter] public string Text { get; set; } = "plain";
}

[Route("/page")]
public class RoutablePage : ComponentBase;

[WorkbenchIgnore]
public class IgnoredComponent : ComponentBase;

public class TestButtonScenarios : ScenarioSet<TestButton>
{
    protected override void Configure(ScenarioSetBuilder<TestButton> set)
    {
        set.Title("Inputs/Test button").Tags("inputs");
        set.Fixture<List<string>>("Three", _ => ["a", "b", "c"]);
        set.Setup(s => s.Service<IGreeter, Greeter>().Parameter(b => b.Label, "Save"));
        set.Add("Primary", s => s.Interaction("click", i => i.Click(By.Role("button", "Save")).ExpectCallback("OnClick", 1)));
        set.Add("Danger", s => s.Parameter(b => b.Tone, Tone.Danger).Cascade(new Theme("dark")));
        set.Add("With items", s => s.Fixture(b => b.Items, "Three"));
    }
}

public class GenericScenarios : ScenarioSet<Generic<int>>
{
    protected override void Configure(ScenarioSetBuilder<Generic<int>> set) =>
        set.Add("Numbers", s => s.Parameter(g => g.Items, (IReadOnlyList<int>)[1, 2, 3]));
}
