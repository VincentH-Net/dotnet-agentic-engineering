using System.Runtime.ExceptionServices;

namespace Agentic.Check;

sealed class SkillInstaller(
    ICommandRunner commandRunner,
    IReporter reporter,
    int maxConcurrentInstalls = SkillInstaller.DefaultMaxConcurrentInstalls,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    internal const string StableSwitchAction = "switch to stable";

    // gh skill update skips a skill whose SKILL.md carries no GitHub metadata; a forced
    // re-install from the manifest source writes that metadata so later updates work.
    internal const string MetadataReinstallAction = "re-install to enable updates";

    // gh skill update follows the path stamped into the skill; a skill its repository moved is
    // re-installed from where the manifest names it now.
    internal const string MovedReinstallAction = "re-install from its new path";

    internal const string RemoveAction = "remove";

    // Each gh skill install spends its time on about nine serial GitHub round trips, so installs
    // run concurrently. Measured on 2026-10-02: 84 installs launched at once finished in 7 s with
    // no rate-limit response. 48 keeps the largest manifest batch near 10 s while bounding the
    // number of gh processes and staying clear of GitHub's documented 100 concurrent requests.
    internal const int DefaultMaxConcurrentInstalls = 48;

    // gh writes each skill file straight into its target, so a failed install would leave a
    // partial folder that later runs mistake for an installed skill. Installing into a sibling
    // staging folder and moving the finished skill keeps the live directory whole; a sibling
    // keeps that move a same-volume rename.
    internal const string StagingFolderName = ".agentic-check-staging";

    // GitHub documents no wait for a secondary limit beyond "at least one minute, then back off
    // exponentially", and gh does not surface a retry-after value, so the waits are fixed.
    internal static IReadOnlyList<TimeSpan> SecondaryLimitWaits { get; } = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];

    readonly ICommandRunner commandRunner = commandRunner;
    readonly IReporter reporter = reporter;
    readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, string skillsDirectory)
        => [.. skills.Where(skill => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md")))];

    internal static IReadOnlyList<SkillManifestEntry> FindMissing(IReadOnlyList<SkillManifestEntry> skills, IReadOnlyList<string> skillsDirectories)
        => [.. skills.Where(skill => skillsDirectories.Any(directory => IsMissing(skill, directory)))];

    internal static IReadOnlyList<SkillManifestEntry> FindRequiringStableSwitch(
        IReadOnlyList<SkillManifestEntry> skills,
        IReadOnlyList<string> skillsDirectories)
        => [.. skills.Where(skill => skillsDirectories.Any(directory => RequiresStableSwitch(skill, directory)))];

    internal static bool IsMissing(SkillManifestEntry skill, string skillsDirectory)
        => !File.Exists(Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md"));

    internal static bool RequiresStableSwitch(SkillManifestEntry skill, string skillsDirectory)
    {
        string skillFile = Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md");
        if (!string.IsNullOrWhiteSpace(ReadFrontMatterValue(skillFile, "github-pinned:")))
        {
            return true;
        }

        string? gitHubRef = ReadGitHubRef(skillFile);
        if (gitHubRef is null || !gitHubRef.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            return false;
        }

        // An unpinned default branch is also a valid stable source when no release exists.
        return skill.ResolvedSource is not { IsDefaultBranch: true } stableSource
            || !gitHubRef.Equals($"refs/heads/{stableSource.Ref}", StringComparison.Ordinal);
    }

    // Removes the whole folder of each selected removal wherever it is installed: a folder stamped with
    // the row's repository, or one without a stamp, which only a row the user selected can name.
    internal IReadOnlyList<SkillRemovalResult> Remove(
        IReadOnlyList<SkillManifestEntry> removals,
        IReadOnlyList<InstalledSkill> installed,
        string workingDirectory,
        Action<SkillRemovalResult>? reportResult = null)
    {
        List<SkillRemovalResult> results = [];
        foreach (var removal in removals)
        {
            foreach (var skill in installed.Where(skill => skill.Folder.Equals(removal.LocalFolder, StringComparison.OrdinalIgnoreCase)
                && (skill.SourceRepo is null || skill.SourceRepo.Equals(removal.SourceRepo, StringComparison.OrdinalIgnoreCase))))
            {
                string? error = null;
                try
                {
                    Directory.Delete(Path.Combine(skill.SkillsDirectory, skill.Folder), recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    error = exception.Message;
                }

                SkillRemovalResult result = new(removal.SourceRepo, skill.Folder, skill.SkillsDirectory, error is null, error);
                results.Add(result);
                reportResult?.Invoke(result);
                string skillName = ActionOutputFormatter.FormatSkillName(workingDirectory, skill.SkillsDirectory, skill.Folder);
                if (error is null)
                {
                    reporter.Success(ActionOutputFormatter.FormatLine("Removed skill", skillName));
                }
                else
                {
                    reporter.Error(ActionOutputFormatter.FormatLine("Failed skill removal", skillName));
                    reporter.Error(ActionOutputFormatter.FormatDetail(error));
                }
            }
        }

        return results;
    }

    internal static string StagingRoot(string skillsDirectory)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(skillsDirectory);
        return Path.Combine(Path.GetDirectoryName(trimmed) ?? trimmed, StagingFolderName);
    }

    internal static string StagingDirectory(string skillsDirectory, string localFolder)
        => Path.Combine(StagingRoot(skillsDirectory), localFolder);

    public async Task<IReadOnlyList<SkillInstallResult>> InstallAsync(
        IReadOnlyList<SkillManifestEntry> skills,
        string skillsDirectory,
        string workingDirectory,
        CancellationToken cancellationToken,
        Action? progressAdvance = null,
        bool reportPreviewChangeStatus = false,
        Action<SkillInstallResult>? reportResult = null,
        Action<string>? recordWarning = null)
    {
        _ = Directory.CreateDirectory(skillsDirectory);
        string stagingRoot = StagingRoot(skillsDirectory);
        DeleteDirectory(stagingRoot);
        InstallBatch batch = new(this, skills, skillsDirectory, workingDirectory, reportPreviewChangeStatus, progressAdvance, reportResult);
        try
        {
            var remaining = await batch.RunAsync([.. Enumerable.Range(0, skills.Count)], maxConcurrentInstalls, cancellationToken).ConfigureAwait(false);
            for (int attempt = 0; remaining.Count > 0; attempt++)
            {
                if (attempt == SecondaryLimitWaits.Count)
                {
                    throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true);
                }

                var wait = SecondaryLimitWaits[attempt];
                string notice = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"GitHub temporarily limited requests. Waiting {wait.TotalSeconds:0} seconds, then retrying {remaining.Count} skill install(s) one at a time.");
                reporter.Warning(notice);
                recordWarning?.Invoke(notice);
                await delay(wait, cancellationToken).ConfigureAwait(false);
                remaining = await batch.RunAsync(remaining, 1, cancellationToken).ConfigureAwait(false);
            }

            return batch.Results;
        }
        catch
        {
            // Whatever finished before the failure stays installed and belongs in the report.
            batch.FlushCompleted();
            throw;
        }
        finally
        {
            DeleteDirectory(stagingRoot);
        }
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
                    reporter.Error(ActionOutputFormatter.FormatLine(
                        "Failed skill copy",
                        ActionOutputFormatter.FormatSkillName(workingDirectory, targetSkillsDirectory, skill.LocalFolder)));
                    reporter.Error(ActionOutputFormatter.FormatDetail(exception.Message));
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

        return FormatForcedAction(skill) ?? "Installed skill";
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

        return FormatForcedAction(skill) ?? "Copied skill";
    }

    static string? FormatForcedAction(SkillManifestEntry skill)
        => !skill.ForceInstall
            ? null
            : skill.RecommendationAction switch
            {
                StableSwitchAction => "Switch to stable skill",
                MetadataReinstallAction or MovedReinstallAction => "Re-installed skill",
                _ => null
            };

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

    internal static string? ReadFrontMatterValue(string skillFile, string key)
    {
        if (!File.Exists(skillFile))
        {
            return null;
        }

        using var lines = File.ReadLines(skillFile).GetEnumerator();
        if (!lines.MoveNext() || !lines.Current.Trim().Equals("---", StringComparison.Ordinal))
        {
            return null;
        }

        while (lines.MoveNext())
        {
            string trimmedLine = lines.Current.Trim();
            if (trimmedLine.Equals("---", StringComparison.Ordinal))
            {
                return null;
            }

            if (!trimmedLine.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
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

    // Staging folders are disposable: a leftover is removed on the next run, so cleanup never fails a run.
    static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    sealed record InstallAttempt(CommandResult? Result, GitHubRateLimitException? RateLimit);

    // Runs the gh installs of one skills directory. gh processes run concurrently, but results are
    // emitted in manifest order so output and reports stay deterministic.
    sealed class InstallBatch(
        SkillInstaller installer,
        IReadOnlyList<SkillManifestEntry> skills,
        string skillsDirectory,
        string workingDirectory,
        bool reportPreviewChangeStatus,
        Action? progressAdvance,
        Action<SkillInstallResult>? reportResult)
    {
        readonly SkillInstallResult?[] results = new SkillInstallResult?[skills.Count];
        readonly bool[] emitted = new bool[skills.Count];
        readonly string?[] beforeShas = [.. skills.Select(skill => reportPreviewChangeStatus ? ReadTreeSha(SkillFile(skillsDirectory, skill)) : null)];
        int nextToEmit;

        public IReadOnlyList<SkillInstallResult> Results
            => [.. results.Select(result => result ?? throw new InvalidOperationException("A skill install did not complete."))];

        // Returns the indexes to retry: those that met a secondary rate limit, plus any not launched
        // because one was met. A primary limit cannot clear within a run, so it ends the batch.
        public async Task<List<int>> RunAsync(List<int> indexes, int maxConcurrency, CancellationToken cancellationToken)
        {
            Dictionary<Task<InstallAttempt>, int> running = [];
            List<int> retry = [];
            GitHubRateLimitException? primaryLimit = null;
            bool launching = true;
            int next = 0;
            try
            {
                while (running.Count > 0 || (launching && next < indexes.Count))
                {
                    while (launching && next < indexes.Count && running.Count < maxConcurrency)
                    {
                        int index = indexes[next++];
                        running[AttemptAsync(index, cancellationToken)] = index;
                    }

                    var completed = await Task.WhenAny(running.Keys).ConfigureAwait(false);
                    int completedIndex = running[completed];
                    _ = running.Remove(completed);
                    var attempt = await completed.ConfigureAwait(false);
                    if (attempt.RateLimit is { IsSecondary: true })
                    {
                        retry.Add(completedIndex);
                        launching = false;
                    }
                    else if (attempt.RateLimit is not null)
                    {
                        primaryLimit ??= attempt.RateLimit;
                        launching = false;
                    }
                    else
                    {
                        Complete(completedIndex, attempt.Result!);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Let the cancelled installs finish shutting down before their staging folders go.
                try
                {
                    _ = await Task.WhenAll(running.Keys).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                throw;
            }

            if (primaryLimit is not null)
            {
                ExceptionDispatchInfo.Capture(primaryLimit).Throw();
            }

            retry.AddRange(indexes.Skip(next));
            retry.Sort();
            return retry;
        }

        public void FlushCompleted()
        {
            for (int index = 0; index < results.Length; index++)
            {
                if (results[index] is { } result && !emitted[index])
                {
                    Emit(index, result);
                }
            }
        }

        async Task<InstallAttempt> AttemptAsync(int index, CancellationToken cancellationToken)
        {
            var skill = skills[index];
            string stagingDirectory = StagingDirectory(skillsDirectory, skill.LocalFolder);
            DeleteDirectory(stagingDirectory);
            List<string> arguments = ["skill", "install", skill.SourceRepo, skill.InstallArg, "--dir", stagingDirectory];
            if (!string.IsNullOrWhiteSpace(skill.SourceRef))
            {
                arguments.AddRange(["--pin", skill.SourceRef]);
            }

            if (skill.ForceInstall || !string.IsNullOrWhiteSpace(skill.SourceRef))
            {
                arguments.Add("--force");
            }

            try
            {
                var result = await installer.commandRunner.RunAsync("gh", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
                return new InstallAttempt(result, null);
            }
            catch (GitHubRateLimitException exception)
            {
                return new InstallAttempt(null, exception);
            }
        }

        void Complete(int index, CommandResult result)
        {
            var skill = skills[index];
            string stagingDirectory = StagingDirectory(skillsDirectory, skill.LocalFolder);
            string? error = result.Success
                ? MoveIntoPlace(
                    Path.Combine(stagingDirectory, skill.LocalFolder),
                    Path.Combine(skillsDirectory, skill.LocalFolder),
                    Path.Combine(StagingRoot(skillsDirectory), skill.LocalFolder + ".previous"))
                : null;
            DeleteDirectory(stagingDirectory);
            bool success = result.Success && error is null;
            results[index] = new SkillInstallResult(
                skill.SourceRepo,
                skill.InstallArg,
                skill.LocalFolder,
                success,
                result.ExitCode,
                result.StandardOutput,
                error ?? result.StandardError);
            while (nextToEmit < results.Length && results[nextToEmit] is { } ready)
            {
                Emit(nextToEmit, ready);
                nextToEmit++;
            }
        }

        void Emit(int index, SkillInstallResult result)
        {
            emitted[index] = true;
            var skill = skills[index];
            reportResult?.Invoke(result);
            string skillName = ActionOutputFormatter.FormatSkillName(workingDirectory, skillsDirectory, skill.LocalFolder);
            if (result.Success)
            {
                installer.reporter.Success(ActionOutputFormatter.FormatLine(
                    FormatInstallAction(skill, reportPreviewChangeStatus, beforeShas[index], ReadTreeSha(SkillFile(skillsDirectory, skill))),
                    skillName));
            }
            else
            {
                installer.reporter.Error(ActionOutputFormatter.FormatLine("Failed skill install", skillName));
                installer.reporter.Error(ActionOutputFormatter.FormatDetail(result.StandardError.Trim()));
            }

            progressAdvance?.Invoke();
        }

        // Replaces the live skill folder only once the staged install is complete; the previous
        // content is held aside until the replacement is in place and restored if it fails.
        static string? MoveIntoPlace(string stagedSkill, string target, string previous)
        {
            if (!Directory.Exists(stagedSkill))
            {
                return $"gh did not produce the expected skill folder {stagedSkill}.";
            }

            try
            {
                DeleteDirectory(previous);
                bool hadPrevious = Directory.Exists(target);
                if (hadPrevious)
                {
                    Directory.Move(target, previous);
                }

                try
                {
                    Directory.Move(stagedSkill, target);
                }
                catch (Exception exception) when (hadPrevious && !Directory.Exists(target) && exception is IOException or UnauthorizedAccessException)
                {
                    Directory.Move(previous, target);
                    throw;
                }

                DeleteDirectory(previous);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"Could not move the installed skill into {target}: {exception.Message}";
            }
        }

        static string SkillFile(string skillsDirectory, SkillManifestEntry skill)
            => Path.Combine(skillsDirectory, skill.LocalFolder, "SKILL.md");
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

sealed record SkillRemovalResult(
    string SourceRepo,
    string LocalFolder,
    string SkillsDirectory,
    bool Success,
    string? Error);
