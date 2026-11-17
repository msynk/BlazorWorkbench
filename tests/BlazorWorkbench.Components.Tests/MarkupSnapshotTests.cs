using System.Globalization;
using BlazorWorkbench.Demo.Scenarios;
using BlazorWorkbench.Testing.Bunit;
using Bunit;

namespace BlazorWorkbench.Components.Tests;

public sealed class MarkupSnapshotTests : BunitContext, IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wb-snapshots-" + Guid.NewGuid().ToString("N"));

    public MarkupSnapshotTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void A_missing_snapshot_fails_and_writes_the_received_markup_which_then_verifies()
    {
        var cut = this.RenderScenario<ButtonScenarios>("primary");

        var missing = Assert.Throws<ScenarioAssertionException>(() => cut.MatchMarkupSnapshot(_folder));
        Assert.Contains("no markup snapshot yet", missing.Message, StringComparison.Ordinal);
        var received = Path.Combine(_folder, "inputs-button--primary.received.html");
        var markup = File.ReadAllText(received);
        Assert.Contains("<button", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor:", markup, StringComparison.Ordinal);

        File.Move(received, Path.Combine(_folder, "inputs-button--primary.verified.html"));
        cut.MatchMarkupSnapshot(_folder);
        Assert.False(File.Exists(received));
    }

    [Fact]
    public void A_changed_rendering_fails_with_the_first_different_line()
    {
        var cut = this.RenderScenario<ButtonScenarios>("primary");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "inputs-button--primary.verified.html"), MarkupSnapshots.Normalize(cut).Replace("Save changes", "Save", StringComparison.Ordinal));

        var changed = Assert.Throws<ScenarioAssertionException>(() => cut.MatchMarkupSnapshot(_folder));
        Assert.Contains("expected:", changed.Message, StringComparison.Ordinal);
        Assert.Contains("Save changes", changed.Message, StringComparison.Ordinal);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
