namespace Agentic.Check.Tests;

public sealed class PrerequisiteCheckerTests
{
    [Fact]
    public async Task CheckAsyncUsesSingularGhSkillHelpWhenAvailable()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.95.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, GhSkillHelp(), string.Empty));
        PrerequisiteChecker checker = new(commandRunner);

        var result = await checker.CheckAsync(Environment.CurrentDirectory, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Checks, check => check.Name == "gh skill" && check.Success);
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "--help"]));
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skills", "--help"]));
    }

    [Fact]
    public async Task CheckAsyncFallsBackToPluralGhSkillsHelp()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.95.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "unknown command \"skill\""));
        commandRunner.Enqueue(new CommandResult(0, GhSkillHelp(), string.Empty));
        PrerequisiteChecker checker = new(commandRunner);

        var result = await checker.CheckAsync(Environment.CurrentDirectory, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Checks, check => check.Name == "gh skill" && check.Success);
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skill", "--help"]));
        Assert.Contains(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skills", "--help"]));
    }

    [Fact]
    public async Task CheckAsyncAcceptsGhSkillHelpOutputWhenExitCodeIsNonZero()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.95.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(1, GhSkillHelp(), string.Empty));
        PrerequisiteChecker checker = new(commandRunner);

        var result = await checker.CheckAsync(Environment.CurrentDirectory, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Checks, check => check.Name == "gh skill" && check.Success);
        Assert.DoesNotContain(commandRunner.Calls, call => call.Arguments.SequenceEqual(["skills", "--help"]));
    }

    [Fact]
    public void IsSuccessfulAllowsFailedGhSkillCheckInDryRun()
    {
        PrerequisiteResult result = new(
            [
                new("gh", true, "2.95.0", "2.93.0", string.Empty, string.Empty),
                new("gh skill", false, null, null, string.Empty, "unknown command")
            ]);

        Assert.False(result.Success);
        Assert.True(result.IsSuccessful(dryRun: true));
        Assert.False(result.IsSuccessful(dryRun: false));
    }

    static string GhSkillHelp()
        => """
           USAGE
             gh skill <command> [flags]

           AVAILABLE COMMANDS
             install: Install agent skills from a GitHub repository
             update:  Update installed skills to their latest versions
           """;
}
