namespace Agentic.Check;

// Inside a repository that has a .gitignore, the files git considers part of it: tracked, plus
// untracked and not ignored. Null when the target is outside a repository, no .gitignore applies,
// or git is unavailable or fails; the detector then walks the folder, skipping output folders.
static class RepositoryFiles
{
    internal static async Task<IReadOnlyCollection<string>?> ListAsync(ICommandRunner runner, string targetDirectory, CancellationToken cancellationToken)
    {
        var directories = RepositoryScope.DirectoriesUpToGitRoot(targetDirectory);
        if (!RepositoryScope.IsGitRoot(directories[^1]) || !directories.Any(directory => File.Exists(Path.Combine(directory, ".gitignore"))))
        {
            return null;
        }

        var result = await runner.RunAsync("git", ["ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "."], directories[0], cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return null;
        }

        // Entries that are not files on disk are deleted tracked files and submodule links.
        return [.. result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.GetFullPath(Path.Combine(directories[0], path)))
            .Where(File.Exists)];
    }
}
