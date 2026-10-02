namespace Agentic.Check.Tests;

public sealed class SkillUpdateNoticeTests
{
    const string Repository = "VincentH-Net/dotnet-agentic-engineering";
    const string NoMetadataSkill = "dotnet-livecharts2";
    const string UpdatedSkill = "dotnet-modern-csharp-editorconfig";

    // The exact gh skill update --dry-run output (stdout, then stderr) for one real update next to a skill it skips.
    const string UpdateLine = $"  • {UpdatedSkill} (vincenth-net/dotnet-agentic-engineering)  > 02299f49 [v2.3.0]";
    const string NoMetadataNoticeText = $"{NoMetadataSkill} has no GitHub metadata. Run `gh skill update {NoMetadataSkill}` interactively to add metadata, or reinstall to enable updates";
    const string NoMetadataNotice = $"! {NoMetadataNoticeText}";
    const string PinnedNoticeText = $"{NoMetadataSkill} is pinned to v1.0.0; run with --unpin to update it";
    const string PinnedNotice = $"! {PinnedNoticeText}";
    const string SkillWithoutMetadata = $"---\nname: {NoMetadataSkill}\ndescription: Hand-copied skill.\n---\n# Skill\n";
    const string SkillWithMetadata = $"---\nname: {NoMetadataSkill}\nmetadata:\n  github-ref: refs/tags/v1.2.3\n  github-tree-sha: 02299f49\n---\n# Skill\n";

    [Theory]
    [InlineData(NoMetadataNoticeText, NoMetadataSkill)]
    [InlineData("cli-e2e-testing has no GitHub metadata. Run `gh skill update cli-e2e-testing` interactively to add metadata, or reinstall to enable updates", "cli-e2e-testing")]
    [InlineData(PinnedNoticeText, null)]
    [InlineData("has no GitHub metadata", null)]
    public void ParseSkillWithoutMetadataReadsTheSkillNameFromGhNotices(string notice, string? expected)
        => Assert.Equal(expected, CheckWorkflow.ParseSkillWithoutMetadata(notice));

    [Fact]
    public void ExtractNoticesReturnsTrimmedDistinctNoticeLinesFromSuccessfulRunsOnly()
    {
        CommandReport report = new(0, UpdateLine, $"{NoMetadataNotice}\n{NoMetadataNotice}\n\n1 update(s) available:\n");

        Assert.Equal([NoMetadataNoticeText], CheckWorkflow.ExtractNotices(report));
        Assert.Empty(CheckWorkflow.ExtractNotices(report with { ExitCode = 1 }));
    }

    [Fact]
    public async Task DryRunKeepsNoticesOutOfTheUpdateListAndOffersReinstall()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        foreach (string agent in new[] { ".agents", ".claude" })
        {
            temp.Write($"{agent}/skills/{NoMetadataSkill}/SKILL.md", SkillWithoutMetadata);
            temp.Write($"{agent}/skills/{UpdatedSkill}/SKILL.md", SkillWithMetadata);
        }

        FakeCommandRunner runner = new();
        QueueChecks(runner, UpdateLine, $"{NoMetadataNotice}\n\n1 update(s) available:\n");
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter).RunAsync(new(temp.Path, true, false, null, null, null, false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Report.OutdatedSkills);
        Assert.Equal(1, reporter.OutdatedSkillCount);
        Assert.Contains("Would update skills in skills directories:", reporter.BoldMessages);
        Assert.Contains($"      {UpdatedSkill}", reporter.Infos);
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("no GitHub metadata", StringComparison.Ordinal));
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("unknown source", StringComparison.Ordinal));
        Assert.Empty(reporter.Warnings);
        foreach (string agent in new[] { ".agents", ".claude" })
            Assert.Contains($"Would install {Repository} {NoMetadataSkill} into {Path.Combine(temp.Path, agent, "skills")}.", result.Report.Actions);
        Assert.DoesNotContain(result.Report.Actions, action => action.Contains($"Would install {Repository} {UpdatedSkill} ", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("install"));
    }

    [Fact]
    public async Task OtherNoticesBecomeWarningsWithoutInstallOrUpdateActions()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        foreach (string agent in new[] { ".agents", ".claude" })
            temp.Write($"{agent}/skills/{NoMetadataSkill}/SKILL.md", SkillWithMetadata);
        FakeCommandRunner runner = new();
        QueueChecks(runner, string.Empty, $"{PinnedNotice}\nAll skills are up to date.\n");
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter).RunAsync(new(temp.Path, true, false, null, null, null, false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Report.OutdatedSkills);
        Assert.Equal([$"gh skill update: {PinnedNoticeText}"], reporter.Warnings);
        Assert.DoesNotContain(result.Report.Actions, action => action.Contains($"Would install {Repository} {NoMetadataSkill} ", StringComparison.Ordinal));
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("pinned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MetadataNoticeTriggersOneForcedReinstallThatReachesBothDirectoriesThenStops()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        foreach (string agent in new[] { ".agents", ".claude" })
            temp.Write($"{agent}/skills/{NoMetadataSkill}/SKILL.md", SkillWithoutMetadata);
        FakeCommandRunner runner = new()
        {
            OnRun = call =>
            {
                if (call.Arguments is ["skill", "install", ..])
                    temp.Write($".agents/skills/{NoMetadataSkill}/SKILL.md", SkillWithMetadata);
            }
        };
        QueueChecks(runner, string.Empty, $"{NoMetadataNotice}\nAll skills are up to date.\n");
        runner.Enqueue(new(0, "installed", string.Empty));
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [NoMetadataSkill] };
        RecordingReporter reporter = new();
        AgenticCheckOptions options = new(temp.Path, false, false, null, null, "codex,claude-code", false);
        string agentsSkills = Path.Combine(temp.Path, ".agents", "skills");

        var reinstalled = await Workflow(runner, reporter, prompts).RunAsync(options, CancellationToken.None);

        Assert.Equal(0, reinstalled.ExitCode);
        var action = Assert.Single(prompts.RecommendedSkillActions, skill => skill.InstallArg == NoMetadataSkill);
        Assert.Equal(SkillInstaller.MetadataReinstallAction, action.RecommendationAction);
        Assert.True(action.ForceInstall);
        var install = Assert.Single(runner.Calls, call => call.Arguments is ["skill", "install", ..]);
        Assert.Equal(["skill", "install", Repository, NoMetadataSkill, "--dir", agentsSkills, "--force"], install.Arguments);
        Assert.True(Assert.Single(reinstalled.Report.InstallResults).Success);
        Assert.True(Assert.Single(reinstalled.Report.SkillCopyResults).Success);
        foreach (string agent in new[] { ".agents", ".claude" })
            Assert.Equal(SkillWithMetadata, await File.ReadAllTextAsync(Path.Combine(temp.Path, agent, "skills", NoMetadataSkill, "SKILL.md")));
        Assert.Equal(2, reporter.Successes.Count(message => message.StartsWith("  Re-installed skill", StringComparison.Ordinal)));
        Assert.DoesNotContain($"      ✓ {NoMetadataSkill}", reporter.Successes);
        Assert.Empty(reporter.Warnings);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["skill", "update", ..] && !call.Arguments.Contains("--dry-run"));

        runner.Calls.Clear();
        QueueChecks(runner, string.Empty, "All skills are up to date.\n");
        RecordingReporter repeatedReporter = new();
        FakePrompts repeatedPrompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [NoMetadataSkill] };

        var repeated = await Workflow(runner, repeatedReporter, repeatedPrompts).RunAsync(options, CancellationToken.None);

        Assert.Equal(0, repeated.ExitCode);
        Assert.Empty(repeated.Report.InstallResults);
        Assert.Empty(repeated.Report.SkillCopyResults);
        Assert.DoesNotContain(repeatedPrompts.RecommendedSkillActions, skill => skill.InstallArg == NoMetadataSkill);
        Assert.Contains($"      ✓ {NoMetadataSkill}", repeatedReporter.Successes);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["skill", "install", ..]);
    }

    [Fact]
    public async Task UpdateSummaryCountsTheSkillsGhReportsAsUpdated()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner runner = new();
        string dryRun = $"  • {NoMetadataSkill} ({Repository}) 52b04c64 > c9fa2d43 [1.2.0]\n  1 update(s) available:";
        QueueChecks(runner, dryRun, string.Empty);
        for (int i = 0; i < 2; i++)
            runner.Enqueue(new(0, $"  • {NoMetadataSkill} ({Repository}) 52b04c64 > c9fa2d43 [1.2.0]\nUpdated {NoMetadataSkill}\n", "1 update(s) available:\n"));
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter, new FakePrompts { ConfirmResult = true }).RunAsync(new(temp.Path, false, false, null, null, null, false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result.Report.SkillUpdates.Count);
        Assert.Contains("Updated 1 skill(s) successfully.", reporter.Successes);
        Assert.Empty(reporter.Warnings);
    }

    [Fact]
    public async Task UpdateThatOnlyPrintsNoticesWarnsInsteadOfClaimingSuccess()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner runner = new();
        string dryRun = $"  • {NoMetadataSkill} ({Repository}) 52b04c64 > c9fa2d43 [1.2.0]\n  1 update(s) available:";
        QueueChecks(runner, dryRun, string.Empty);
        for (int i = 0; i < 2; i++)
            runner.Enqueue(new(0, string.Empty, $"{PinnedNotice}\nAll skills are up to date.\n"));
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter, new FakePrompts { ConfirmResult = true }).RunAsync(new(temp.Path, false, false, null, null, null, false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [$"gh skill update: {PinnedNoticeText}", "gh skill update changed no skills."],
            reporter.Warnings);
        Assert.DoesNotContain(reporter.Successes, message => message.Contains("successfully", StringComparison.Ordinal));
    }

    static CheckWorkflow Workflow(FakeCommandRunner runner, RecordingReporter reporter, FakePrompts? prompts = null)
        => new(runner, prompts ?? new FakePrompts { SelectedSkillInstallArgs = [] }, reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver());

    // Prerequisite checks, then one gh skill update --dry-run per skills directory.
    static void QueueChecks(FakeCommandRunner runner, string dryRunOutput, string dryRunError)
    {
        runner.Enqueue(new(0, "gh version 2.101.0", string.Empty));
        runner.Enqueue(new(0, "gh skill help", string.Empty));
        runner.Enqueue(new(0, dryRunOutput, dryRunError));
        runner.Enqueue(new(0, dryRunOutput, dryRunError));
    }
}
