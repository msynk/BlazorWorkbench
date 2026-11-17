using System.Text;
using BlazorWorkbench.Components.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace BlazorWorkbench.Components.Tests;

public class CatalogReferenceTests
{
    private static readonly Uri Base = new("http://localhost:5280/");

    private static CatalogReferenceLoader Loader(Func<Uri, string> respond, List<Uri>? requests = null, TimeProvider? time = null) =>
        new((url, _) =>
        {
            requests?.Add(url);
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(respond(url))));
        }, time);

    [Fact]
    public async Task Components_are_read_from_search_json_and_unsafe_ids_are_dropped()
    {
        var requests = new List<Uri>();
        var loader = Loader(_ => """
            [{"id":"inputs-button","title":"Inputs/Button","name":"Button","summary":"A button.","tags":["forms"],"parameters":["Variant"],"scenarios":["Primary"]},
             {"id":"../../etc","title":"Evil"},
             {"title":"No id"}]
            """, requests);

        var catalog = await loader.LoadAsync(new CatalogReference("Design system", "catalogs/ds"), Base);

        Assert.Null(catalog.Error);
        Assert.Equal(new Uri("http://localhost:5280/catalogs/ds/search.json"), requests.Single());
        var button = Assert.Single(catalog.Components);
        Assert.Equal(("Inputs/Button", "A button.", "Primary"), (button.Title, button.Summary, button.Scenarios[0]));
        Assert.Equal(new Uri("http://localhost:5280/catalogs/ds/components/inputs-button.html"), catalog.PageUrl(button.Id));
    }

    [Fact]
    public async Task Failures_are_reported_and_cached_for_a_while()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var requests = new List<Uri>();
        var loader = Loader(_ => "{ not a list", requests, time);
        var reference = new CatalogReference("Broken", "https://example.com/catalog/");

        var first = await loader.LoadAsync(reference, Base);
        Assert.StartsWith("The catalog could not be loaded", first.Error, StringComparison.Ordinal);
        Assert.Empty(first.Components);

        await loader.LoadAsync(reference, Base);
        Assert.Single(requests);
        time.Advance(TimeSpan.FromMinutes(6));
        await loader.LoadAsync(reference, Base);
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void References_are_validated_when_added()
    {
        var options = new WorkbenchOptions();
        Assert.Throws<ArgumentException>(() => options.AddCatalogReference("Files", "file:///c:/catalog"));
        options.AddCatalogReference("Design system", "https://ds.example.com/catalog");
        Assert.Throws<ArgumentException>(() => options.AddCatalogReference("design system", "https://other.example.com/"));
        Assert.Equal(new Uri("https://ds.example.com/catalog/"), options.CatalogReferences[0].Resolve(Base));
    }
}
