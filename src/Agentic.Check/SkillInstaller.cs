namespace Agentic.Check;

sealed class SkillInstaller(ICommandRunner commandRunner, IReporter reporter)
{
    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, string skillsDirectory)
        => [.. skills.Where(skill => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md")))];

    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, IReadOnlyList<string> skillsDirectories)
        => [.. skills.Where(skill => skillsDirectories.Any(directory => IsMissing(skill, directory)))];

    internal static IReadOnlyList<SkillManifestEntry> FindInstalledFromBranch(
        IReadOnlyList<SkillManifestEntry> skills,
        IReadOnlyList<string> skillsDirectories)
        => [.. skills.Where(skill => skillsDirectories.Any(directory => IsInstalledFromBranch(skill, directory)))];

    internal static bool IsMissing(SkillManifestEntry skill, string skillsDirectory)
        => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md"));

    internal static bool IsInstalledFromBranch(SkillManifestEntry skill, string skillsDirectory)
        => ReadGitHubRef(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md")) is { } gitHubRef
            && gitHubRef.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SkillInstallResult>> InstallAsync(
        IReadOnlyList<SkillManifestEntry> skills,
        string skillsDirectory,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action? progressAdvance = null,
        bool reportPreviewChangeStatus = false)
    {
        _ = Directory.CreateDirectory(skillsDirectory);
        List<SkillInstallResult> results = [];
        foreach (var skill in skills)
        {
            string skillFile = Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md");
            string? beforeSha = reportPreviewChangeStatus ? ReadTreeSha(skillFile) : null;
            List<string> arguments = ["skill", "install", skill.SourceRepo, skill.InstallArg, "--dir", skillsDirectory];
            if (!string.IsNullOrWhiteSpace(skill.SourceRef))
            {
                arguments.AddRange(["--pin", skill.SourceRef]);
            }

            if (skill.ForceInstall || !string.IsNullOrWhiteSpace(skill.SourceRef))
            {
                arguments.Add("--force");
            }

            var result = await commandRunner.RunAsync(
                "gh",
                arguments,
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
                reporter.Success(ActionOutputFormatter.FormatLine(
                    FormatInstallAction(skill, reportPreviewChangeStatus, beforeSha, ReadTreeSha(skillFile)),
                    ActionOutputFormatter.FormatSkillName(workingDirectory, skillsDirectory, skill.LocalFolder)));
            }
            else
            {
                reporter.Error($"Failed to install {skill.Display}: {result.StandardError.Trim()}");
            }

            progressAdvance?.Invoke();
        }

        return results;
    }

    public IReadOnlyList<SkillCopyResult> CopyInstalledSkills(
        IReadOnlyList<SkillManifestEntry> skills,
        string sourceSkillsDirectory,
        IReadOnlyList<string> targetSkillsDirectories,
        string workingDirectory,
        Action? progressAdvance = null,
        bool overwriteExisting = false,
        bool reportPreviewChangeStatus = false)
    {
        List<SkillCopyResult> results = [];
        foreach (string targetSkillsDirectory in targetSkillsDirectories)
        {
            _ = Directory.CreateDirectory(targetSkillsDirectory);

            foreach (var skill in skills.Where(skill => overwriteExisting || skill.ForceInstall || IsMissing(skill, targetSkillsDirectory)))
            {
                string sourceDirectory = Path.Combine(sourceSkillsDirectory, skill.LocalFolder);
                string targetDirectory = Path.Combine(targetSkillsDirectory, skill.LocalFolder);
                string targetSkillFile = Path.Combine(targetDirectory, "SKILL.md");
                string? beforeSha = reportPreviewChangeStatus ? ReadTreeSha(targetSkillFile) : null;
                try
                {
                    CopyDirectory(sourceDirectory, targetDirectory);
                    results.Add(new SkillCopyResult(sourceDirectory, targetDirectory, skill.LocalFolder, true, null));
                    reporter.Success(ActionOutputFormatter.FormatLine(
                        FormatCopyAction(skill, reportPreviewChangeStatus, beforeSha, ReadTreeSha(targetSkillFile)),
                        ActionOutputFormatter.FormatSkillName(workingDirectory, targetSkillsDirectory, skill.LocalFolder)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    results.Add(new SkillCopyResult(sourceDirectory, targetDirectory, skill.LocalFolder, false, exception.Message));
                    reporter.Error($"Failed to copy {skill.LocalFolder} to {targetSkillsDirectory}: {exception.Message}");
                }
                finally
                {
                    progressAdvance?.Invoke();
                }
            }
        }

        return results;
    }

    static string FormatInstallAction(
        SkillManifestEntry skill,
        bool reportPreviewChangeStatus,
        string? beforeSha,
        string? afterSha)
    {
        if (reportPreviewChangeStatus)
        {
            return FormatPreviewChangeAction(beforeSha, afterSha);
        }

        return skill.ForceInstall && skill.RecommendationAction.Equals("switch to stable", StringComparison.Ordinal)
            ? "Switch to stable skill"
            : "Installed skill";
    }

    static string FormatCopyAction(
        SkillManifestEntry skill,
        bool reportPreviewChangeStatus,
        string? beforeSha,
        string? afterSha)
    {
        if (reportPreviewChangeStatus)
        {
            return FormatPreviewChangeAction(beforeSha, afterSha);
        }

        return skill.ForceInstall && skill.RecommendationAction.Equals("switch to stable", StringComparison.Ordinal)
            ? "Switch to stable skill"
            : "Copied skill";
    }

    static string FormatPreviewChangeAction(string? beforeSha, string? afterSha)
    {
        if (string.IsNullOrWhiteSpace(afterSha))
        {
            return "Re-installed skill";
        }

        if (string.IsNullOrWhiteSpace(beforeSha))
        {
            return "Installed skill";
        }

        return string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)
            ? "Re-installed skill"
            : "Updated skill";
    }

    static string? ReadTreeSha(string skillFile)
        => ReadFrontMatterValue(skillFile, "github-tree-sha:");

    static string? ReadGitHubRef(string skillFile)
        => ReadFrontMatterValue(skillFile, "github-ref:");

    static string? ReadFrontMatterValue(string skillFile, string key)
    {
        if (!File.Exists(skillFile))
        {
            return null;
        }

        foreach (string line in File.ReadLines(skillFile))
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.Equals("---", StringComparison.Ordinal) && line.Length == 3)
            {
                continue;
            }

            if (!trimmedLine.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmedLine.Equals("---", StringComparison.Ordinal))
                {
                    return null;
                }

                continue;
            }

            return trimmedLine[key.Length..].Trim().Trim('"', '\'');
        }

        return null;
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
