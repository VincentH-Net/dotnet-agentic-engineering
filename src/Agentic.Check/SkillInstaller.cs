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
            string[] arguments = string.IsNullOrWhiteSpace(skill.SourceRef)
                ? ["skill", "install", skill.SourceRepo, skill.InstallArg, "--dir", skillsDirectory]
                : ["skill", "install", skill.SourceRepo, skill.InstallArg, "--dir", skillsDirectory, "--pin", skill.SourceRef, "--force"];
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
                string prefix = reportPreviewChangeStatus
                    ? FormatPreviewChangePrefix(beforeSha, ReadTreeSha(skillFile))
                    : string.Empty;
                reporter.Success($"{prefix}Installed {skill.Display}");
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
        Action? progressAdvance = null,
        bool overwriteExisting = false,
        bool reportPreviewChangeStatus = false)
    {
        List<SkillCopyResult> results = [];
        foreach (string targetSkillsDirectory in targetSkillsDirectories)
        {
            _ = Directory.CreateDirectory(targetSkillsDirectory);

            foreach (var skill in skills.Where(skill => overwriteExisting || IsMissing(skill, targetSkillsDirectory)))
            {
                string sourceDirectory = Path.Combine(sourceSkillsDirectory, skill.LocalFolder);
                string targetDirectory = Path.Combine(targetSkillsDirectory, skill.LocalFolder);
                string targetSkillFile = Path.Combine(targetDirectory, "SKILL.md");
                string? beforeSha = reportPreviewChangeStatus ? ReadTreeSha(targetSkillFile) : null;
                try
                {
                    CopyDirectory(sourceDirectory, targetDirectory);
                    results.Add(new SkillCopyResult(sourceDirectory, targetDirectory, skill.LocalFolder, true, null));
                    string prefix = reportPreviewChangeStatus
                        ? FormatPreviewChangePrefix(beforeSha, ReadTreeSha(targetSkillFile))
                        : string.Empty;
                    reporter.Success($"{prefix}Copied {skill.LocalFolder} to {targetSkillsDirectory}");
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

    internal static IReadOnlyDictionary<string, string?> ReadTreeShas(
        IReadOnlyList<SkillManifestEntry> skills,
        IReadOnlyList<string> skillsDirectories)
    {
        Dictionary<string, string?> shas = new(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            shas[skill.Key] = skillsDirectories
                .Select(directory => ReadTreeSha(Path.Combine(directory, skill.LocalFolder, "SKILL.md")))
                .FirstOrDefault(sha => !string.IsNullOrWhiteSpace(sha));
        }

        return shas;
    }

    static string FormatPreviewChangePrefix(string? beforeSha, string? afterSha)
    {
        if (string.IsNullOrWhiteSpace(afterSha))
        {
            return "(re-installed) ";
        }

        if (string.IsNullOrWhiteSpace(beforeSha))
        {
            return "(installed   ) ";
        }

        return string.Equals(beforeSha, afterSha, StringComparison.OrdinalIgnoreCase)
            ? "(re-installed) "
            : "(updated     ) ";
    }

    static string? ReadTreeSha(string skillFile)
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

            if (!trimmedLine.StartsWith("github-tree-sha:", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmedLine.Equals("---", StringComparison.Ordinal))
                {
                    return null;
                }

                continue;
            }

            return trimmedLine["github-tree-sha:".Length..].Trim().Trim('"', '\'');
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
