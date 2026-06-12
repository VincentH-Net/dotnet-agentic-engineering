namespace Agentic.Check;

sealed class SkillInstaller(ICommandRunner commandRunner, IReporter reporter)
{
    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, string skillsDirectory)
        => [.. skills.Where(skill => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md")))];

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
}

sealed record SkillInstallResult(
    string SourceRepo,
    string InstallArg,
    string LocalFolder,
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError);
