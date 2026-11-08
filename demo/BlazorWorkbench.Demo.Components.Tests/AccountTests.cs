using Bunit;
using BlazorWorkbench.Testing.Bunit;
using BlazorWorkbench.Demo.Scenarios;

namespace BlazorWorkbench.Demo.Components.Tests;

/// <summary>Simulated users against real authorization policies, and time travel with the scenario's fake clock.</summary>
public class AccountTests : DemoTestContext
{
    [Theory]
    [InlineData("viewer", false, false, false)]
    [InlineData("editor", false, false, false)]
    [InlineData("admin", true, false, false)]
    [InlineData("owner-on-pro", true, true, true)]
    [InlineData("billing-contact", false, true, false)]
    public void Policies_decide_what_each_user_can_do(string scenario, bool invite, bool billing, bool delete)
    {
        var cut = this.RenderScenario<WorkspaceSettingsScenarios>(scenario);

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".settings-section .badge"), b => b.TextContent is "Pro" or "Free"));
        var buttons = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(invite, buttons.Contains("Invite people"));
        Assert.Equal(billing, buttons.Contains("Manage billing"));
        Assert.Equal(delete, buttons.Contains("Delete workspace"));
    }

    [Fact]
    public void Fifteen_idle_minutes_pass_in_milliseconds()
    {
        var cut = this.RenderScenario<SessionTimeoutScenarios>("active");
        var clock = cut.Session().Clock!;
        Assert.Contains("Session active", cut.Markup);

        clock.Advance(TimeSpan.FromMinutes(8));
        cut.WaitForAssertion(() => Assert.Equal("2:00", cut.Find("[data-testid=countdown]").TextContent));

        clock.Advance(TimeSpan.FromSeconds(30));
        cut.WaitForAssertion(() => Assert.Equal("1:30", cut.Find("[data-testid=countdown]").TextContent));

        clock.Advance(TimeSpan.FromMinutes(2));
        cut.WaitForAssertion(() => Assert.Contains("You have been signed out", cut.Markup));
        Assert.Equal(1, cut.CallbackCount("OnSignedOut"));
    }

    [Fact]
    public void The_flash_sale_ends_when_the_clock_passes_its_end()
    {
        var cut = this.RenderScenario<FlashSaleScenarios>("last-minute");
        Assert.Contains("45", cut.Find("[data-testid=countdown]").TextContent);

        cut.Session().Clock!.Advance(TimeSpan.FromSeconds(45));

        cut.WaitForAssertion(() => Assert.Contains("This sale ended", cut.Markup));
    }
}
