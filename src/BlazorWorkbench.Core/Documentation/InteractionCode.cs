using System.Globalization;
using System.Text;

namespace BlazorWorkbench.Documentation;

/// <summary>Writes interaction steps as C# for a scenario (used by the interaction recorder).</summary>
public static class InteractionCode
{
    /// <summary>Returns <c>.Interaction("name", i => i.Click(...)...)</c> with one step per line.</summary>
    public static string ToCSharp(string name, IEnumerable<InteractionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var sb = new StringBuilder();
        sb.Append(".Interaction(").Append(ScenarioExporter.Literal(name)).Append(", i => i");
        var tabs = 0;
        foreach (var step in steps)
        {
            // Consecutive Tab presses read better as Tab(n).
            if (step is { Action: StepAction.Press, Target: null, Value: "Tab" })
            {
                tabs++;
                continue;
            }

            FlushTabs();
            sb.Append("\n    ").Append(Step(step));
        }

        FlushTabs();
        sb.Append(')');
        return sb.ToString();

        void FlushTabs()
        {
            if (tabs == 0) return;
            sb.Append("\n    ").Append(tabs == 1 ? ".Tab()" : $".Tab({tabs.ToString(CultureInfo.InvariantCulture)})");
            tabs = 0;
        }
    }

    /// <summary>One builder call, such as <c>.Fill(By.Label("Email"), "ada@example.com")</c>.</summary>
    public static string Step(InteractionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var target = step.Target is null ? null : Locator(step.Target);
        var value = step.Value is null ? null : ScenarioExporter.Literal(step.Value);
        var expected = step.Expected is null ? null : ScenarioExporter.Literal(step.Expected);
        return step.Action switch
        {
            StepAction.Fill or StepAction.Type or StepAction.Select => $".{step.Action}({target}, {value})",
            StepAction.Press => target is null ? $".Press({value})" : $".Press({value}, {target})",
            StepAction.ExpectText or StepAction.ExpectValue => $".{step.Action}({target}, {expected})",
            StepAction.ExpectAttribute => $".ExpectAttribute({target}, {value}, {expected})",
            StepAction.ExpectCount => $".ExpectCount({target}, {(step.Count ?? 0).ToString(CultureInfo.InvariantCulture)})",
            StepAction.ExpectCallback => step.Count is { } times ? $".ExpectCallback({value}, times: {times.ToString(CultureInfo.InvariantCulture)})" : $".ExpectCallback({value})",
            StepAction.ExpectNoErrors => ".ExpectNoErrors()",
            StepAction.ReleaseGate or StepAction.FailGate or StepAction.Checkpoint => $".{step.Action}({value})",
            StepAction.DragTo => $".DragTo({target}, {(step.Destination is null ? "null" : Locator(step.Destination))})",
            StepAction.Upload => $"// Upload of {step.Value} is not recorded; add .Upload({target}, \"file\", content)",
            _ => $".{step.Action}({target})",
        };
    }

    /// <summary>A <c>By.*</c> expression for <paramref name="locator"/>.</summary>
    public static string Locator(Locator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var value = ScenarioExporter.Literal(locator.Value);
        var exact = locator.Exact ? ", exact: true" : "";
        return locator.Kind switch
        {
            LocatorKind.Role when locator.Name is not null => $"By.Role({value}, {ScenarioExporter.Literal(locator.Name)}{exact})",
            LocatorKind.Role => $"By.Role({value})",
            LocatorKind.TestId => $"By.TestId({value})",
            LocatorKind.Css => $"By.Css({value})",
            _ => $"By.{locator.Kind}({value}{exact})",
        };
    }
}
