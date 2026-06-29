namespace Agentic.Check.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task DryRunDoesNotInstallOrApplySkillUpdates()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "Would update dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering)", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "Would update dotnet-modern-csharp-editorconfig (VincentH-Net/dotnet-agentic-engineering)", string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedSkillInstallArgs = []
            },
            reporter,
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                Path.Combine(tempDirectory.Path, ".claude", "skills"),
                Path.Combine(tempDirectory.Path, ".agents", "skills")
            ],
            result.Report.SkillsDirectories);
        Assert.Contains(result.Report.Actions, action => action.Contains("Would install", StringComparison.Ordinal));
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.Contains("install"));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "update",
            "--dir",
            Path.Combine(tempDirectory.Path, ".agents", "skills"),
            "--all",
            "--dry-run"]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "update",
            "--dir",
            Path.Combine(tempDirectory.Path, ".claude", "skills"),
            "--all",
            "--dry-run"]));
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.Contains("update") && call.Arguments.Contains("--all") && !call.Arguments.Contains("--dry-run"));
        Assert.Contains(result.Report.SkillUpdateDryRuns, update => update.StandardOutput.Contains("Would update dotnet-livecharts2", StringComparison.Ordinal));
        Assert.Contains(result.Report.SkillUpdateDryRuns, update => update.StandardOutput.Contains("Would update dotnet-modern-csharp-editorconfig", StringComparison.Ordinal));
        Assert.Contains("Would install directives into AGENTS.md:", reporter.BoldMessages);
        Assert.Contains(("Would install directives into AGENTS.md:", ToolHeader.CheckColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Would install directives into AGENTS.md:");
        Assert.Contains("  dotnet-cli-run", reporter.Infos);
        Assert.Contains("  foundation-prompt-log", reporter.Infos);
        Assert.Contains("Would install skills into skills directories:", reporter.BoldMessages);
        Assert.Contains(("Would install skills into skills directories:", ToolHeader.CheckColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Would install skills into skills directories:");
        int wouldInstallBlankLineIndex = reporter.Infos.IndexOf(string.Empty);
        Assert.True(wouldInstallBlankLineIndex >= 0);
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.BoldMessages);
        Assert.Contains("    dotnet:", reporter.BoldMessages);
        Assert.Contains(("  VincentH-Net/dotnet-agentic-engineering repo:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains(("    dotnet:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.Infos);
        Assert.Contains("    dotnet:", reporter.Infos);
        Assert.Contains("      dotnet-livecharts2", reporter.Infos);
        Assert.Contains("      dotnet-modern-csharp-editorconfig", reporter.Infos);
        Assert.Contains("  dotnet/skills repo:", reporter.BoldMessages);
        Assert.Contains("    dotnet-test:", reporter.BoldMessages);
        Assert.Contains("  dotnet/skills repo:", reporter.Infos);
        Assert.Contains("    dotnet-test:", reporter.Infos);
        Assert.Contains("      run-tests", reporter.Infos);
        Assert.Contains("Would update skills in skills directories:", reporter.BoldMessages);
        Assert.Contains(("Would update skills in skills directories:", ToolHeader.CheckColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Would update skills in skills directories:");
        Assert.Contains("      dotnet-livecharts2", reporter.Infos);
        Assert.Contains("      dotnet-modern-csharp-editorconfig", reporter.Infos);
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("Would update target-local skills", StringComparison.Ordinal));
        Assert.Contains("Directive cache duration: 30 minute(s)", reporter.Infos);
        Assert.DoesNotContain(reporter.Infos, IsLegacyDirectiveStatusMessage);
        Assert.Equal("claude-code,codex", reporter.TargetAgents);
        Assert.Contains("Scanning target directory", reporter.ProgressDescriptions);
        Assert.Equal(3, reporter.ProgressTicksByDescription["Scanning target directory"]);
    }

    [Fact]
    public async Task DryRunDoesNotAbortWhenGhSkillHelpFails()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "unknown command \"skill\""));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "unknown command \"skills\""));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "unknown command \"skill\""));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "unknown command \"skill\""));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter, new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Report.Prerequisites, check => check.Name == "gh skill" && !check.Success);
        Assert.DoesNotContain(reporter.Errors, error => error.Contains("Required tools are missing", StringComparison.Ordinal));
        Assert.Contains("Would install skills into skills directories:", reporter.BoldMessages);
        Assert.Contains(reporter.Warnings, warning => warning.Contains("Could not check target-local skills for updates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunReportsOutdatedDirectiveActions()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            ## foundation-prompt-log
            Old content.
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "No updates available.", string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedSkillInstallArgs = []
            },
            reporter,
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Would update directives in AGENTS.md:", reporter.BoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Would update directives in AGENTS.md:");
        Assert.Contains("  foundation-prompt-log", reporter.Infos);
    }

    [Fact]
    public async Task InstallFailureProducesNonZeroExitCode()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(".agents/skills/ensure-directives/SKILL.md", "# Present");
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "skill not found"));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedSkillInstallArgs = ["dotnet-modern-csharp-editorconfig"]
            },
            reporter,
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Report.InstallResults, install => !install.Success && install.StandardError.Contains("skill not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitAgentsAddDistinctSkillDirectoriesInAgentOrder()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "agents dry-run", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "claude dry-run", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "trae dry-run", string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter, new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex,claude-code,trae,trae-cn", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                Path.Combine(tempDirectory.Path, ".agents", "skills"),
                Path.Combine(tempDirectory.Path, ".claude", "skills"),
                Path.Combine(tempDirectory.Path, ".trae", "skills")
            ],
            result.Report.SkillsDirectories);
        Assert.Equal("codex,claude-code,trae,trae-cn", reporter.TargetAgents);
    }

    [Fact]
    public async Task SkillsDirectoryCannotBeCombinedWithAgents()
    {
        using TempDirectory tempDirectory = new();
        CheckWorkflow workflow = new(new FakeCommandRunner(), new FakePrompts(), new NullReporter());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, ".claude/skills", "codex", false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task ExistingFileTargetDirectoryProducesValidationErrorBeforePrerequisites()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("target-file", "not a directory");
        FakeCommandRunner commandRunner = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter);

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(Path.Combine(tempDirectory.Path, "target-file"), true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(commandRunner.Calls);
        Assert.Contains(reporter.Errors, error => error.Contains("Invalid target directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidTargetDirectoryPathProducesValidationErrorBeforePrerequisites()
    {
        FakeCommandRunner commandRunner = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter);

        var result = await workflow.RunAsync(
            new AgenticCheckOptions($"invalid{'\0'}path", true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(commandRunner.Calls);
        Assert.Contains(reporter.Errors, error => error.Contains("Invalid target directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingTargetDirectoryProducesValidationErrorBeforePrerequisites()
    {
        using TempDirectory tempDirectory = new();
        string targetDirectory = Path.Combine(tempDirectory.Path, "repo");
        FakeCommandRunner commandRunner = new();
        FakePrompts prompts = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, prompts, reporter);

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(targetDirectory, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(targetDirectory));
        Assert.Empty(commandRunner.Calls);
        Assert.Empty(prompts.ConfirmPrompts);
        Assert.Contains(reporter.Errors, error => error.Contains("Target directory does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingFileSkillsDirectoryProducesValidationErrorBeforePrerequisites()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("skills-file", "not a directory");
        FakeCommandRunner commandRunner = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter);

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, Path.Combine(tempDirectory.Path, "skills-file"), null, false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(commandRunner.Calls);
        Assert.Contains(reporter.Errors, error => error.Contains("Invalid skills directory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownAgentProducesValidationError()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "agents dry-run", string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex,unknown-agent", false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Empty(commandRunner.Calls);
    }

    [Fact]
    public async Task StandardAgentValueProducesValidationError()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "standard", false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task CodexAgentValueMapsToAgentsDirectory()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "agents dry-run", string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([Path.Combine(tempDirectory.Path, ".agents", "skills")], result.Report.SkillsDirectories);
        Assert.False(result.Report.DirectiveSummary?.CreateClaudeFile);
    }

    [Fact]
    public async Task CodexAgentOnlyDoesNotCreateClaudeFile()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts { ConfirmResult = false },
            new NullReporter(),
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(tempDirectory.Path, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "CLAUDE.md")));
    }

    [Fact]
    public async Task NormalRunReportsUpToDateItemsAsDisplayOnlySections()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            ## foundation-prompt-log
            Body for foundation-prompt-log.
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        tempDirectory.Write(".agents/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Present");
        tempDirectory.Write(".claude/skills/dotnet-livecharts2/SKILL.md", "# Present");
        tempDirectory.Write(".claude/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "No updates available.", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "No updates available.", string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedSkillInstallArgs = []
            },
            reporter,
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        int upToDateHeaderIndex = reporter.Infos.IndexOf("Up to date directives:");
        Assert.True(upToDateHeaderIndex > 0);
        Assert.Equal(string.Empty, reporter.Infos[upToDateHeaderIndex - 1]);
        Assert.Contains("Up to date directives:", reporter.BoldMessages);
        Assert.Contains(("Up to date directives:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Up to date directives:");
        Assert.Contains("  ✓ foundation-prompt-log", reporter.Successes);
        Assert.Contains("Up to date skills:", reporter.BoldMessages);
        Assert.Contains(("Up to date skills:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Up to date skills:");
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.BoldMessages);
        Assert.Contains("    dotnet:", reporter.BoldMessages);
        Assert.Contains(("  VincentH-Net/dotnet-agentic-engineering repo:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains(("    dotnet:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.Infos);
        Assert.Contains("    dotnet:", reporter.Infos);
        Assert.Contains("      ✓ dotnet-livecharts2", reporter.Successes);
        Assert.Contains("      ✓ dotnet-modern-csharp-editorconfig", reporter.Successes);
        Assert.Contains("Directive cache duration: 30 minute(s)", reporter.Infos);
        Assert.DoesNotContain(reporter.Infos, IsLegacyDirectiveStatusMessage);
    }

    [Fact]
    public async Task SkillUpdatePromptRunsScopedUpdateForEachSkillsDirectoryAndShowsSummary()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(
            0,
            """
              • dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering) 52b04c64 > c9fa2d43 [1.2.0]
              1 update(s) available:
            """,
            string.Empty));
        commandRunner.Enqueue(new CommandResult(
            0,
            """
              • dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering) 52b04c64 > c9fa2d43 [1.2.0]
              1 update(s) available:
            """,
            string.Empty));
        commandRunner.Enqueue(new CommandResult(0, string.Empty, "1 update(s) available:"));
        commandRunner.Enqueue(new CommandResult(0, string.Empty, "1 update(s) available:"));
        FakePrompts prompts = new() { ConfirmResult = true };
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, prompts, reporter, new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Update these skill(s)?", prompts.ConfirmPrompts);
        string agentsSkillsDirectory = Path.Combine(tempDirectory.Path, ".agents", "skills");
        string claudeSkillsDirectory = Path.Combine(tempDirectory.Path, ".claude", "skills");
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "update", "--dir", agentsSkillsDirectory, "--all", "--dry-run"]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "update", "--dir", claudeSkillsDirectory, "--all", "--dry-run"]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "update", "--dir", agentsSkillsDirectory, "--all"]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "update", "--dir", claudeSkillsDirectory, "--all"]));
        Assert.Contains("Found 1 skill update(s) available:", reporter.BoldMessages);
        Assert.DoesNotContain(("Found 1 skill update(s) available:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        AssertExactlyOneBlankLineBefore(reporter.Infos, "Found 1 skill update(s) available:");
        Assert.Contains(string.Empty, reporter.Infos);
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.BoldMessages);
        Assert.Contains("    dotnet:", reporter.BoldMessages);
        Assert.Contains(("  VincentH-Net/dotnet-agentic-engineering repo:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains(("    dotnet:", ToolHeader.AgenticColor), reporter.ColoredBoldMessages);
        Assert.Contains("  VincentH-Net/dotnet-agentic-engineering repo:", reporter.Infos);
        Assert.Contains("    dotnet:", reporter.Infos);
        Assert.Contains("      dotnet-livecharts2", reporter.Infos);
        Assert.Contains("      dotnet-livecharts2", reporter.PlainMessages);
        Assert.DoesNotContain("      dotnet-livecharts2", reporter.InfoMessages);
        Assert.Equal(1, reporter.OutdatedSkillCount);
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("Skills in", StringComparison.Ordinal)
            && message.Contains("with update available", StringComparison.Ordinal));
        Assert.DoesNotContain(reporter.Infos, message => message.Contains(agentsSkillsDirectory, StringComparison.Ordinal));
        Assert.DoesNotContain(reporter.Infos, message => message.Contains(claudeSkillsDirectory, StringComparison.Ordinal));
        Assert.DoesNotContain(reporter.Infos, message => message.Contains("1 update(s) available", StringComparison.Ordinal));
        Assert.Empty(reporter.Warnings);
        Assert.Contains("Scanning target directory", reporter.ProgressDescriptions);
        Assert.Equal(3, reporter.ProgressTicksByDescription["Scanning target directory"]);
        Assert.Contains("Updating skills", reporter.ProgressDescriptions);
        Assert.Equal(2, reporter.ProgressTicksByDescription["Updating skills"]);
        Assert.Contains("Updated 1 skill(s) successfully.", reporter.Successes);
    }

    [Fact]
    public async Task OutdatedSkillSummaryCountsSameSkillOnceAcrossSkillDirectories()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        tempDirectory.Write(".agents/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Present");
        tempDirectory.Write(".claude/skills/dotnet-livecharts2/SKILL.md", "# Present");
        tempDirectory.Write(".claude/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(
            0,
            """
              • dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering) 52b04c64 > c9fa2d43 [1.2.0]
              1 update(s) available:
            """,
            string.Empty));
        commandRunner.Enqueue(new CommandResult(
            0,
            """
              • dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering) 52b04c64 > c9fa2d43 [1.2.0]
              1 update(s) available:
            """,
            string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), reporter, new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Report.OutdatedSkills);
        Assert.Equal(1, reporter.OutdatedSkillCount);
        Assert.Contains("      dotnet-livecharts2", reporter.Infos);
        Assert.DoesNotContain("  1 update(s) available:", reporter.Infos);
    }

    [Fact]
    public async Task SkillUpdateDoesNotPromptWhenDryRunReportsNoUpdates()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "No updates available.", string.Empty));
        FakePrompts prompts = new() { ConfirmResult = true };
        CheckWorkflow workflow = new(commandRunner, prompts, new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Update these skill(s)?", prompts.ConfirmPrompts);
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "update",
            "--dir",
            Path.Combine(tempDirectory.Path, ".agents", "skills"),
            "--all"]));
        Assert.Contains(result.Report.Actions, action => action.Equals("No target-local skill updates found.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InteractiveSelectionOnlyAppliesSelectedDirectives()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        FakePrompts prompts = new()
        {
            ConfirmResult = false,
            SelectedDirectiveNames = ["foundation-prompt-log"],
            SelectedSkillInstallArgs = []
        };
        CheckWorkflow workflow = new(commandRunner, prompts, new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        Assert.Contains("dotnet-agentic-engineering:foundation-prompt-log:start", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-agentic-engineering:dotnet-cli-run:start", agents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveSelectionInstallsSelectedSkillDependencies()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call =>
            {
                if (!call.Arguments.Contains("install"))
                {
                    return;
                }

                string localFolder = call.Arguments[3].Split('/')[^1];
                tempDirectory.Write(Path.Combine(".agents", "skills", localFolder, "SKILL.md"), "# Installed");
            }
        };
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "installed filter-syntax", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "installed platform-detection", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "installed run-tests", string.Empty));
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedDirectiveNames = [],
                SelectedSkillInstallArgs = ["plugins/dotnet-test/skills/run-tests"]
            },
            new NullReporter(),
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "install",
            "dotnet/skills",
            "plugins/dotnet-test/skills/filter-syntax",
            "--dir",
            Path.Combine(tempDirectory.Path, ".agents", "skills")]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "install",
            "dotnet/skills",
            "plugins/dotnet-test/skills/platform-detection",
            "--dir",
            Path.Combine(tempDirectory.Path, ".agents", "skills")]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual([
            "skill",
            "install",
            "dotnet/skills",
            "plugins/dotnet-test/skills/run-tests",
            "--dir",
            Path.Combine(tempDirectory.Path, ".agents", "skills")]));
    }

    [Fact]
    public async Task InstallsIntoFirstAgentDirectoryAndCopiesToRemainingDirectories()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(".agents/skills/ensure-directives/SKILL.md", "# Present");
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new()
        {
            OnRun = call =>
            {
                if (call.Arguments.Contains("install"))
                {
                    tempDirectory.Write(".claude/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Installed");
                }
            }
        };
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts
            {
                SelectedSkillInstallArgs = ["dotnet-modern-csharp-editorconfig"]
            },
            reporter,
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        _ = Assert.Single(commandRunner.Calls, call => call.Arguments.Contains("install"));
        Assert.True(File.Exists(Path.Combine(tempDirectory.Path, ".agents", "skills", "dotnet-modern-csharp-editorconfig", "SKILL.md")));
        Assert.Contains("Installing skills", reporter.ProgressDescriptions);
        Assert.Equal(2, reporter.ProgressTicksByDescription["Installing skills"]);
    }

    static void AssertExactlyOneBlankLineBefore(List<string> messages, string header)
    {
        int headerIndex = -1;
        for (int index = 0; index < messages.Count; index++)
        {
            if (messages[index].Equals(header, StringComparison.Ordinal))
            {
                headerIndex = index;
                break;
            }
        }

        Assert.True(headerIndex > 0, $"Expected '{header}' to have a preceding blank line.");
        Assert.Equal(string.Empty, messages[headerIndex - 1]);
        if (headerIndex > 1)
        {
            Assert.NotEqual(string.Empty, messages[headerIndex - 2]);
        }
    }

    static bool IsLegacyDirectiveStatusMessage(string message)
        => message.StartsWith("Directive ", StringComparison.Ordinal)
            && !message.StartsWith("Directive cache duration:", StringComparison.Ordinal);
}
