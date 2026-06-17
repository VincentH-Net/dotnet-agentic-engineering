namespace Agentic.Check;

sealed class SkillInstaller(ICommandRunner commandRunner, IReporter reporter)
{
    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, string skillsDirectory)
        => [.. skills.Where(skill => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md")))];

    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, IReadOnlyList<string> skillsDirectories)
        => [.. skills.Where(skill => skillsDirectories.Any(directory => IsMissing(skill, directory)))];

    internal static bool IsMissing(SkillManifestEntry skill, string skillsDirectory)
        => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md"));

    public async Task<IReadOnlyList<SkillInstallResult>> InstallAsync(
        IReadOnlyList<SkillManifestEntry> skills,
        string skillsDirectory,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(skillsDirectory);
        List<SkillInstallResult> results = [];
        foreach (var skill in skills)
        {
            var result = await commandRunner.RunAsync(
                "gh",
                ["skill", "install", skill.SourceRepo, skill.InstallArg, "--dir", skillsDirectory],
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            SkillInstallResult installResult = new(
                skill.SourceRepo,
                skill.InstallArg,
                skill.LocalFolder,
                result.Success,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
            results.Add(installResult);

            if (result.Success)
            {
                reporter.Success($"Installed {skill.Display}");
            }
            else
            {
                reporter.Error($"Failed to install {skill.Display}: {result.StandardError.Trim()}");
            }
        }

        return results;
    }

    public IReadOnlyList<SkillCopyResult> CopyInstalledSkills(
        IReadOnlyList<SkillManifestEntry> skills,
        string sourceSkillsDirectory,
        IReadOnlyList<string> targetSkillsDirectories)
    {
        List<SkillCopyResult> results = [];
        foreach (string targetSkillsDirectory in targetSkillsDirectories)
        {
            _ = Directory.CreateDirectory(targetSkillsDirectory);

            foreach (var skill in skills.Where(skill => IsMissing(skill, targetSkillsDirectory)))
            {
                string sourceDirectory = Path.Combine(sourceSkillsDirectory, skill.LocalFolder);
                string targetDirectory = Path.Combine(targetSkillsDirectory, skill.LocalFolder);
                try
                {
                    CopyDirectory(sourceDirectory, targetDirectory);
                    results.Add(new SkillCopyResult(sourceDirectory, targetDirectory, skill.LocalFolder, true, null));
                    reporter.Success($"Copied {skill.LocalFolder} to {targetSkillsDirectory}");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    results.Add(new SkillCopyResult(sourceDirectory, targetDirectory, skill.LocalFolder, false, exception.Message));
                    reporter.Error($"Failed to copy {skill.LocalFolder} to {targetSkillsDirectory}: {exception.Message}");
                }
            }
        }

        return results;
    }

    static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source skill directory was not found: {sourceDirectory}");
        }

        _ = Directory.CreateDirectory(targetDirectory);

        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, targetFile, true);
        }

        foreach (string sourceChildDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string targetChildDirectory = Path.Combine(targetDirectory, Path.GetFileName(sourceChildDirectory));
            CopyDirectory(sourceChildDirectory, targetChildDirectory);
        }
    }
}

sealed record SkillInstallResult(
    string SourceRepo,
    string InstallArg,
    string LocalFolder,
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError);
