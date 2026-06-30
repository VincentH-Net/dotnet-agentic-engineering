namespace Agentic.Check.Tests;

public sealed class SkillInstallerTests
{
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
    public async Task InstallContinuesAfterFailuresAndReportsEachResult()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "not found"));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());
        SkillManifestEntry missing = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry valid = new("owner/repo", "valid-skill", "valid-skill", TechnologyNames.Dotnet, []);

        var results = await installer.InstallAsync(
            [missing, valid],
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            Environment.CurrentDirectory,
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
    }

    [Fact]
    public async Task InstallUsesPlainRepositoryWhenSourceRefIsEmpty()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());
        SkillManifestEntry skill = new("owner/repo", "stable-skill", "stable-skill", TechnologyNames.Dotnet, []);
        string skillsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            Environment.CurrentDirectory,
            CancellationToken.None);

        var call = Assert.Single(commandRunner.Calls);
        Assert.Equal(
            ["skill", "install", "owner/repo", "stable-skill", "--dir", skillsDirectory],
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
        string skillsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        _ = await installer.InstallAsync(
            [skill],
            skillsDirectory,
            Environment.CurrentDirectory,
            CancellationToken.None);

        var call = Assert.Single(commandRunner.Calls);
        Assert.Equal(
            ["skill", "install", "owner/repo", "preview-skill", "--dir", skillsDirectory, "--pin", "main", "--force"],
            call.Arguments);
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
            OnRun = _ => tempDirectory.Write(
                ".agents/skills/preview-skill/SKILL.md",
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
            Environment.CurrentDirectory,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains("(updated     ) Installed owner/repo@main preview-skill", reporter.Successes);
    }

    [Fact]
    public async Task PreviewInstallPrefixesInstalledWhenSkillWasNotPresent()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = _ => tempDirectory.Write(
                ".agents/skills/preview-skill/SKILL.md",
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
            Environment.CurrentDirectory,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains("(installed   ) Installed owner/repo@main preview-skill", reporter.Successes);
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
            OnRun = _ => tempDirectory.Write(
                ".agents/skills/preview-skill/SKILL.md",
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
            Environment.CurrentDirectory,
            CancellationToken.None,
            reportPreviewChangeStatus: true);

        Assert.Contains("(updated     ) Installed owner/repo@main preview-skill", reporter.Successes);
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
            overwriteExisting: true,
            reportPreviewChangeStatus: true);

        Assert.Contains($"(re-installed) Copied preview-skill to {targetSkillsDirectory}", reporter.Successes);
    }
}
