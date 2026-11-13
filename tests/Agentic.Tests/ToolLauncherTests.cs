namespace Agentic.Tests;

public sealed class ToolLauncherTests
{
    // The .NET 11 SDK starts an Agentic.Check that it downloaded before unless it is told to ask the feeds.
    // Everything after -- belongs to the check, also an argument that looks like the SDK's own option.
    [Fact]
    public void TheCheckIsStartedWithTheOptionThatAsksTheFeedsForTheLatestVersion()
        => Assert.Equal(
            ["tool", "exec", "Agentic.Check", "--no-http-cache", "--", "--dry-run", "--no-http-cache"],
            ToolLauncher.CheckArguments(["--dry-run", "--no-http-cache"]));
}
