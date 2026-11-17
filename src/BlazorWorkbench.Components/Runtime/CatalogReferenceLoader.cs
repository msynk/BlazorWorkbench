using System.Collections.Concurrent;
using System.Text.Json;

namespace BlazorWorkbench.Components.Runtime;

/// <summary>A component of a referenced catalog, as its <c>search.json</c> lists it.</summary>
public sealed record ReferencedComponent(string Id, string Title, string Name, string? Summary, IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parameters, IReadOnlyList<string> Scenarios);

/// <summary>A referenced catalog after loading: its components, or why it could not be loaded.</summary>
public sealed record ReferencedCatalog(CatalogReference Reference, Uri Folder, IReadOnlyList<ReferencedComponent> Components, string? Error)
{
    /// <summary>The published page of a component.</summary>
    public Uri PageUrl(string componentId) => new(Folder, "components/" + Uri.EscapeDataString(componentId) + ".html");
}

/// <summary>
/// Loads the <c>search.json</c> of the published catalogs in <see cref="WorkbenchOptions.CatalogReferences"/>. Results are
/// cached for a few minutes. Only the index is read, never scripts; it is limited in size and number of components, and
/// a failure is reported in the explorer instead of breaking it. In WebAssembly hosts the browser fetches the index, so a
/// catalog on another origin must allow it with CORS (static hosts such as GitHub Pages do).
/// </summary>
public sealed class CatalogReferenceLoader
{
    /// <summary>Largest <c>search.json</c> read, in bytes.</summary>
    public const int MaxIndexBytes = 4 * 1024 * 1024;

    /// <summary>Most components read from one catalog.</summary>
    public const int MaxComponents = 5000;

    private static readonly TimeSpan s_cacheFor = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, Task<ReferencedCatalog> Load)> _cache = new(StringComparer.Ordinal);
    private readonly Func<Uri, CancellationToken, Task<Stream>> _open;
    private readonly TimeProvider _time;

    public CatalogReferenceLoader() : this(null, null)
    {
    }

    /// <param name="open">Opens the index stream of a URL (tests replace it); defaults to an HTTP GET with a 10 second timeout.</param>
    /// <param name="time">The clock of the cache.</param>
    public CatalogReferenceLoader(Func<Uri, CancellationToken, Task<Stream>>? open, TimeProvider? time)
    {
        _open = open ?? OpenAsync;
        _time = time ?? TimeProvider.System;
    }

    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<Stream> OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await s_http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxIndexBytes)
            throw new InvalidOperationException($"search.json is larger than {MaxIndexBytes / (1024 * 1024)} MB.");
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads every reference of <paramref name="options"/>; relative URLs resolve against <paramref name="baseUri"/>.</summary>
    public async Task<IReadOnlyList<ReferencedCatalog>> LoadAllAsync(WorkbenchOptions options, Uri baseUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return await Task.WhenAll(options.CatalogReferences.Select(r => LoadAsync(r, baseUri, cancellationToken))).ConfigureAwait(false);
    }

    /// <summary>Loads one reference (cached for five minutes; failures are cached too, so a dead link is not hammered).</summary>
    public Task<ReferencedCatalog> LoadAsync(CatalogReference reference, Uri baseUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Uri folder;
        try
        {
            folder = reference.Resolve(baseUri);
        }
        catch (UriFormatException ex)
        {
            return Task.FromResult(new ReferencedCatalog(reference, baseUri, [], $"The URL '{reference.Url}' is not valid: {ex.Message}"));
        }

        var key = folder.AbsoluteUri;
        var now = _time.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && now - cached.At < s_cacheFor)
            return cached.Load;
        var load = LoadCoreAsync(reference, folder, cancellationToken);
        _cache[key] = (now, load);
        return load;
    }

    private async Task<ReferencedCatalog> LoadCoreAsync(CatalogReference reference, Uri folder, CancellationToken cancellationToken)
    {
        if (folder.Scheme is not ("http" or "https"))
            return new ReferencedCatalog(reference, folder, [], "Only http(s) catalogs can be referenced.");
        try
        {
            await using var stream = await _open(new Uri(folder, "search.json"), cancellationToken).ConfigureAwait(false);
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (limited.Length + read > MaxIndexBytes)
                    throw new InvalidOperationException($"search.json is larger than {MaxIndexBytes / (1024 * 1024)} MB.");
                limited.Write(buffer, 0, read);
            }

            limited.Position = 0;
            using var doc = await JsonDocument.ParseAsync(limited, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("search.json is not a list of components.");
            var components = new List<ReferencedComponent>();
            foreach (var item in doc.RootElement.EnumerateArray().Take(MaxComponents))
            {
                if (item.ValueKind != JsonValueKind.Object || Text(item, "id") is not { Length: > 0 } id || !IsSafeId(id))
                    continue;
                components.Add(new ReferencedComponent(id, Text(item, "title") ?? id, Text(item, "name") ?? id, Text(item, "summary"),
                    List(item, "tags"), List(item, "parameters"), List(item, "scenarios")));
            }

            return new ReferencedCatalog(reference, folder, components.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToArray(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException or TaskCanceledException or NotSupportedException)
        {
            return new ReferencedCatalog(reference, folder, [], $"The catalog could not be loaded: {ex.Message}");
        }
    }

    // Ids become URL segments of the Workbench and of the referenced site.
    private static bool IsSafeId(string id) => id.Length <= 200 && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> List(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Take(200).ToArray()
            : [];
}
