using System.Runtime.InteropServices;
using System.Text.Json;
using BlazorWorkbench.Contracts;
using BlazorWorkbench.Testing.Imaging;

namespace BlazorWorkbench.Testing;

/// <summary>
/// Approved screenshots, stored as <c>{platform}/{scenarioId}/{variant}.png</c> plus <c>manifest.json</c> with capture
/// metadata. Rendering differs between operating systems (fonts, antialiasing), so each platform has its own baselines.
/// Baselines change only through <see cref="Approve"/>; test runs never overwrite them.
/// </summary>
public sealed class BaselineStore(string directory, string? platform = null)
{
    public const string ManifestFile = "manifest.json";

    /// <summary>Root directory (contains one folder per platform and the manifest).</summary>
    public string Directory { get; } = Path.GetFullPath(directory);

    public string Platform { get; } = platform ?? CurrentPlatform;

    public static string CurrentPlatform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>
    /// Platform folder for a hosting model: rendering can differ between Server and WebAssembly (globalization data,
    /// exception messages) and in Blazor Hybrid apps (the platform WebView renders text differently from Playwright's Chromium).
    /// </summary>
    public static string PlatformFor(string? hostingModel) => hostingModel?.ToLowerInvariant() switch
    {
        "webassembly" => CurrentPlatform + "-webassembly",
        "hybrid" => CurrentPlatform + "-hybrid",
        _ => CurrentPlatform,
    };

    /// <summary>Path relative to <see cref="Directory"/> (includes the platform folder).</summary>
    public string RelativePath(string scenarioId, string variant) => $"{Platform}/{Sanitize(scenarioId)}/{Sanitize(variant)}.png";

    /// <summary>Path relative to the platform folder (used for screenshots in reports).</summary>
    public static string ScenarioPath(string scenarioId, string variant) => $"{Sanitize(scenarioId)}/{Sanitize(variant)}.png";

    public string FullPath(string scenarioId, string variant) => Path.Combine(Directory, Platform, Sanitize(scenarioId), Sanitize(variant) + ".png");

    public PngImage? TryLoad(string scenarioId, string variant)
    {
        var path = FullPath(scenarioId, variant);
        return File.Exists(path) ? PngImage.Load(path) : null;
    }

    public BaselineManifest LoadManifest()
    {
        var path = Path.Combine(Directory, ManifestFile);
        if (!File.Exists(path))
            return new BaselineManifest();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<BaselineManifest>(stream, WorkbenchJson.Options) ?? new BaselineManifest();
    }

    public void SaveManifest(BaselineManifest manifest)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, ManifestFile), JsonSerializer.Serialize(manifest, WorkbenchJson.Indented) + "\n");
    }

    /// <summary>
    /// Copies the actual screenshots of failed or missing visual checks from <paramref name="report"/> into the baseline
    /// directory. Returns the approved entries. Only checks matching <paramref name="filter"/> are approved.
    /// </summary>
    public IReadOnlyList<BaselineEntry> Approve(TestRunReport report, string reportDirectory, Func<string, bool> filter, bool includePassed = false) =>
        BaselineApproval.Approve(report, reportDirectory, Directory, filter, includePassed, platform: Platform);

    /// <summary>Removes baseline files whose scenario no longer exists. Returns removed relative paths.</summary>
    public IReadOnlyList<string> Prune(IReadOnlySet<string> scenarioIds)
    {
        var removed = new List<string>();
        var platformDirectory = Path.Combine(Directory, Platform);
        if (!System.IO.Directory.Exists(platformDirectory))
            return removed;
        var manifest = LoadManifest();
        // Folders are named after the sanitized scenario id.
        var keep = scenarioIds.Select(Sanitize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in System.IO.Directory.EnumerateDirectories(platformDirectory))
        {
            var id = Path.GetFileName(dir);
            if (keep.Contains(id))
                continue;
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.png"))
                removed.Add($"{Platform}/{id}/{Path.GetFileName(file)}");
            System.IO.Directory.Delete(dir, recursive: true);
        }

        foreach (var key in manifest.Entries.Keys.Where(k => removed.Contains(k)).ToList())
            manifest.Entries.Remove(key);
        if (removed.Count > 0)
            SaveManifest(manifest);
        return removed;
    }

    internal static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray());
    }

    internal static string CurrentOs => $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture})";
}
