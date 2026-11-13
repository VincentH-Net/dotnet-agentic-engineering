namespace Agentic.Check.Tests;

public sealed class NewerVersionTests
{
    const string Update = "Run `dotnet tool update --global InnoWvate.Dna`";

    // A run that dna started cannot replace dna, so its action list never offers the update. A person at the
    // terminal is stopped before any work, so the line cannot be overlooked; a run that asks no questions
    // goes on with a warning, so pipelines and agents keep working.
    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public async Task RunStartedByDnaOlderThanTheLatestStopsOnlyWhenAPersonIsAtTheTerminal(bool dryRun, bool yes, bool terminal, bool stops)
    {
        using TempDirectory temp = new();
        RecordingReporter reporter = new();
        var (workflow, runner) = Workflow(temp, reporter, "1.0.0+abc123", terminal, new() { [DnaInstaller.PackageId] = ["1.0.0", "1.0.1"] });

        var result = await workflow.RunAsync(new(temp.Path, dryRun, yes, null, null, "codex", false), CancellationToken.None);

        string outdated = $"The dna shorthand 1.0.0 is older than the latest 1.0.1. {Update}";
        var launcher = Assert.Single(result.Report.Prerequisites, check => check.Name == "dna");
        Assert.Empty(runner.Writes);
        if (stops)
        {
            Assert.Equal(2, result.ExitCode);
            Assert.Equal(outdated + ", then run `dna check` again.", Assert.Single(reporter.Errors));
            Assert.False(launcher.Success);
            Assert.Equal(Assert.Single(reporter.Errors), launcher.StandardError);
            Assert.Empty(reporter.Warnings);
            // Nothing was looked at yet: no prerequisite other than dna, no stack, no plan.
            _ = Assert.Single(result.Report.Prerequisites);
        }
        else
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(reporter.Errors);
            Assert.True(launcher.Success);
            Assert.Contains(outdated + ".", reporter.Warnings);
            Assert.Contains(outdated + ".", result.Report.Warnings);
        }
    }

    // Nothing is said about a dna that is the latest or newer, about one whose version cannot be read, or
    // when nuget.org gave no answer.
    [Theory]
    [InlineData("1.0.1", "1.0.1")]
    [InlineData("1.1.0-preview.1", "1.0.1")]
    [InlineData("not a version", "1.0.1")]
    [InlineData("1.0.0", null)]
    public async Task RunStartedByDnaThatIsNotKnownToBeOlderGoesOn(string version, string? published)
    {
        using TempDirectory temp = new();
        RecordingReporter reporter = new();
        var (workflow, _) = Workflow(temp, reporter, version, terminal: true, new() { [DnaInstaller.PackageId] = published is null ? null : [published] });

        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(reporter.Errors);
        Assert.DoesNotContain(result.Report.Warnings, warning => warning.Contains("dna shorthand", StringComparison.Ordinal));
    }

    // Every version of the check reads the directives and skills of the latest release, and only the latest
    // version is tested with them. So an older one refuses in every kind of run, before it looks at anything.
    // It can be running unasked: the .NET 11 SDK starts a version of a tool that it downloaded before.
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public async Task CheckOlderThanTheLatestRefusesInEveryRun(bool dryRun, bool yes, bool terminal)
    {
        using TempDirectory temp = new();
        RecordingReporter reporter = new();
        var (workflow, runner) = Workflow(temp, reporter, launcherVersion: null, terminal, new() { [ToolHeader.PackageId] = ["0.1.0", "99.0.0"] });

        var result = await workflow.RunAsync(new(temp.Path, dryRun, yes, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        string error = Assert.Single(reporter.Errors);
        Assert.Equal($"Agentic.Check {ToolHeader.Version} is older than the latest 99.0.0, and only the latest version works with the latest directives and skills. Run `dnx agentic.check --no-http-cache`.", error);
        var check = Assert.Single(result.Report.Prerequisites);
        Assert.Equal((ToolHeader.PackageId, false, ToolHeader.Version, "99.0.0", error), (check.Name, check.Success, check.Version, check.MinimumVersion, check.StandardError));
        Assert.Empty(runner.Commands.Calls);
        Assert.Empty(reporter.Warnings);
    }

    // Nothing stops a check that is the latest or newer, one that only a prerelease is newer than, or one that
    // got no answer from nuget.org.
    [Theory]
    [InlineData("99.0.0-preview.1")]
    [InlineData("0.1.0")]
    [InlineData(null)]
    public async Task CheckThatIsNotKnownToBeOlderRuns(string? published)
    {
        using TempDirectory temp = new();
        RecordingReporter reporter = new();
        var (workflow, _) = Workflow(temp, reporter, launcherVersion: null, terminal: true, new() { [ToolHeader.PackageId] = published is null ? null : [published] });

        var result = await workflow.RunAsync(new(temp.Path, true, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(reporter.Errors);
        Assert.DoesNotContain(result.Report.Prerequisites, check => check.Name == ToolHeader.PackageId);
    }

    // On the .NET 11 SDK an older dna starts an older check. The run then names the cause: updating dna is
    // what makes the next run start the latest check. This holds without a person at the terminal too.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OlderCheckStartedByAnOlderDnaRefusesWithTheDnaUpdate(bool dryRun, bool yes)
    {
        using TempDirectory temp = new();
        RecordingReporter reporter = new();
        var (workflow, runner) = Workflow(temp, reporter, "1.0.0", terminal: false,
            new() { [DnaInstaller.PackageId] = ["1.0.0", "1.0.1"], [ToolHeader.PackageId] = ["99.0.0"] });

        var result = await workflow.RunAsync(new(temp.Path, dryRun, yes, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal($"The dna shorthand 1.0.0 is older than the latest 1.0.1. {Update}, then run `dna check` again.", Assert.Single(reporter.Errors));
        Assert.False(Assert.Single(result.Report.Prerequisites).Success);
        Assert.Empty(runner.Commands.Calls);
    }

    static (CheckWorkflow Workflow, DnaRunner Runner) Workflow(TempDirectory temp, RecordingReporter reporter, string? launcherVersion, bool terminal,
        Dictionary<string, string[]?> published)
    {
        CompanionTests.WriteManifest(temp.Path, "2.3.0");
        DnaRunner runner = new(new ToolRunner()) { Version = "1.0.0" };
        CheckWorkflow workflow = new(runner, new FakePrompts { IsInteractive = terminal }, reporter,
            new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [],
            new DnaInstaller(runner, temp.CreateDirectory("bin")),
            name => name == DnaLauncherContract.VersionVariable ? launcherVersion : null,
            new FakeVersionSource(published));
        return (workflow, runner);
    }
}
