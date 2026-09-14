using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class PreviewSourceOptionTests
{
    [Fact]
    public async Task HelpKeepsInternalOptionHidden()
    {
        using FixtureWorkspace workspace = new();
        string tool = Path.ChangeExtension(typeof(AgenticCheckCli).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        string help = await workspace.Process.SuccessAsync(tool, ["--help", "--agents", "codex"], workspace.Target).ConfigureAwait(true);
        Assert.DoesNotContain("--preview-source-ref", help, StringComparison.Ordinal);
        Assert.Contains("--preview", help, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--preview-source-ref")]
    [InlineData("--preview-source-ref=")]
    [InlineData("--preview-source-ref=../bad")]
    [InlineData("--preview-source-ref=feature/test")]
    public async Task InvalidOrStableInvocationCannotWriteContent(string argument)
    {
        using FixtureWorkspace workspace = new();
        string tool = Path.ChangeExtension(typeof(AgenticCheckCli).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        var result = await workspace.Process.RunAsync(tool, [workspace.Target, "--agents", "codex", argument], workspace.Target).ConfigureAwait(true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--preview-source-ref", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(workspace.Target));
    }
}
