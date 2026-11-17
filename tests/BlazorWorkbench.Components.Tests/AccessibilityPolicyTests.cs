using BlazorWorkbench.Catalog;
using BlazorWorkbench.Components.Runtime;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Demo.Components.Inputs;
using BlazorWorkbench.Demo.Scenarios;
using BlazorWorkbench.Hosting;

namespace BlazorWorkbench.Components.Tests;

public sealed class AccessibilityPolicyTests : IDisposable
{
    private readonly string _baselines = Path.Combine(Path.GetTempPath(), "wb-policy-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task The_host_supplies_the_fail_threshold_tags_and_known_violations_of_catalog_scenarios()
    {
        var options = new WorkbenchHostOptions { BaselineDirectory = _baselines, AccessibilityFailOn = "moderate" };
        options.AccessibilityTags.Add("wcag2a");
        options.AddAssemblyOf<ButtonScenarios>().AddAssemblyOf<Button>();
        var catalogs = new WorkbenchCatalogAccessor(options, CatalogBuilder.Build(options));
        AccessibilityBaseline.Save(_baselines, "inputs-button--primary", [new AccessibilityFinding("button-name", "serious", "", null, [], ["button"], true)]);
        AccessibilityBaseline.Save(_baselines, "not-in-the-catalog", [new AccessibilityFinding("x", "serious", "", null, [], ["y"], true)]);

        var policy = await new FileAccessibilityPolicyProvider(options, catalogs).GetAsync(["inputs-button--primary", "inputs-button--danger", "not-in-the-catalog"]);

        Assert.Equal("moderate", policy.FailOn);
        Assert.Equal(["wcag2a"], policy.Tags);
        var known = Assert.Single(policy.Known);
        Assert.Equal("inputs-button--primary", known.Key);
        Assert.Equal(new AccessibilityBaseline.KnownViolation("button-name", "button"), Assert.Single(known.Value));
    }

    public void Dispose()
    {
        if (Directory.Exists(_baselines))
            Directory.Delete(_baselines, recursive: true);
    }
}
