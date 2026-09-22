namespace Agentic.Check;

// The target and its parents up to and including the nearest git root, target first. Without a git
// root only the target counts, so nothing outside a scratch folder is ever read or written.
static class RepositoryScope
{
    internal static List<string> DirectoriesUpToGitRoot(string targetDirectory)
    {
        List<string> directories = [];
        for (string? current = Path.GetFullPath(targetDirectory); current is not null; current = Path.GetDirectoryName(current))
        {
            directories.Add(current);
            if (IsGitRoot(current))
            {
                return directories;
            }
        }

        return [directories[0]];
    }

    static bool IsGitRoot(string directory)
        => Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));
}
