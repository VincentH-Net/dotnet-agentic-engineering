namespace Agentic.Check.Tests;

// An agent or a pipeline has no terminal. A run that needs an answer there used to crash on its first key
// read; it now stops before it applies anything and names the options to pass, with the command that started it.
public sealed class NoTerminalTests
{
    [Theory]
    [InlineData(null, "dnx agentic.check --")]
    [InlineData("1.0.0", "dna check")]
    public async Task RunWithoutATerminalStopsAtTheActionListAndNamesTheOptions(string? launcherVersion, string command)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        ToolRunner runner = new();
        FakePrompts prompts = new() { IsInteractive = false };
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, prompts, reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(), [],
            readEnvironment: name => name == DnaLauncherContract.VersionVariable ? launcherVersion : null);

        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        string error = Assert.Single(reporter.Errors);
        Assert.Matches(@"^[1-9][0-9]* action\(s\) are recommended, but this run has no terminal to select them in\. ", error);
        Assert.EndsWith($"Run `{command} --yes` to apply them all, or `{command} --dry-run` to list them.", error, StringComparison.Ordinal);
        // The action list was never shown, and nothing was written.
        Assert.Null(prompts.Duplicates);
        Assert.False(File.Exists(Path.Combine(temp.Path, "AGENTS.md")));
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "install" or "update", ..]);
    }

    // The options are what such a run passes, so with one of them it goes on as before.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunWithoutATerminalGoesOnWhenAnOptionSaysWhatToDo(bool dryRun, bool yes)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(new ToolRunner(), new FakePrompts { IsInteractive = false }, reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(), []);

        var result = await workflow.RunAsync(new(temp.Path, dryRun, yes, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(reporter.Errors);
        Assert.Equal(yes, File.Exists(Path.Combine(temp.Path, "AGENTS.md")));
    }

    // Updates of installed skills are confirmed separately. Without a terminal that question cannot be asked
    // either, and the answer is the same.
    [Fact]
    public async Task RunWithoutATerminalStopsAtTheSkillUpdateQuestion()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        temp.Write(".claude/skills/fixture-consumer/SKILL.md", "old content");
        SkillManifestEntry installed = new(CompanionDependency.SourceRepo, "fixture-consumer", "fixture-consumer", TechnologyNames.Dotnet, []);
        ToolRunner runner = new() { UpdateCandidate = true };
        FakePrompts prompts = new() { IsInteractive = false };
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, prompts, reporter, new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [installed]);

        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "claude-code", false), CancellationToken.None);

        Assert.Equal("1 skill update(s) are available, but this run has no terminal to confirm them in. Run `dnx agentic.check -- --yes` to apply them, or `dnx agentic.check -- --dry-run` to list them.",
            Assert.Single(reporter.Errors));
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(prompts.ConfirmPrompts);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "gh" && call.Arguments.Contains("update") && !call.Arguments.Contains("--dry-run"));
    }

    // A run that has nothing to ask needs no terminal: it reports that everything is up to date.
    [Fact]
    public async Task RunWithoutATerminalThatHasNothingToAskSucceeds()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        FakePrompts prompts = new() { IsInteractive = false };
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(new NoUpdatesRunner(), prompts, reporter, new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), []);

        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "claude-code", false), CancellationToken.None);

        Assert.Empty(reporter.Errors);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(prompts.Duplicates);
        Assert.Empty(prompts.ConfirmPrompts);
    }

    // gh reports that no installed skill has an update.
    sealed class NoUpdatesRunner : ICommandRunner
    {
        readonly ToolRunner inner = new();

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string?>? environment = null)
            => fileName == "gh" && arguments.Contains("update") && arguments.Contains("--dry-run")
                ? Task.FromResult(new CommandResult(0, "No updates available", string.Empty))
                : inner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment);
    }
}
