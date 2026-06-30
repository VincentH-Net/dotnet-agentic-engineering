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
    public void FindInstalledFromBranchUsesLocalSkillMetadata()
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

        var result = SkillInstaller.FindInstalledFromBranch([preview, stable], [skillsDirectory]);

        var found = Assert.Single(result);
        Assert.Equal("preview-skill", found.InstallArg);
    }

    [Fact]
    public async Task InstallContinuesAfterFailuresAndReportsEachResult()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "not found"));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        SkillInstaller installer = new(commandRunner, reporter);
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
}
