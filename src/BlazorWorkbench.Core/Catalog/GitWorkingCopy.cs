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
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var top = await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (top.Error is not null)
            return (null, top.Error);
        var root = top.Output.Trim();

        var status = await RunAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken).ConfigureAwait(false);
        if (status.Error is not null)
            return (null, status.Error);
        var files = ParseStatus(status.Output).Select(e => new ChangedFile(Path.GetFullPath(Path.Combine(root, e.Path)), e.IsNew)).ToList();

        var description = "Uncommitted changes";
        if (baseRef is { Length: > 0 })
        {
            var diff = await RunAsync(root, ["diff", "--name-only", "-z", $"{baseRef}...HEAD"], cancellationToken).ConfigureAwait(false);
            if (diff.Error is not null)
                return (files, $"Uncommitted changes (comparing with '{baseRef}' failed: {diff.Error})");
            foreach (var path in diff.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                files.Add(new ChangedFile(Path.GetFullPath(Path.Combine(root, path)), false));
            description = $"Uncommitted changes and commits since {baseRef}";
        }

        return (files, description);
    }

    /// <summary>Parses <c>git status --porcelain=v1 -z</c> output. Renames report the new path.</summary>
    public static IReadOnlyList<(string Path, bool IsNew)> ParseStatus(string output)
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
            // Renames and copies are followed by the original path, which is skipped.
            if (code[0] is 'R' or 'C')
                i++;
            if (code[0] == 'D' || code[1] == 'D')
                continue; // deleted files have no source to map
            result.Add((path, code == "??" || code[0] == 'A'));
        }

        return result;
    }

    private static async Task<(string Output, string? Error)> RunAsync(string directory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return ("", "git could not be started.");
            // Read both streams concurrently: git can write enough to stderr to block on a full pipe.
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? (await output.ConfigureAwait(false), null) : ("", (await error.ConfigureAwait(false)).Trim() is { Length: > 0 } message ? message : $"git exited with {process.ExitCode}.");
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
}
