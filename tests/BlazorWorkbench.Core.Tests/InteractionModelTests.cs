using System.Text.Json;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Core.Tests.Components;

namespace BlazorWorkbench.Core.Tests;

public class InteractionModelTests
{
    [Fact]
    public void New_steps_round_trip_through_json()
    {
        var builder = new InteractionBuilder()
            .DragTo(By.Text("A"), By.Text("B"))
            .Upload(By.Label("File"), "a.txt", "hello")
            .ScrollIntoView(By.TestId("x"))
            .ExpectEnabled(By.Role("button", "Save"))
            .ExpectDisabled(By.Role("button", "Undo"))
            .ExpectChecked(By.Label("Agree"))
            .ExpectUnchecked(By.Label("Spam"));
        var test = new InteractionTest("t", builder.Steps.ToArray());

        var back = JsonSerializer.Deserialize<InteractionTest>(JsonSerializer.Serialize(test, WorkbenchJson.Options), WorkbenchJson.Options)!;

        Assert.Equal(StepAction.DragTo, back.Steps[0].Action);
        Assert.Equal("B", back.Steps[0].Destination!.Value);
        Assert.Equal("aGVsbG8=", back.Steps[1].FileContent);
        Assert.Equal("text/plain", back.Steps[1].ContentType);
        Assert.Equal(StepAction.ExpectUnchecked, back.Steps[^1].Action);
        Assert.Contains("→ text=B", back.Steps[0].ToString());
    }

    [Fact]
    public void Validator_requires_destination_and_file_content()
    {
        var options = new WorkbenchOptions();
        options.AddScenarios(new StepSet());
        var issues = CatalogBuilder.Build(options).Issues.Where(i => i.Code == "WB1012").Select(i => i.Message).ToList();
        Assert.Contains(issues, m => m.Contains("destination", StringComparison.Ordinal));
        Assert.Contains(issues, m => m.Contains("file name and content", StringComparison.Ordinal));
    }

    private sealed class StepSet : ScenarioSet<PlainComponent>
    {
        protected override void Configure(ScenarioSetBuilder<PlainComponent> set) =>
            set.Add("Bad steps", s => s
                .Interaction("drag", i => i.DragTo(By.Text("A"), By.Text(" ")))
                .Interaction("upload", i => i.Upload(By.Label("File"), "", "x")));
    }
}
