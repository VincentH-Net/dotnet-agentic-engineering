namespace Agentic.Check.Tests;

public sealed class StableSkillSwitchTests
{
    const string Repository = "VincentH-Net/dotnet-agentic-engineering";
    const string SkillName = "dotnet-livecharts2";
    const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Theory]
    [InlineData("---\nmetadata:\n  github-ref: refs/tags/v1.2.3\n---\ngithub-pinned: example\n")]
    [InlineData("# Skill\ngithub-ref: refs/heads/main\ngithub-pinned: example\n")]
    public void SkillBodyExamplesDoNotTriggerStableSwitch(string content)
    {
        using TempDirectory temp = new();
        string directory = temp.CreateDirectory(".agents/skills");
        temp.Write($".agents/skills/{SkillName}/SKILL.md", content);
        SkillManifestEntry skill = new(Repository, SkillName, SkillName, TechnologyNames.Dotnet, []);

        Assert.False(SkillInstaller.RequiresStableSwitch(skill, directory));
    }

    [Theory]
    [InlineData("refs/heads/main", null, "main", true, false)]
    [InlineData("refs/heads/Main", null, "main", true, true)]
    [InlineData("refs/heads/old", null, "main", true, true)]
    [InlineData("refs/heads/main", null, "v1.2.3", false, true)]
    [InlineData("refs/heads/main", null, "main", false, true)]
    [InlineData("refs/heads/main", "main", "main", true, true)]
    [InlineData(Commit, Commit, "main", true, true)]
    [InlineData(Commit, Commit, "v1.2.3", false, true)]
    [InlineData("refs/tags/v1.2.3", null, "v1.2.3", false, false)]
    [InlineData("refs/tags/v1.0.0", null, "v1.2.3", false, false)]
    [InlineData("refs/tags/v1.2.3", "v1.2.3", "v1.2.3", false, true)]
    public void StableSwitchUsesResolvedSourceAndPinAcrossBothDirectories(
        string installedRef, string? pin, string stableRef, bool defaultBranch, bool expected)
    {
        using TempDirectory temp = new();
        string first = temp.CreateDirectory(".agents/skills");
        string second = temp.CreateDirectory(".claude/skills");
        SkillManifestEntry skill = new(Repository, SkillName, SkillName, TechnologyNames.Dotnet, [])
        {
            ResolvedSource = new(Repository, stableRef, DateTimeOffset.MinValue) { IsDefaultBranch = defaultBranch }
        };
        Assert.Empty(SkillInstaller.FindRequiringStableSwitch([skill], [first, second]));

        temp.Write($".claude/skills/{SkillName}/SKILL.md", Metadata(installedRef, pin));

        Assert.Equal(expected, SkillInstaller.RequiresStableSwitch(skill, second));
        Assert.Equal(expected ? [skill] : [], SkillInstaller.FindRequiringStableSwitch([skill], [first, second]));
        Assert.Equal(expected ? [skill] : [], SkillInstaller.FindRequiringStableSwitch([skill], [second, first]));
    }

    [Theory]
    [InlineData("refs/heads/main", "main", true)]
    [InlineData(Commit, Commit, true)]
    [InlineData(Commit, Commit, false)]
    [InlineData("refs/heads/main", null, false)]
    [InlineData("refs/heads/old", null, true)]
    public async Task StableSwitchInstallsUnpinnedOnceThenSkipsBothDirectories(string installedRef, string? pin, bool defaultBranch)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        string stableRef = defaultBranch ? "main" : "v1.2.3";
        string stableMetadata = Metadata(defaultBranch ? "refs/heads/main" : "refs/tags/v1.2.3");
        temp.Write($".agents/skills/{SkillName}/SKILL.md", stableMetadata);
        temp.Write($".claude/skills/{SkillName}/SKILL.md", Metadata(installedRef, pin));
        FakeCommandRunner runner = new()
        {
            OnRun = call =>
            {
                if (call.Arguments is ["skill", "install", ..])
                    temp.Write($".agents/skills/{SkillName}/SKILL.md", stableMetadata);
            }
        };
        QueueStableCheck(runner);
        runner.Enqueue(new(0, "installed stable", string.Empty));
        var workflow = Workflow(runner, stableRef, defaultBranch);
        AgenticCheckOptions options = new(temp.Path, false, false, null, null, "codex,claude-code", false);

        var switched = await workflow.RunAsync(options, CancellationToken.None);

        Assert.Equal(0, switched.ExitCode);
        Assert.True(Assert.Single(switched.Report.InstallResults).Success);
        Assert.True(Assert.Single(switched.Report.SkillCopyResults).Success);
        var install = Assert.Single(runner.Calls, call => call.Arguments is ["skill", "install", ..]);
        Assert.Contains("--force", install.Arguments);
        Assert.DoesNotContain("--pin", install.Arguments);
        foreach (string agent in new[] { ".agents", ".claude" })
            Assert.Equal(stableMetadata, await File.ReadAllTextAsync(Path.Combine(temp.Path, agent, "skills", SkillName, "SKILL.md")));

        runner.Calls.Clear();
        QueueStableCheck(runner);
        var repeated = await workflow.RunAsync(options, CancellationToken.None);

        Assert.Equal(0, repeated.ExitCode);
        Assert.Empty(repeated.Report.InstallResults);
        Assert.Empty(repeated.Report.SkillCopyResults);
        Assert.Equal(0, repeated.Report.OutdatedSkills);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["skill", "install", ..]
            || call.Arguments is ["skill", "update", ..] && !call.Arguments.Contains("--dry-run"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task UnpinnedStableSkillsUseUpdateChecksWithoutReinstallation(bool defaultBranch, bool hasUpdates)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        string stableRef = defaultBranch ? "main" : "v1.2.3";
        string installed = Metadata(defaultBranch ? "refs/heads/main" : "refs/tags/v1.0.0");
        foreach (string agent in new[] { ".agents", ".claude" })
            temp.Write($"{agent}/skills/{SkillName}/SKILL.md", installed);
        FakeCommandRunner runner = new();
        string update = $"  • {SkillName} ({Repository}) 52b04c64 > c9fa2d43 [main]\n  1 update(s) available:";
        QueueStableCheck(runner, hasUpdates ? update : "No updates available.");
        if (hasUpdates)
        {
            runner.Enqueue(new(0, "updated", string.Empty));
            runner.Enqueue(new(0, "updated", string.Empty));
        }

        var result = await Workflow(runner, stableRef, defaultBranch).RunAsync(
            new(temp.Path, false, false, null, null, "codex,claude-code", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Report.InstallResults);
        Assert.Empty(result.Report.SkillCopyResults);
        Assert.Equal(hasUpdates ? 1 : 0, result.Report.OutdatedSkills);
        Assert.Equal(hasUpdates ? 2 : 0, result.Report.SkillUpdates.Count);
        Assert.All(result.Report.SkillUpdates, result => Assert.True(result.Success));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["skill", "install", ..]);
    }

    static CheckWorkflow Workflow(FakeCommandRunner runner, string stableRef, bool defaultBranch)
        => new(runner, new FakePrompts { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [SkillName] },
            new NullReporter(), new FakeDirectiveSource(),
            new FakeSourceVersionResolver { StableRef = stableRef, StableUsesDefaultBranch = defaultBranch });

    static void QueueStableCheck(FakeCommandRunner runner, string output = "No updates available.")
    {
        runner.Enqueue(new(0, "gh version 2.100.0", string.Empty));
        runner.Enqueue(new(0, "gh skill help", string.Empty));
        runner.Enqueue(new(0, output, string.Empty));
        runner.Enqueue(new(0, output, string.Empty));
    }

    static string Metadata(string reference, string? pin = null)
        => $"---\nmetadata:\n  github-ref: {reference}\n  github-tree-sha: unchanged-tree\n"
            + (pin is null ? string.Empty : $"  github-pinned: {pin}\n") + "---\nSkill content\n";
}
