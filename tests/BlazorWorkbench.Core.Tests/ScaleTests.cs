using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Core.Tests;

/// <summary>Scale characteristics of discovery, validation, filtering and serialization (budgets are generous for CI).</summary>
public class ScaleTests
{
    [Fact]
    public void Discovers_a_thousand_components_quickly_and_deterministically()
    {
        var assembly = SyntheticLibrary.Create(1000, 10);
        var options = new WorkbenchOptions();
        options.AddAssembly(assembly);

        var watch = Stopwatch.StartNew();
        var catalog = CatalogBuilder.Build(options);
        var build = watch.Elapsed;

        Assert.Equal(1000, catalog.Components.Count);
        Assert.Equal(10, catalog.Components[0].Metadata.Parameters.Count);
        Assert.Equal(25, catalog.Components.Select(c => c.Path[0]).Distinct().Count());

        watch.Restart();
        var filtered = catalog.Filter("group1-*");
        var filter = watch.Elapsed;

        watch.Restart();
        var json = System.Text.Json.JsonSerializer.Serialize(CatalogMapper.ToDto(catalog), WorkbenchJson.Options);
        var serialize = watch.Elapsed;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"1000 components × 10 parameters: build {build.TotalMilliseconds:0} ms, filter {filter.TotalMilliseconds:0.0} ms, catalog JSON {json.Length / 1024} KB in {serialize.TotalMilliseconds:0} ms");

        Assert.NotEmpty(filtered);
        Assert.True(build < TimeSpan.FromSeconds(15), $"Catalog build took {build}.");
        Assert.True(filter < TimeSpan.FromSeconds(1), $"Filtering took {filter}.");

        var again = CatalogBuilder.Build(options);
        Assert.Equal(catalog.Scenarios.Select(s => s.Id), again.Scenarios.Select(s => s.Id));
    }
}
