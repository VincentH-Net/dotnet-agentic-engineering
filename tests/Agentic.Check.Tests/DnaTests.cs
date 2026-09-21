using System.Text.Json;

namespace Agentic.Check.Tests;

public sealed class DnaTests
{
    [Fact]
    public void SelectionDefaultsShorthandWithCompanionButPreservesOptOut()
    {
        var companion = CompanionDependency.Action();
        var dna = DnaInstaller.Action(new(null));
        var items = RecommendationSelectionPrompt.BuildItems([], [companion, dna]);
        RecommendationSelectionState state = new(items);
        state.Apply(new(SkillSelectionCommand.SelectNone));
        state.Apply(new(SkillSelectionCommand.Toggle));
        Assert.Equal([companion, dna], state.SelectedSkills);
        state.Apply(new(SkillSelectionCommand.Down));
        state.Apply(new(SkillSelectionCommand.Toggle));
        Assert.Equal([companion], state.SelectedSkills);
        Assert.Equal([companion], CheckWorkflow.CloseDependencies([], state.SelectedSkills, [companion, dna]).SelectedSkills);
        state.Apply(new(SkillSelectionCommand.Toggle));
        state.DeselectWithDependents(items[0].Key);
        Assert.Empty(state.SelectedSkills);
        state.SelectWithDependencies(items[1].Key);
        Assert.Equal([companion, dna], state.SelectedSkills);
        Assert.All(items, item => Assert.Equal(RecommendationSelectionKind.Tool, item.Kind));
    }

    [Theory]
    [InlineData(null, "install")]
    [InlineData("1.0.0", "update")]
    public async Task SelectedShorthandInstallsOrUpdatesVerifiedPackage(string? version, string action)
    {
        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("bin");
        DnaRunner runner = new() { Version = version };
        DnaInstaller installer = new(runner, bin, bin);
        var plan = await installer.InspectAsync(temp.Path, CancellationToken.None);
        var result = await installer.EnsureAsync(plan, temp.Path, false, false, new FakePrompts(), CancellationToken.None);
        Assert.True(result.Success, result.Error);
        Assert.False(result.Skipped);
        Assert.Equal(action, result.Action);
        Assert.Equal("1.1.0", result.ResolvedVersion);
        Assert.Equal(["tool", action, "--global", DnaInstaller.PackageId], Assert.Single(runner.Writes));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task CollisionNeverExecutesUnknownCommand(bool unattended, bool consent, bool skipped)
    {
        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("[untrusted] commands");
        string unknown = Path.Combine(bin, OperatingSystem.IsWindows() ? "dna.cmd" : "dna");
        await File.WriteAllTextAsync(unknown, "#!/bin/sh\ntouch unexpected-execution\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(unknown, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        DnaRunner runner = new();
        DnaInstaller installer = new(runner, bin, temp.CreateDirectory("global"));
        FakePrompts prompts = new() { ConfirmResult = consent };
        var result = await installer.EnsureAsync(new(null), temp.Path, false, unattended, prompts, CancellationToken.None);
        Assert.True(result.Success, result.Error);
        Assert.Equal(skipped, result.Skipped);
        Assert.Equal([unknown], result.Conflicts);
        Assert.Equal(skipped ? 0 : 1, runner.Writes.Count);
        Assert.Equal(unattended ? 0 : 1, prompts.ConfirmPrompts.Count);
        if (!unattended)
        {
            Assert.Contains("may hide the shorthand. Install anyway?", Assert.Single(prompts.ConfirmPrompts), StringComparison.Ordinal);
            Assert.Contains("[[untrusted]] commands", Assert.Single(prompts.ConfirmPrompts), StringComparison.Ordinal);
        }
        Assert.False(File.Exists(Path.Combine(temp.Path, "unexpected-execution")));
    }

    [Fact]
    public async Task DryRunAndInspectionErrorsCannotInstall()
    {
        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("bin");
        DnaRunner runner = new();
        DnaInstaller installer = new(runner, bin, bin);
        var dry = await installer.EnsureAsync(new(null), temp.Path, true, false, new FakePrompts(), CancellationToken.None);
        Assert.True(dry.Success);
        var failed = await installer.EnsureAsync(new(null, "broken global metadata"), temp.Path, false, true, new FakePrompts(), CancellationToken.None);
        Assert.False(failed.Success);
        Assert.Empty(runner.Writes);
    }

    [Fact]
    public void OwnedShimIsNotAConflictButAnotherPathStillIs()
    {
        using TempDirectory temp = new();
        string own = temp.CreateDirectory("global");
        string other = temp.CreateDirectory("other");
        DnaInstaller installer = new(new DnaRunner(), other + Path.PathSeparator + own, own);
        File.WriteAllText(installer.CommandPath, string.Empty);
        string conflict = Path.Combine(other, Path.GetFileName(installer.CommandPath));
        File.WriteAllText(conflict, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(installer.CommandPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            File.SetUnixFileMode(conflict, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        Assert.Equal([conflict], installer.Conflicts(new("1.0.0")));
        Assert.Equal([conflict, installer.CommandPath], installer.Conflicts(new(null)));
    }

    [Fact]
    public void WindowsPathExtensionsAndQuotedDirectoriesAreInspectedWithoutExecution()
    {
        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("with spaces");
        string command = Path.Combine(bin, "dna.CMD");
        File.WriteAllText(command, "not an executable fixture");
        Assert.Equal([command], DnaInstaller.FindPathCommands('"' + bin + '"', ".EXE;.CMD", true, temp.Path));
        Assert.Empty(DnaInstaller.FindPathCommands(bin, ".EXE", true, temp.Path));
    }

    [Fact]
    public async Task ExistingCompanionOffersDnaWithoutInstallingCompanionAgain()
    {
        using TempDirectory temp = new();
        CompanionTests.WriteManifest(temp.Path, "2.3.0");
        DnaRunner runner = new();
        var installer = new DnaInstaller(runner, temp.CreateDirectory("bin"));
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(),
            new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [], installer);
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Report.Companion);
        Assert.NotNull(result.Report.Dna);
        Assert.True(result.Report.Dna.Success, result.Report.Dna.Error);
        _ = Assert.Single(runner.Writes);
    }

    [Fact]
    public async Task FailedCompanionSkipsShorthandAndOptOutPreservesCompanion()
    {
        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("bin");
        DnaRunner runner = new(new ToolRunner { Fail = true });
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(), new FakeDirectiveSource(),
            new FakeSourceVersionResolver(), dnaInstaller: new(runner, bin));
        var failed = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(1, failed.ExitCode);
        Assert.Null(failed.Report.Dna);
        Assert.Empty(runner.Writes);

        runner = new();
        workflow = new(runner, new FakePrompts { SelectedSkillInstallArgs = [CompanionDependency.PackageId] },
            new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver(), dnaInstaller: new(runner, bin));
        var optedOut = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, optedOut.ExitCode);
        Assert.True(optedOut.Report.Companion?.Success);
        Assert.Null(optedOut.Report.Dna);
        Assert.Empty(runner.Writes);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+abc123")]
    [InlineData("1.2.0-preview.1")]
    [InlineData("not a version")]
    public async Task RunStartedByDnaNeverReplacesTheRunningShorthand(string version)
    {
        using TempDirectory temp = new();
        CompanionTests.WriteManifest(temp.Path, "2.3.0");
        DnaRunner runner = new() { Version = "1.0.0" };
        var installer = new DnaInstaller(runner, temp.CreateDirectory("bin"));
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(),
            new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [], installer,
            name => name == DnaLauncherContract.VersionVariable ? version : null);
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Report.Dna);
        Assert.Empty(runner.Writes);
        var launcher = Assert.Single(result.Report.Prerequisites, check => check.Name == "dna");
        Assert.True(launcher.Success);
        Assert.Equal(version, launcher.Version);
        Assert.Equal(DnaInstaller.MinimumLauncherVersion, launcher.MinimumVersion);
    }

    [Theory]
    [InlineData("0.9.9")]
    [InlineData("0.9.9-preview.1+abc123")]
    public async Task RunStartedByOutdatedDnaStopsBeforeAnyWork(string version)
    {
        using TempDirectory temp = new();
        _ = Directory.CreateDirectory(temp.Path);
        ToolRunner runner = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(),
            readEnvironment: name => name == DnaLauncherContract.VersionVariable ? version : null);
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(runner.Calls);
        string error = Assert.Single(reporter.Errors);
        Assert.Equal($"The dna shorthand {version} is older than the required 1.0.0. Run `dotnet tool update --global InnoWvate.Dna`, then run `dna check` again.", error);
        var launcher = Assert.Single(result.Report.Prerequisites);
        Assert.Equal("dna", launcher.Name);
        Assert.False(launcher.Success);
        Assert.Equal(version, launcher.Version);
        Assert.Equal("1.0.0", launcher.MinimumVersion);
        Assert.Equal(error, launcher.StandardError);
    }

    [Fact]
    public void LauncherVersionIsReadFromTheSharedVariableOnly()
    {
        Assert.Null(DnaInstaller.Launcher(_ => null));
        Assert.Null(DnaInstaller.Launcher(name => name == DnaLauncherContract.VersionVariable ? "  " : "1.0.0"));
        var launcher = DnaInstaller.Launcher(name => name == DnaLauncherContract.VersionVariable ? " 1.0.0 " : null);
        Assert.Equal("1.0.0", Assert.IsType<DnaLauncher>(launcher).Version);
        Assert.Null(launcher.Error);
    }
}

sealed class DnaRunner(ToolRunner? companion = null) : ICommandRunner
{
    internal string? Version { get; set; }
    internal List<IReadOnlyList<string>> Writes { get; } = [];
    readonly ToolRunner companion = companion ?? new();

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (fileName != "dotnet" || !arguments.Contains("--global"))
            return companion.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment);
        if (arguments[1] == "list")
        {
            object[] data = Version is null ? [] : [new { packageId = DnaInstaller.PackageId, version = Version, commands = new[] { "dna" } }];
            return Task.FromResult(new CommandResult(0, JsonSerializer.Serialize(new { version = 1, data }), string.Empty));
        }
        Writes.Add(arguments);
        Version = "1.1.0";
        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }
}
