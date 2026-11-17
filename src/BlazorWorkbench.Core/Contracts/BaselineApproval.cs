using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace BlazorWorkbench.Contracts;

/// <summary>
/// Approves screenshots of a test run as visual baselines: copies them to <c>{baselines}/{platform}/{scenarioId}/{variant}.png</c>
/// and records them in <c>manifest.json</c>. Shared by <c>workbench approve</c>, the MCP tool and the Visual panel.
/// </summary>
public static class BaselineApproval
{
    public const string ManifestFile = "manifest.json";

    /// <summary>The platform of the current operating system (<c>windows</c>, <c>macos</c> or <c>linux</c>).</summary>
    public static string CurrentPlatform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>
    /// Copies the actual screenshots of failed or missing visual checks from <paramref name="report"/> into
    /// <paramref name="baselineDirectory"/> and returns the approved entries. Screenshots belong to the platform that captured
    /// them (<see cref="RunEnvironment.Platform"/>), whatever platform approves them.
    /// </summary>
    /// <param name="report">The test run.</param>
    /// <param name="reportDirectory">The directory of <c>report.json</c>; actual screenshot paths are relative to it.</param>
    /// <param name="baselineDirectory">The baseline root directory.</param>
    /// <param name="scenarioFilter">Scenario ids to approve.</param>
    /// <param name="includePassed">Also re-approve checks that passed.</param>
    /// <param name="variant">Only this variant (viewport and theme), or every variant when null.</param>
    /// <param name="platform">The platform folder when the report does not name one; defaults to <see cref="CurrentPlatform"/>.</param>
    public static IReadOnlyList<BaselineEntry> Approve(TestRunReport report, string reportDirectory, string baselineDirectory, Func<string, bool> scenarioFilter,
        bool includePassed = false, string? variant = null, string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(scenarioFilter);
        platform = report.Environment.Platform ?? platform ?? CurrentPlatform;
        var root = Path.GetFullPath(baselineDirectory);
        var reports = Path.GetFullPath(reportDirectory);
        var manifest = LoadManifest(root);
        var approved = new List<BaselineEntry>();
        foreach (var scenario in report.Scenarios.Where(s => scenarioFilter(s.ScenarioId)))
        {
            foreach (var check in scenario.Checks.Where(c => c.Kind == CheckKind.Visual && c.Visual?.ActualPath is not null))
            {
                if (!includePassed && check.Status == CheckStatus.Passed)
                    continue;
                var visual = check.Visual!;
                if (variant is not null && !string.Equals(visual.Variant, variant, StringComparison.Ordinal))
                    continue;
                var source = Path.GetFullPath(Path.Combine(reports, visual.ActualPath!));
                // Report paths never point outside the report directory.
                if (!PathSafety.IsInside(source, reports) || !File.Exists(source))
                    continue;

                var relative = RelativePath(platform, scenario.ScenarioId, visual.Variant);
                var target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
                var bytes = File.ReadAllBytes(target);
                var (width, height) = TryReadPngSize(bytes) ?? (0, 0);
                var entry = new BaselineEntry
                {
                    ScenarioId = scenario.ScenarioId,
                    Variant = visual.Variant,
                    File = relative,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    Width = width,
                    Height = height,
                    Browser = report.Environment.Browser,
                    BrowserVersion = report.Environment.BrowserVersion,
                    Viewport = visual.Viewport,
                    Theme = visual.Theme,
                    Os = report.Environment.Os,
                    ApprovedAt = DateTimeOffset.UtcNow,
                };
                manifest.Entries[entry.File] = entry;
                approved.Add(entry);
            }
        }

        if (approved.Count > 0)
            SaveManifest(root, manifest);
        return approved;
    }

    /// <summary><c>{platform}/{scenarioId}/{variant}.png</c> with characters that are invalid in file names replaced.</summary>
    public static string RelativePath(string platform, string scenarioId, string variant) => $"{platform}/{Sanitize(scenarioId)}/{Sanitize(variant)}.png";

    public static BaselineManifest LoadManifest(string baselineDirectory)
    {
        var path = Path.Combine(baselineDirectory, ManifestFile);
        if (!File.Exists(path))
            return new BaselineManifest();
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<BaselineManifest>(stream, WorkbenchJson.Options) ?? new BaselineManifest();
    }

    public static void SaveManifest(string baselineDirectory, BaselineManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(baselineDirectory);
        File.WriteAllText(Path.Combine(baselineDirectory, ManifestFile), JsonSerializer.Serialize(manifest, WorkbenchJson.Indented) + "\n");
    }

    /// <summary>Reads the size of a PNG image from its header, or null when the bytes are not a PNG.</summary>
    public static (int Width, int Height)? TryReadPngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png[12..16].SequenceEqual("IHDR"u8))
            return null;
        return (BinaryPrimitives.ReadInt32BigEndian(png[16..20]), BinaryPrimitives.ReadInt32BigEndian(png[20..24]));
    }

    internal static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray());
    }
}
