using System.Diagnostics;
using BlazorWorkbench.Catalog;
using BlazorWorkbench.Contracts;

namespace BlazorWorkbench.Testing;

/// <summary>Scenarios selected for a change set. <see cref="ScenarioIds"/> is null when everything must run.</summary>
public sealed record ChangeSelection(IReadOnlySet<string>? ScenarioIds, string Reason, IReadOnlyList<string> UnmappedFiles)
{
    public bool RunAll => ScenarioIds is null;
}

/// <summary>
/// Maps changed files to the scenarios they can affect. It is conservative: a change that cannot be attributed to
/// specific components (shared code, global styles, project files) selects every scenario.
/// </summary>
public static class ChangeImpact
{
    /// <summary>Paths (globs relative to the source root) that never affect rendering.</summary>
    public static IReadOnlyList<string> DefaultIgnore { get; } = ["*.md", "docs/*", ".github/*", "*.txt", "LICENSE", ".gitignore", ".gitattributes", ".editorconfig", "*/manifest.json"];

    public static ChangeSelection Select(CatalogDto catalog, IEnumerable<string> changedFiles, string sourceRoot, string? baselineDirectory = null, IEnumerable<string>? ignore = null)
    {
        var ignorePatterns = (ignore ?? DefaultIgnore).ToList();
        var root = Path.GetFullPath(sourceRoot);
        var baselines = baselineDirectory is null ? null : Path.GetFullPath(baselineDirectory);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmapped = new List<string>();
        var mapped = 0;

        var componentsBySource = catalog.Components.Where(c => c.SourcePath is not null)
            .GroupBy(c => Normalize(c.SourcePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(c => c.Scenarios.Select(s => s.Id)).ToList(), StringComparer.OrdinalIgnoreCase);
        var scenariosBySource = catalog.Scenarios.Where(s => s.SourcePath is not null)
            .GroupBy(s => Normalize(s.SourcePath!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList(), StringComparer.OrdinalIgnoreCase);
        var scenarioIds = catalog.Scenarios.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in changedFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Baselines: {baselines}/{platform}/{scenarioId}/{variant}.png
            if (baselines is not null && file.StartsWith(baselines + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var parts = Path.GetRelativePath(baselines, file).Replace('\\', '/').Split('/');
                if (parts.Length >= 3 && scenarioIds.Contains(parts[1]))
                {
                    selected.Add(parts[1]);
                    mapped++;
                }

                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                continue; // outside the Workbench root: not part of this catalog
            if (ignorePatterns.Any(p => Glob.IsMatch(relative, p)))
                continue;

            var key = Normalize(relative);
            var hit = false;
            if (componentsBySource.TryGetValue(key, out var byComponent))
            {
                selected.UnionWith(byComponent);
                hit = true;
            }

            if (scenariosBySource.TryGetValue(key, out var byScenario))
            {
                selected.UnionWith(byScenario);
                hit = true;
            }

            if (hit) mapped++;
            else unmapped.Add(relative);
        }

        if (unmapped.Count > 0)
            return new ChangeSelection(null, $"{unmapped.Count} changed file(s) cannot be attributed to specific components (for example {unmapped[0]}); running every scenario.", unmapped);

        return new ChangeSelection(selected, mapped == 0
            ? "No changes affect scenarios."
            : $"{mapped} changed file(s) affect {selected.Count} scenario(s).", unmapped);
    }

    /// <summary>Files changed since <paramref name="reference"/> (committed, staged, unstaged and untracked), as absolute paths.</summary>
    public static IReadOnlyList<string> GitChangedFiles(string workingDirectory, string reference)
    {
        var top = Git(workingDirectory, "rev-parse", "--show-toplevel").Trim();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in new[]
                 {
                     Git(top, "diff", "--name-only", reference),
                     Git(top, "diff", "--name-only", "--cached"),
                     Git(top, "ls-files", "--others", "--exclude-standard"),
                 })
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                files.Add(Path.GetFullPath(Path.Combine(top, line)));
        }

        return files.Order(StringComparer.Ordinal).ToList();
    }

    // Button.razor.cs / Button.razor.css / Button.razor.js belong to Button.razor.
    internal static string Normalize(string path)
    {
        path = path.Replace('\\', '/');
        foreach (var suffix in new[] { ".razor.cs", ".razor.css", ".razor.js" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return path[..^(suffix.Length - ".razor".Length)];
        }

        return path;
    }

    private static string Git(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.safecrlf=false");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            // Read both streams concurrently: git can write enough warnings to stderr to block on a full pipe.
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            var error = errorTask.GetAwaiter().GetResult();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new WorkbenchInfrastructureException($"git {string.Join(' ', args)} failed: {error.Trim()}");
            return output;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new WorkbenchInfrastructureException("git is not available on PATH; --changed-since needs it.", ex);
        }
    }
}
