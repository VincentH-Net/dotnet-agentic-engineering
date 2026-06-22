namespace Agentic.Check.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task DryRunDoesNotInstallOrUpdateSkills()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Report.Actions, action => action.Contains("Would install", StringComparison.Ordinal));
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.Contains("install"));
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.Contains("update"));
    }

    [Fact]
    public async Task InstallFailureProducesNonZeroExitCode()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(".agents/skills/ensure-directives/SKILL.md", "# Present");
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "skill not found"));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "skill not found"));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "skill not found"));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "updated", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "updated", string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, true, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Report.InstallResults, install => !install.Success && install.StandardError.Contains("skill not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitAgentsAddDistinctSkillDirectoriesAfterStandardDirectory()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "standard,claude-code,trae,trae-cn", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                Path.Combine(tempDirectory.Path, ".agents", "skills"),
                Path.Combine(tempDirectory.Path, ".claude", "skills"),
                Path.Combine(tempDirectory.Path, ".trae", "skills")
            ],
            result.Report.SkillsDirectories);
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
    public async Task UnknownAgentProducesValidationError()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex,unknown-agent", false),
            CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task StandardAgentOnlyUsesStandardDirectoryAndDoesNotManageClaude()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "standard", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([Path.Combine(tempDirectory.Path, ".agents", "skills")], result.Report.SkillsDirectories);
        Assert.False(result.Report.DirectiveSummary?.CreateClaudeFile);
        Assert.Equal(string.Empty, result.Report.ClaudeFile);
        Assert.DoesNotContain(result.Report.Actions, action => action.Contains("CLAUDE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StandardPathAgentValueMapsToStandardDirectory()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([Path.Combine(tempDirectory.Path, ".agents", "skills")], result.Report.SkillsDirectories);
        Assert.False(result.Report.DirectiveSummary?.CreateClaudeFile);
    }

    [Fact]
    public async Task StandardAgentOnlyDoesNotCreateClaudeFile()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts { ConfirmResult = false },
            new NullReporter(),
            new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, "standard", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(tempDirectory.Path, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "CLAUDE.md")));
    }

    [Fact]
    public async Task InteractiveSelectionOnlyAppliesSelectedDirectives()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
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
                    tempDirectory.Write(".agents/skills/dotnet-modern-csharp-editorconfig/SKILL.md", "# Installed");
                }
            }
        };
        commandRunner.Enqueue(new CommandResult(0, "git version 2.50.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, tempDirectory.Path, string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "updated", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "updated", string.Empty));
        CheckWorkflow workflow = new(commandRunner, new FakePrompts(), new NullReporter(), new FakeDirectiveSource());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, true, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        _ = Assert.Single(commandRunner.Calls, call => call.Arguments.Contains("install"));
        Assert.True(File.Exists(Path.Combine(tempDirectory.Path, ".claude", "skills", "dotnet-livecharts2", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(tempDirectory.Path, ".claude", "skills", "dotnet-modern-csharp-editorconfig", "SKILL.md")));
    }
}
