namespace Agentic.PackageFixtures;

// Test processes keep the host PATH for dotnet, git, gh and bash, but not the host's .NET global
// tools folder: a developer's own dna there would otherwise count as a shorthand conflict.
static class HostPath
{
    internal static string WithoutGlobalTools()
        => WithoutGlobalTools(Environment.GetEnvironmentVariable("PATH"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("DOTNET_CLI_HOME"), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    internal static string WithoutGlobalTools(string? path, string home, string? cliHome, StringComparison comparison)
    {
        string[] toolFolders = [.. new[] { home, cliHome }.Where(root => !string.IsNullOrEmpty(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.Combine(root!, ".dotnet", "tools")))];
        // The macOS .NET installer adds a literal ~/.dotnet/tools entry, which Bash expands.
        return string.Join(Path.PathSeparator, (path ?? string.Empty).Split(Path.PathSeparator).Where(entry =>
        {
            string folder = entry == "~" || entry.StartsWith("~/", StringComparison.Ordinal) ? home + entry[1..] : entry;
            return !toolFolders.Any(tools => string.Equals(Path.TrimEndingDirectorySeparator(folder), tools, comparison));
        }));
    }
}
