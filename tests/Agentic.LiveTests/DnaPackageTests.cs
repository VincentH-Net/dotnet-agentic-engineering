using Agentic.Check;

namespace Agentic.LiveTests;

public sealed class DnaPackageTests
{
    static string MissingMessage => "`dotnet agentic` is not installed in the scope of the current working folder."
        + Environment.NewLine + "Please run `dna check` to install it." + Environment.NewLine;

    [Fact]
    public async Task GlobalShorthandUsesLocalCompanionAndPreservesStreamsArgumentsAndWorkingDirectory()
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        workspace.AddPackage("2.3.0");
        await workspace.AddToolAsync("Dna").ConfigureAwait(true);
        var installer = new DnaInstaller(workspace.Runner, workspace.Feed, Path.Combine(workspace.CliHome, ".dotnet", "tools"));
        var absent = await installer.InspectAsync(workspace.Target, CancellationToken.None).ConfigureAwait(true);
        Assert.Null(absent.Version);
        var installed = await installer.EnsureAsync(absent, workspace.Target, false, true, new NoPrompts(), CancellationToken.None).ConfigureAwait(true);
        Assert.True(installed.Success, installed.Error);
        Assert.Equal("install", installed.Action);
        Assert.Equal("1.0.0", installed.ResolvedVersion);
        Assert.True(File.Exists(installer.CommandPath));

        var missing = await workspace.Runner.RunAsync(installer.CommandPath, ["-h"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, missing.ExitCode);
        Assert.Empty(missing.StandardOutput);
        Assert.Equal(MissingMessage, missing.StandardError);
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(workspace.Target)));
        await workspace.RunAsync("dotnet", ["tool", "install", CompanionDependency.PackageId, "--local", "--version", "2.3.0"]).ConfigureAwait(true);
        string child = Directory.CreateDirectory(Path.Combine(workspace.Target, "child")).FullName;
        string payload = "literal \"quotes\"; $(touch unexpected) 漢字 😀\n\n" + new string('x', 150000) + "\n";
        string[] arguments = ["prompt-log", "wrap", "--input", "-", "-m", "2.3"];
        var expected = await workspace.Runner.RunWithInputAsync("dotnet", ["agentic", .. arguments], child, payload).ConfigureAwait(true);
        var actual = await workspace.Runner.RunWithInputAsync(installer.CommandPath, arguments, child, payload).ConfigureAwait(true);
        Assert.True(actual.Success, actual.StandardError);
        Assert.Equal(expected, actual);
        Assert.False(File.Exists(Path.Combine(child, "unexpected")));

        const string name = "input with spaces ' and ;.txt";
        await File.WriteAllTextAsync(Path.Combine(child, name), payload).ConfigureAwait(true);
        var file = await workspace.Runner.RunAsync(installer.CommandPath, ["prompt-log", "wrap", "--input", name, "-m", "2.3"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(expected, file);
        var invalid = await workspace.Runner.RunAsync(installer.CommandPath, ["prompt-log", "wrap", "--unknown"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, invalid.ExitCode);
        Assert.NotEmpty(invalid.StandardError);
        var present = await installer.InspectAsync(workspace.Target, CancellationToken.None).ConfigureAwait(true);
        var updated = await installer.EnsureAsync(present, workspace.Target, false, true, new NoPrompts(), CancellationToken.None).ConfigureAwait(true);
        Assert.True(updated.Success, updated.Error);
        Assert.Equal("update", updated.Action);

        // A nearer root manifest prevents a parent companion from being in scope.
        string manifest = CompanionInstaller.ManifestPath(child);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        await File.WriteAllTextAsync(manifest, "{\"version\":1,\"isRoot\":true,\"tools\":{}}").ConfigureAwait(true);
        var outOfScope = await workspace.Runner.RunAsync(installer.CommandPath, [], child, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, outOfScope.ExitCode);
        Assert.Empty(outOfScope.StandardOutput);
        Assert.Equal(MissingMessage, outOfScope.StandardError);
        await File.WriteAllTextAsync(manifest, "{\"version\":1,\"isRoot\":false,\"tools\":{}}").ConfigureAwait(true);
        var inScope = await workspace.Runner.RunAsync(installer.CommandPath, ["--help"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(inScope.Success, inScope.StandardError);
        Assert.Contains("prompt-log", inScope.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unrestored")]
    [InlineData("invalid-manifest")]
    [InlineData("missing-sdk")]
    public async Task OtherFailuresRetainOriginalSdkDiagnostics(string failure)
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        await workspace.AddToolAsync("Dna").ConfigureAwait(true);
        await workspace.RunAsync("dotnet", ["tool", "install", DnaInstaller.PackageId, "--global"]).ConfigureAwait(true);
        string dna = Path.Combine(workspace.CliHome, ".dotnet", "tools", OperatingSystem.IsWindows() ? "dna.exe" : "dna");
        string manifest = CompanionInstaller.ManifestPath(workspace.Target);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        // Declare the companion without installing/restoring it into this isolated CLI home.
        await File.WriteAllTextAsync(manifest, failure == "invalid-manifest" ? "not json"
            : "{\"version\":1,\"isRoot\":true,\"tools\":{\"innowvate.agentic\":{\"version\":\"2.3.0\",\"commands\":[\"agentic\"]}}}").ConfigureAwait(true);
        if (failure == "missing-sdk")
            await File.WriteAllTextAsync(Path.Combine(workspace.Target, "global.json"), "{\"sdk\":{\"version\":\"999.0.100\",\"rollForward\":\"disable\"}}").ConfigureAwait(true);

        var expected = await workspace.Runner.RunAsync("dotnet", ["tool", "run", "agentic", "--", "--version"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
        var actual = await workspace.Runner.RunAsync(dna, ["--version"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
        Assert.NotEqual(0, expected.ExitCode);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("Please run `dna check`", actual.StandardError, StringComparison.Ordinal);
        if (failure == "unrestored")
            Assert.Contains("dotnet tool restore", actual.StandardOutput + actual.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothCheckShortcutsForwardToRealCheckIncludingHelpAndFailureExitCode()
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        workspace.AddPackage("2.3.0");
        await workspace.AddToolAsync("Dna").ConfigureAwait(true);
        await workspace.AddToolAsync("Agentic.Check").ConfigureAwait(true);
        await workspace.RunAsync("dotnet", ["tool", "install", DnaInstaller.PackageId, "--global"]).ConfigureAwait(true);
        string dna = Path.Combine(workspace.CliHome, ".dotnet", "tools", OperatingSystem.IsWindows() ? "dna.exe" : "dna");
        // dna check works before the repo-local companion exists.
        var standalone = await workspace.Runner.RunAsync(dna, ["check", "--help"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
        Assert.True(standalone.Success, standalone.StandardError);
        Assert.Contains("--skills-dir", standalone.StandardOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(workspace.Target)));
        await workspace.RunAsync("dotnet", ["tool", "install", CompanionDependency.PackageId, "--local", "--version", "2.3.0"]).ConfigureAwait(true);
        foreach (string command in new[] { "dotnet", dna })
        {
            string[] prefix = command == "dotnet" ? ["agentic", "check"] : ["check"];
            var help = await workspace.Runner.RunAsync(command, [.. prefix, "--help"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
            Assert.True(help.Success, help.StandardError);
            Assert.Contains("--skills-dir", help.StandardOutput, StringComparison.Ordinal);
            var failure = await workspace.Runner.RunAsync(command, [.. prefix, "--agents", "not an agent"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
            // Agentic.Check's parser returns 1; the prompt-log CLI returns 2 for invalid arguments.
            Assert.Equal(1, failure.ExitCode);
            Assert.Contains("not an agent", failure.StandardOutput + failure.StandardError, StringComparison.Ordinal);
        }

        await File.WriteAllTextAsync(Path.Combine(workspace.Target, "global.json"), "{\"sdk\":{\"version\":\"9.0.100\",\"rollForward\":\"disable\"}}").ConfigureAwait(true);
        var sdk = await workspace.Runner.RunAsync(dna, ["check", "--help"], workspace.Target, CancellationToken.None).ConfigureAwait(true);
        Assert.NotEqual(0, sdk.ExitCode);
        Assert.Contains("requires .NET SDK 10 or later", sdk.StandardError, StringComparison.Ordinal);
        Assert.Contains("global.json", sdk.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("--skills-dir", sdk.StandardOutput, StringComparison.Ordinal);
    }

    sealed class NoPrompts : IUserPrompts
    {
        public bool IsInteractive => false;
        public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unattended installation must not prompt.");
        public Task<RecommendationSelectionResult> SelectRecommendationsAsync(IReadOnlyList<DirectivePlanItem> recommendedDirectives,
            IReadOnlyList<SkillManifestEntry> missingSkills, string targetDirectory, IReadOnlyList<string> skillsDirectories, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unexpected recommendation prompt.");
        public Task WaitForHelpKeyAsync(string url, string purpose, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unexpected help prompt.");
    }
}
