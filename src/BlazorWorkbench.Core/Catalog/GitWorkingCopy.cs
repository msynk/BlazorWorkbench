using System.ComponentModel;
using System.Diagnostics;

namespace BlazorWorkbench.Catalog;

/// <summary>Reads the changed files of a git working copy (uncommitted changes, optionally plus commits since a base).</summary>
public static class GitWorkingCopy
{
    /// <summary>
    /// Returns the files changed in the working copy that contains <paramref name="directory"/>: staged, unstaged and
    /// untracked files (untracked ones as new), plus the files changed by commits since <paramref name="baseRef"/> when given
    /// (for example <c>origin/main</c>, compared from the merge base like a pull request).
    /// </summary>
    /// <returns>The files, or null with a reason when git or the repository is not available.</returns>
    public static async Task<(IReadOnlyList<ChangedFile>? Files, string Description)> ReadAsync(string directory, string? baseRef = null, CancellationToken cancellationToken = default)
    {
        var changes = await CollectAsync(directory, baseRef, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        if (changes.Files is null)
            return (null, changes.Error!);
        if (baseRef is not { Length: > 0 })
            return (changes.Files, "Uncommitted changes");
        return changes.Error is { } error
            ? (changes.Files, $"Uncommitted changes (comparing with '{baseRef}' failed: {error})")
            : (changes.Files, $"Uncommitted changes and commits since {baseRef}");
    }

    /// <summary>
    /// Returns every path changed since <paramref name="baseRef"/> as absolute paths: files changed by commits since the merge
    /// base of <paramref name="baseRef"/> and <c>HEAD</c> (like a pull request), plus staged, unstaged and untracked files.
    /// Deleted files and the old paths of renamed files are included, because removing a file can affect rendering too.
    /// Used by change impact (<c>workbench test --changed-since</c>).
    /// </summary>
    /// <returns>The sorted, distinct paths, or null with the reason when git, the repository or the ref is not available.</returns>
    public static async Task<(IReadOnlyList<string>? Paths, string? Error)> ReadChangedPathsAsync(string directory, string baseRef, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);
        var changes = await CollectAsync(directory, baseRef, includeDeleted: true, cancellationToken).ConfigureAwait(false);
        if (changes.Files is null || changes.Error is not null)
            return (null, changes.Error);
        var paths = changes.Files.Select(f => f.Path).Distinct(PathComparer).Order(StringComparer.Ordinal).ToList();
        return (paths, null);
    }

    /// <summary>
    /// True when <paramref name="revision"/> can safely be passed to git as a revision: it does not start with <c>-</c>
    /// (so it can never be read as an option), contains no whitespace or control characters and no <c>..</c> range.
    /// </summary>
    internal static bool IsSafeRevision(string? revision)
    {
        if (string.IsNullOrEmpty(revision) || revision.Length > 256 || revision[0] == '-' || revision.Contains("..", StringComparison.Ordinal))
            return false;
        foreach (var ch in revision)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                return false;
        }

        return true;
    }

    /// <summary>Parses <c>git status --porcelain=v1 -z</c> output. Renames report the new path.</summary>
    public static IReadOnlyList<(string Path, bool IsNew)> ParseStatus(string output) => ParseStatus(output, includeDeleted: false);

    private static List<(string Path, bool IsNew)> ParseStatus(string output, bool includeDeleted)
    {
        ArgumentNullException.ThrowIfNull(output);
        var result = new List<(string, bool)>();
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4)
                continue;
            var code = entry[..2];
            var path = entry[3..];
            // Renames and copies are followed by the original path.
            if (code[0] is 'R' or 'C')
            {
                i++;
                if (includeDeleted && code[0] == 'R' && i < entries.Length && entries[i].Length > 0)
                    result.Add((entries[i], false));
            }

            if (code[0] == 'D' || code[1] == 'D')
            {
                if (includeDeleted)
                    result.Add((path, false));
                continue; // deleted files have no source to map
            }

            result.Add((path, code == "??" || code[0] == 'A'));
        }

        return result;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Uncommitted changes plus, with a base, committed changes since the merge base. Files is null when git or the repository is unavailable; Error is set when only the base comparison failed.</summary>
    private static async Task<(List<ChangedFile>? Files, string? Error)> CollectAsync(string directory, string? baseRef, bool includeDeleted, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var top = await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (top.Error is not null)
            return (null, top.Error);
        var root = top.Output.Trim();

        var status = await RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (status.Error is not null)
            return (null, status.Error);
        var files = ParseStatus(status.Output, includeDeleted).Select(e => new ChangedFile(Path.GetFullPath(Path.Combine(root, e.Path)), e.IsNew)).ToList();

        if (baseRef is not { Length: > 0 })
            return (files, null);
        if (!IsSafeRevision(baseRef))
            return (files, $"'{baseRef}' is not a valid git revision (it must not start with '-' or contain whitespace, control characters or '..').");

        // --end-of-options (git 2.24+) keeps the revision from ever being read as an option; validation above already rejects
        // leading dashes for older versions. The trailing -- keeps a revision that matches a file name from being ambiguous.
        List<string> args = ["diff", "--name-only", "-z"];
        if (includeDeleted)
            args.Add("--no-renames"); // report both paths of a rename
        args.AddRange(["--end-of-options", $"{baseRef}...HEAD", "--"]);
        var diff = await RunAsync(root, args, cancellationToken).ConfigureAwait(false);
        if (diff.Error is not null)
            return (files, diff.Error);
        foreach (var path in diff.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            files.Add(new ChangedFile(Path.GetFullPath(Path.Combine(root, path)), false));
        return (files, null);
    }

    private static async Task<(string Output, string? Error)> RunAsync(string directory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // Paths are read with -z, which never quotes; quotePath=false also keeps any other output unescaped.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotePath=false");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return ("", "git could not be started.");
            // Read both streams concurrently: git can write enough to stderr to block on a full pipe. The reads end when the
            // process exits (or is killed on cancellation).
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }

            var stdout = await output.ConfigureAwait(false);
            var stderr = await error.ConfigureAwait(false);
            return process.ExitCode == 0 ? (stdout, null) : ("", stderr.Trim() is { Length: > 0 } message ? message : $"git exited with {process.ExitCode}.");
        }
        catch (Win32Exception)
        {
            return ("", "git is not available on PATH.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            return ("", ex.Message);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process already exited or cannot be killed; nothing else to clean up.
        }
    }
}
