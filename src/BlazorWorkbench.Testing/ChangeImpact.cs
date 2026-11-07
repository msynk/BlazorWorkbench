using System.Diagnostics;
using System.Text.RegularExpressions;
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
/// specific components (shared code, global styles, project files) selects every scenario, and a changed component also
/// selects the scenarios of every component that renders it.
/// </summary>
public static partial class ChangeImpact
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
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var changedComponents = new HashSet<string>(StringComparer.Ordinal);
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
                selected.UnionWith(byComponent.SelectMany(c => c.Scenarios.Select(s => s.Id)));
                changedComponents.UnionWith(byComponent.Select(c => TagName(c.Name)));
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

        var direct = selected.Count;
        if (changedComponents.Count > 0)
        {
            // Components that render a changed component (directly or through other components) are affected too.
            var sources = ReadSources(root, baselines);
            var affected = Dependents(changedComponents, sources);
            foreach (var component in catalog.Components.Where(c => affected.Contains(TagName(c.Name))))
                selected.UnionWith(component.Scenarios.Select(s => s.Id));

            // So are scenarios of other components that render one (for example a Button in a Dialog's ChildContent).
            var reference = ReferenceRegex(affected);
            foreach (var (path, text) in sources)
            {
                if (scenariosBySource.TryGetValue(Normalize(Path.GetRelativePath(root, path)), out var ids) && reference.IsMatch(text))
                    selected.UnionWith(ids);
            }
        }

        var indirect = selected.Count - direct;
        return new ChangeSelection(selected, mapped == 0
            ? "No changes affect scenarios."
            : $"{mapped} changed file(s) affect {selected.Count} scenario(s){(indirect > 0 ? $" ({indirect} of them render a changed component)" : "")}.", unmapped);
    }

    // A markup tag or generic argument (<Button, OpenComponent<Button>, <Lib.Button) or typeof(Button).
    private static Regex ReferenceRegex(IEnumerable<string> names) =>
        new($@"(?:<|typeof\(\s*)(?:[\w.]+\.)?(?:{string.Join('|', names.Select(Regex.Escape))})\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns <paramref name="changed"/> plus the names of every component whose source renders one of them, transitively
    /// (Button changed, Toolbar renders Button, Dialog renders Toolbar: all three). Matching is textual and deliberately
    /// broad: a markup tag or generic argument (<c>&lt;Button</c>, <c>OpenComponent&lt;Button&gt;</c>) or <c>typeof(Button)</c>
    /// marks the file's components as dependents. Over-selection only runs extra scenarios; under-selection would hide regressions.
    /// </summary>
    internal static HashSet<string> Dependents(IEnumerable<string> changed, IReadOnlyList<(string Path, string Text)> sources)
    {
        var affected = new HashSet<string>(changed, StringComparer.Ordinal);
        var files = sources.Select(s => (Names: DeclaredComponents(s.Path, s.Text), s.Text)).Where(f => f.Names.Count > 0).ToList();
        var pending = new Queue<string>(affected);
        while (pending.TryDequeue(out var name))
        {
            var reference = ReferenceRegex([name]);
            foreach (var (names, text) in files)
            {
                if (names.All(affected.Contains) || !reference.IsMatch(text))
                    continue;
                foreach (var dependent in names.Where(affected.Add))
                    pending.Enqueue(dependent);
            }
        }

        return affected;
    }

    // Button.razor and Button.razor.cs declare Button; other C# files declare the classes they contain.
    private static List<string> DeclaredComponents(string path, string text)
    {
        var file = Path.GetFileName(path);
        if (file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            return [file[..^".razor".Length]];
        if (file.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
            return [file[..^".razor.cs".Length]];
        return ClassRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
    }

    // Grid<TItem> (or Grid`1) is written <Grid ...> in markup.
    private static string TagName(string componentName)
    {
        var end = componentName.IndexOfAny(['<', '`']);
        return end < 0 ? componentName : componentName[..end];
    }

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", "artifacts", "TestResults" };

    /// <summary>Razor and C# sources under <paramref name="root"/> (build output, hidden folders and baselines excluded).</summary>
    private static List<(string Path, string Text)> ReadSources(string root, string? baselines)
    {
        var result = new List<(string, string)>();
        var pending = new Stack<string>();
        if (Directory.Exists(root))
            pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(sub);
                    if (!name.StartsWith('.') && !SkippedDirectories.Contains(name) && !string.Equals(sub, baselines, StringComparison.OrdinalIgnoreCase))
                        pending.Push(sub);
                }

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        result.Add((file, File.ReadAllText(file)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable folders cannot contribute dependents.
            }
        }

        return result;
    }

    [GeneratedRegex(@"\b(?:class|record)\s+([A-Za-z_]\w*)", RegexOptions.CultureInvariant)]
    private static partial Regex ClassRegex();

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
