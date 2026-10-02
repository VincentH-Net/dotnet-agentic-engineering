namespace Agentic.Check.Tests;

public sealed class SkillInstallerTests
{
    static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public void FindMissingUsesLocalSkillFolder()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(".agents/skills/present-skill/SKILL.md", "# Present");
        SkillManifestEntry present = new("owner/repo", "present-skill", "present-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry missing = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);

        var result = SkillInstaller.FindMissing([present, missing], skillsDirectory);

        var found = Assert.Single(result);
        Assert.Equal("missing-skill", found.InstallArg);
    }

    [Fact]
    public void FindRequiringStableSwitchUsesLocalSkillMetadata()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(
            ".agents/skills/preview-skill/SKILL.md",
            """
            ---
            metadata:
                github-ref: refs/heads/main
            ---
            # Preview
            """);
        tempDirectory.Write(
            ".agents/skills/stable-skill/SKILL.md",
            """
            ---
            metadata:
                github-ref: refs/tags/2.0.0
            ---
            # Stable
            """);
        SkillManifestEntry preview = new("owner/repo", "preview-skill", "preview-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry stable = new("owner/repo", "stable-skill", "stable-skill", TechnologyNames.Dotnet, []);

        var result = SkillInstaller.FindRequiringStableSwitch([preview, stable], [skillsDirectory]);

        var found = Assert.Single(result);
        Assert.Equal("preview-skill", found.InstallArg);
    }

    [Fact]
    public void StagingDirectoryIsASiblingOfTheSkillsDirectory()
    {
        string skillsDirectory = Path.Combine("repo", ".agents", "skills");

        Assert.Equal(Path.Combine("repo", ".agents", SkillInstaller.StagingFolderName), SkillInstaller.StagingRoot(skillsDirectory));
        Assert.Equal(Path.Combine("repo", ".agents", SkillInstaller.StagingFolderName, "sample"), SkillInstaller.StagingDirectory(skillsDirectory + Path.DirectorySeparatorChar, "sample"));
    }

    [Fact]
    public async Task InstallContinuesAfterFailuresAndReportsEachResultWithoutRetrying()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call =>
            {
                if (call.Arguments[3] == "valid-skill")
                    FakeGh.WriteInstalledSkill(call, "# Valid");
            }
        };
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "not found"));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        List<TimeSpan> waits = [];
        SkillInstaller installer = new(commandRunner, reporter, delay: RecordWaits(waits));
        SkillManifestEntry missing = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry valid = new("owner/repo", "valid-skill", "valid-skill", TechnologyNames.Dotnet, []);

        var results = await installer.InstallAsync(
            [missing, valid],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Collection(
            results,
            result =>
            {
                Assert.False(result.Success);
                Assert.Equal("missing-skill", result.InstallArg);
                Assert.Contains("not found", result.StandardError, StringComparison.Ordinal);
            },
            result =>
            {
                Assert.True(result.Success);
                Assert.Equal("valid-skill", result.InstallArg);
            });
        Assert.Contains(ActionOutputFormatter.FormatLine("Failed skill install", Path.Combine(".agents", "skills", "missing-skill")), reporter.Errors);
        Assert.Contains(ActionOutputFormatter.FormatDetail("not found"), reporter.Errors);
        Assert.Empty(waits);
        Assert.Equal(2, commandRunner.Calls.Count);
    }

    [Fact]
    public async Task InstallUsesPlainRepositoryAndStagingDirectoryWhenSourceRefIsEmpty()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());
        SkillManifestEntry skill = new("owner/repo", "stable-skill", "stable-skill", TechnologyNames.Dotnet, []);
        using TempDirectory tempDirectory = new();
        string skillsDirectory = Path.Combine(tempDirectory.Path, ".agents", "skills");

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            Environment.CurrentDirectory,
            CancellationToken.None);

        var call = Assert.Single(commandRunner.Calls);
        Assert.Equal(
            ["skill", "install", "owner/repo", "stable-skill", "--dir", SkillInstaller.StagingDirectory(skillsDirectory, "stable-skill")],
            call.Arguments);
    }

    [Fact]
    public async Task InstallUsesPinFlagWhenSourceRefIsSet()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            sourceRef: "main");
        using TempDirectory tempDirectory = new();
        string skillsDirectory = Path.Combine(tempDirectory.Path, ".agents", "skills");

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            Environment.CurrentDirectory,
            CancellationToken.None);

        var call = Assert.Single(commandRunner.Calls);
        Assert.Equal(
            ["skill", "install", "owner/repo", "preview-skill", "--dir", SkillInstaller.StagingDirectory(skillsDirectory, "preview-skill"), "--pin", "main", "--force"],
            call.Arguments);
    }

    [Fact]
    public async Task InstallMovesTheStagedSkillIntoPlaceAndRemovesStaging()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call =>
            {
                FakeGh.WriteInstalledSkill(call, "# Sample");
                FakeGh.WriteInstalledSkill(call, "echo", Path.Combine("scripts", "run.sh"));
            }
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);

        var results = await installer.InstallAsync([Skill("sample")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.True(Assert.Single(results).Success);
        Assert.Equal("# Sample", await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "sample", "SKILL.md")));
        Assert.Equal("echo", await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "sample", "scripts", "run.sh")));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
        Assert.Contains(ActionOutputFormatter.FormatLine("Installed skill", Path.Combine(".agents", "skills", "sample")), reporter.Successes);
    }

    [Fact]
    public async Task FailedInstallLeavesNoPartialSkillFolder()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new()
        {
            // gh writes SKILL.md before the later files it then fails to fetch.
            OnRun = call => FakeGh.WriteInstalledSkill(call, "# Partial")
        };
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "could not fetch references/notes.md: HTTP 500"));
        SkillInstaller installer = new(commandRunner, new NullReporter());

        var results = await installer.InstallAsync([Skill("sample")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.False(Assert.Single(results).Success);
        Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "sample")));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
        Assert.True(SkillInstaller.IsMissing(Skill("sample"), skillsDirectory));
    }

    [Fact]
    public async Task FailedForcedReinstallKeepsTheExistingSkillIntact()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(".agents/skills/sample/SKILL.md", "# Old");
        tempDirectory.Write(".agents/skills/sample/references/notes.md", "old notes");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(call, "# Partial")
        };
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "could not fetch references/notes.md: HTTP 500"));
        SkillInstaller installer = new(commandRunner, new NullReporter());

        var results = await installer.InstallAsync([Skill("sample", forceInstall: true)], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.False(Assert.Single(results).Success);
        Assert.Equal("# Old", await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "sample", "SKILL.md")));
        Assert.Equal("old notes", await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "sample", "references", "notes.md")));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
    }

    [Fact]
    public async Task SuccessfulReinstallReplacesTheExistingSkillFolderCompletely()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(".agents/skills/sample/SKILL.md", "# Old");
        tempDirectory.Write(".agents/skills/sample/stale.md", "no longer part of the skill");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(call, "# New")
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());

        var results = await installer.InstallAsync([Skill("sample", forceInstall: true)], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.True(Assert.Single(results).Success);
        Assert.Equal("# New", await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "sample", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(skillsDirectory, "sample", "stale.md")));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
    }

    [Fact]
    public async Task SuccessWithoutAStagedSkillFolderIsReportedAsFailure()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "nothing written", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);

        var results = await installer.InstallAsync([Skill("sample")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("did not produce the expected skill folder", result.StandardError, StringComparison.Ordinal);
        Assert.Contains(ActionOutputFormatter.FormatLine("Failed skill install", Path.Combine(".agents", "skills", "sample")), reporter.Errors);
        Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "sample")));
    }

    [Fact]
    public async Task LeftoverStagingFromAnInterruptedRunIsRemovedAndNeverInstalled()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        string leftover = Path.Combine(SkillInstaller.StagingDirectory(skillsDirectory, "orphan"), "orphan", "SKILL.md");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        await File.WriteAllTextAsync(leftover, "# Interrupted");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(call, "# Sample")
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());

        _ = await installer.InstallAsync([Skill("sample")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
        Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "orphan")));
        Assert.True(File.Exists(Path.Combine(skillsDirectory, "sample", "SKILL.md")));
    }

    [Fact]
    public async Task InstallRunsConcurrentlyUpToTheCapAndReportsInManifestOrder()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        System.Collections.Concurrent.ConcurrentDictionary<string, (CommandCall Call, TaskCompletionSource<CommandResult> Completion)> pending = new(StringComparer.Ordinal);
        ScriptedCommandRunner commandRunner = new(call =>
        {
            TaskCompletionSource<CommandResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[call.Arguments[3]] = (call, completion);
            return completion.Task;
        });
        RecordingReporter reporter = new();
        List<SkillInstallResult> reported = [];
        int advances = 0;
        SkillInstaller installer = new(commandRunner, reporter, maxConcurrentInstalls: 2);

        var install = installer.InstallAsync(
            [Skill("a"), Skill("b"), Skill("c")],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            progressAdvance: () => advances++,
            reportResult: reported.Add);

        Assert.Equal(["a", "b"], commandRunner.Calls.Select(call => call.Arguments[3]));
        Assert.False(install.IsCompleted);

        Complete(pending["b"]);
        await WaitUntilAsync(() => commandRunner.Calls.Count == 3);
        Assert.Equal("c", commandRunner.Calls[2].Arguments[3]);
        Assert.Empty(reported);
        Assert.Empty(reporter.Successes);

        Complete(pending["a"]);
        await WaitUntilAsync(() => reported.Count == 2);
        Assert.Equal(["a", "b"], reported.Select(result => result.LocalFolder));
        Assert.Equal(2, advances);
        Assert.Equal(["a", "b"], reporter.Successes.Select(line => line.Split(Path.DirectorySeparatorChar)[^1]));

        Complete(pending["c"]);
        var results = await install.ConfigureAwait(true);

        Assert.Equal(["a", "b", "c"], results.Select(result => result.LocalFolder));
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(3, advances);
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
        foreach (string skill in new[] { "a", "b", "c" })
            Assert.True(File.Exists(Path.Combine(skillsDirectory, skill, "SKILL.md")));

        static void Complete((CommandCall Call, TaskCompletionSource<CommandResult> Completion) entry)
        {
            FakeGh.WriteInstalledSkill(entry.Call, "# Installed");
            entry.Completion.SetResult(new CommandResult(0, "installed", string.Empty));
        }
    }

    [Fact]
    public async Task SecondaryLimitWaitsThenRetriesTheLimitedSkillOneAtATime()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        List<TimeSpan> waits = [];
        List<string> warnings = [];
        int attemptsOfB = 0;
        ScriptedCommandRunner commandRunner = new(call =>
        {
            if (call.Arguments[3] == "b" && Interlocked.Increment(ref attemptsOfB) == 1)
                throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true);
            FakeGh.WriteInstalledSkill(call, "# Installed");
            return Task.FromResult(new CommandResult(0, "installed", string.Empty));
        });
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter, maxConcurrentInstalls: 3, delay: RecordWaits(waits));

        var results = await installer.InstallAsync(
            [Skill("a"), Skill("b"), Skill("c")],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            recordWarning: warnings.Add);

        Assert.Equal([TimeSpan.FromSeconds(60)], waits);
        Assert.Equal(["a", "b", "c", "b"], commandRunner.Calls.Select(call => call.Arguments[3]));
        Assert.Equal(["a", "b", "c"], results.Select(result => result.LocalFolder));
        Assert.All(results, result => Assert.True(result.Success));
        string notice = Assert.Single(warnings);
        Assert.Contains("Waiting 60 seconds", notice, StringComparison.Ordinal);
        Assert.Contains("1 skill install(s) one at a time", notice, StringComparison.Ordinal);
        Assert.Equal([notice], reporter.Warnings);
        Assert.Empty(reporter.Errors);
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
    }

    [Fact]
    public async Task SecondaryLimitDefersUnlaunchedSkillsUntilAfterTheWait()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        List<TimeSpan> waits = [];
        int attemptsOfA = 0;
        List<int> waitsSeenAtLaunch = [];
        ScriptedCommandRunner commandRunner = new(call =>
        {
            waitsSeenAtLaunch.Add(waits.Count);
            if (call.Arguments[3] == "a" && Interlocked.Increment(ref attemptsOfA) == 1)
                throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true);
            FakeGh.WriteInstalledSkill(call, "# Installed");
            return Task.FromResult(new CommandResult(0, "installed", string.Empty));
        });
        SkillInstaller installer = new(commandRunner, new NullReporter(), maxConcurrentInstalls: 1, delay: RecordWaits(waits));

        var results = await installer.InstallAsync([Skill("a"), Skill("b"), Skill("c")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(["a", "a", "b", "c"], commandRunner.Calls.Select(call => call.Arguments[3]));
        Assert.Equal([0, 1, 1, 1], waitsSeenAtLaunch);
        Assert.All(results, result => Assert.True(result.Success));
    }

    [Fact]
    public async Task RepeatedSecondaryLimitWaitsLongerBeforeTheFinalAttempt()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        List<TimeSpan> waits = [];
        int attempts = 0;
        ScriptedCommandRunner commandRunner = new(call =>
        {
            if (Interlocked.Increment(ref attempts) <= 2)
                throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true);
            FakeGh.WriteInstalledSkill(call, "# Installed");
            return Task.FromResult(new CommandResult(0, "installed", string.Empty));
        });
        SkillInstaller installer = new(commandRunner, new NullReporter(), delay: RecordWaits(waits));

        var results = await installer.InstallAsync([Skill("sample")], skillsDirectory, tempDirectory.Path, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)], waits);
        Assert.Equal(3, commandRunner.Calls.Count);
        Assert.True(Assert.Single(results).Success);
    }

    [Fact]
    public async Task PersistentSecondaryLimitStopsAfterTheLastWaitAndReportsCompletedInstalls()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        List<TimeSpan> waits = [];
        List<SkillInstallResult> reported = [];
        ScriptedCommandRunner commandRunner = new(call =>
        {
            if (call.Arguments[3] == "b")
                throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true);
            FakeGh.WriteInstalledSkill(call, "# Installed");
            return Task.FromResult(new CommandResult(0, "installed", string.Empty));
        });
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter, delay: RecordWaits(waits));

        var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => installer.InstallAsync(
            [Skill("a"), Skill("b"), Skill("c")],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            reportResult: reported.Add));

        Assert.True(error.IsSecondary);
        Assert.Equal(GitHubRateLimits.SecondaryMessage, error.Message);
        Assert.Equal(SkillInstaller.SecondaryLimitWaits, waits);
        Assert.Equal(3, commandRunner.Calls.Count(call => call.Arguments[3] == "b"));
        Assert.Equal(["a", "c"], reported.Select(result => result.LocalFolder));
        Assert.Equal(["a", "c"], reporter.Successes.Select(line => line.Split(Path.DirectorySeparatorChar)[^1]));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
        Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "b")));
    }

    [Fact]
    public async Task PrimaryLimitStopsWithoutWaitingAndReportsCompletedInstalls()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        List<TimeSpan> waits = [];
        List<SkillInstallResult> reported = [];
        ScriptedCommandRunner commandRunner = new(call =>
        {
            if (call.Arguments[3] == "b")
                throw new GitHubRateLimitException(GitHubRateLimits.PrimaryMessage(true), isSecondary: false);
            FakeGh.WriteInstalledSkill(call, "# Installed");
            return Task.FromResult(new CommandResult(0, "installed", string.Empty));
        });
        SkillInstaller installer = new(commandRunner, new NullReporter(), delay: RecordWaits(waits));

        var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => installer.InstallAsync(
            [Skill("a"), Skill("b"), Skill("c")],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            reportResult: reported.Add));

        Assert.False(error.IsSecondary);
        Assert.Equal(GitHubRateLimits.PrimaryMessage(true), error.Message);
        Assert.Empty(waits);
        Assert.Equal(1, commandRunner.Calls.Count(call => call.Arguments[3] == "b"));
        Assert.Equal(["a", "c"], reported.Select(result => result.LocalFolder));
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
    }

    [Fact]
    public async Task CancellationDuringTheWaitStopsTheInstall()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        using CancellationTokenSource cancellation = new();
        ScriptedCommandRunner commandRunner = new(_ => throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage, isSecondary: true));
        SkillInstaller installer = new(commandRunner, new NullReporter(), delay: async (_, token) =>
        {
            await cancellation.CancelAsync().ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
        });

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(
            [Skill("sample")], skillsDirectory, tempDirectory.Path, cancellation.Token));

        _ = Assert.Single(commandRunner.Calls);
        Assert.False(Directory.Exists(SkillInstaller.StagingRoot(skillsDirectory)));
    }

    [Fact]
    public async Task PreviewInstallPrefixesUpdatedWhenTreeShaChanges()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(
            ".agents/skills/preview-skill/SKILL.md",
            """
            ---
            metadata:
                github-tree-sha: old-sha
            ---
            # Preview
            """);
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(
                call,
                """
                ---
                metadata:
                    github-tree-sha: new-sha
                ---
                # Preview
                """)
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            sourceRef: "main");

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains(ActionOutputFormatter.FormatLine("Updated skill", Path.Combine(".agents", "skills", "preview-skill")), reporter.Successes);
    }

    [Fact]
    public async Task PreviewInstallPrefixesInstalledWhenSkillWasNotPresent()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(
                call,
                """
                ---
                metadata:
                    github-tree-sha: new-sha
                ---
                # Preview
                """)
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            sourceRef: "main");

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains(ActionOutputFormatter.FormatLine("Installed skill", Path.Combine(".agents", "skills", "preview-skill")), reporter.Successes);
    }

    [Fact]
    public async Task PreviewInstallPrefixesUpdatedWhenFlatTreeShaChanges()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(
            ".agents/skills/preview-skill/SKILL.md",
            """
            ---
            github-tree-sha: old-sha
            ---
            # Preview
            """);
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call => FakeGh.WriteInstalledSkill(
                call,
                """
                ---
                github-tree-sha: new-sha
                ---
                # Preview
                """)
        };
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            sourceRef: "main");

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            tempDirectory.Path,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains(ActionOutputFormatter.FormatLine("Updated skill", Path.Combine(".agents", "skills", "preview-skill")), reporter.Successes);
    }

    [Fact]
    public void PreviewCopyPrefixesReinstalledWhenTreeShaIsUnchanged()
    {
        using TempDirectory tempDirectory = new();
        string sourceSkillsDirectory = tempDirectory.CreateDirectory(".claude/skills");
        string targetSkillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        const string skillContent = """
            ---
            github-tree-sha: same-sha
            ---
            # Preview
            """;
        tempDirectory.Write(".claude/skills/preview-skill/SKILL.md", skillContent);
        tempDirectory.Write(".agents/skills/preview-skill/SKILL.md", skillContent);
        RecordingReporter reporter = new();
        SkillInstaller installer = new(new FakeCommandRunner(), reporter);
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            sourceRef: "main");

        _ = installer.CopyInstalledSkills(
            [skill],
            sourceSkillsDirectory,
            [targetSkillsDirectory],
            tempDirectory.Path,
            overwriteExisting: true,
            reportPreviewChangeStatus: true);

        Assert.Contains(ActionOutputFormatter.FormatLine("Re-installed skill", Path.Combine(".agents", "skills", "preview-skill")), reporter.Successes);
    }

    [Fact]
    public void CopyFailureUsesActionTableOutput()
    {
        using TempDirectory tempDirectory = new();
        string sourceSkillsDirectory = tempDirectory.CreateDirectory(".claude/skills");
        string targetSkillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        RecordingReporter reporter = new();
        SkillInstaller installer = new(new FakeCommandRunner(), reporter);
        SkillManifestEntry skill = new(
            "owner/repo",
            "missing-source",
            "missing-source",
            TechnologyNames.Dotnet,
            []);

        _ = installer.CopyInstalledSkills(
            [skill],
            sourceSkillsDirectory,
            [targetSkillsDirectory],
            tempDirectory.Path,
            overwriteExisting: true);

        Assert.Contains(ActionOutputFormatter.FormatLine("Failed skill copy", Path.Combine(".agents", "skills", "missing-source")), reporter.Errors);
        Assert.Contains(reporter.Errors, error => error.StartsWith("    Source skill directory was not found:", StringComparison.Ordinal));
    }

    static SkillManifestEntry Skill(string name, bool forceInstall = false)
        => new("owner/repo", name, name, TechnologyNames.Dotnet, [], forceInstall: forceInstall);

    static Func<TimeSpan, CancellationToken, Task> RecordWaits(List<TimeSpan> waits)
        => (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        };

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow - started < WaitLimit, "Timed out waiting for the installer to make progress.");
            await Task.Delay(10).ConfigureAwait(true);
        }
    }
}
