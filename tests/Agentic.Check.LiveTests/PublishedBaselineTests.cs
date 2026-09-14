using Agentic.PackageFixtures;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class PublishedBaselineTests(ITestOutputHelper output)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "BaselineNetwork")]
    public async Task RealGhBranchAndShaPinsMatchIndependentSource(bool immutablePin)
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1", "Opt in with AGENTIC_E2E_NETWORK=1 for real gh metadata verification.");
        using FixtureWorkspace workspace = new();
        await workspace.InitializeAsync().ConfigureAwait(true);
        using SourceOracle oracle = new(workspace.Process, workspace.Root);
        var source = await oracle.SelectedAsync(SourceOracle.OwnRepository, true).ConfigureAwait(true);
        string pin = immutablePin ? source.Commit : source.Reference;
        _ = await workspace.Process.SuccessAsync("gh", ["skill", "install", source.Repository, "cli-e2e-testing", "--pin", pin, "--force", "--dir", Path.Combine(workspace.Target, ".agents/skills")], workspace.Target).ConfigureAwait(true);
        var installed = Assert.Single(await oracle.VerifySkillsAsync(workspace.Target).ConfigureAwait(true));
        Assert.Equal(source.Commit, installed.Commit);
        Assert.Equal(pin, installed.Pin);
        await oracle.EnsureUnmovedAsync(source).ConfigureAwait(true);
        output.WriteLine($"Real-gh helper verification only: {installed.Repository}@{installed.Reference}, pin {installed.Pin}, commit {installed.Commit}, tree {installed.Tree}");
    }

    [SkippableFact]
    [Trait("Category", "BaselineNetwork")]
    public async Task PublishedInstallerRunsFromExactNuGetBytesInRecordedTerminal()
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1", "Opt in with AGENTIC_E2E_NETWORK=1 for published-package verification.");
        Skip.If(!RecordedTerminal.Supported, "Recorded terminal requires Bash on macOS/Linux.");
        var baseline = FixtureFiles.ReadJson<BaselineDefinition>(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baseline-definitions/agentic-check-2.2.0.json"));
        var package = await PackageArtifact.DownloadAsync(baseline).ConfigureAwait(true);
        using FixtureWorkspace workspace = new();
        await workspace.InitializeAsync().ConfigureAwait(true);
        string executable = await workspace.InstallCheckAsync(package).ConfigureAwait(true);
        string help = await workspace.Process.SuccessAsync(executable, ["--help", "--agents", "codex"], workspace.Target).ConfigureAwait(true);
        Assert.Contains("--preview", help, StringComparison.Ordinal);
        string recording = Path.Combine(FixtureFiles.Reports, "recordings", $"published-{package.Version}-{Guid.NewGuid():N}.cast");
        output.WriteLine($"{package.Id} {package.Version} SHA256 {package.Sha256}");
        output.WriteLine("asciinema play " + RecordedTerminal.Quote(recording));
        string recorded = await RecordedTerminal.RunAsync(workspace, executable, ["--version"], recording, _ => Task.CompletedTask).ConfigureAwait(true);
        Assert.Contains(package.Version, recorded, StringComparison.Ordinal);
        string failedRecording = Path.ChangeExtension(recording, ".failure.cast");
        output.WriteLine("asciinema play " + RecordedTerminal.Quote(failedRecording));
        _ = await Assert.ThrowsAsync<InvalidDataException>(() => RecordedTerminal.RunAsync(workspace, executable, ["--invalid-recording-probe"], failedRecording, _ => Task.CompletedTask)).ConfigureAwait(true);
        Assert.True(new FileInfo(failedRecording).Length > 0, "Failed terminal runs must preserve recordings.");
        package.Verify();
    }
}
