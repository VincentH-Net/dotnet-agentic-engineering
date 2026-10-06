namespace Agentic.Check.Tests;

public sealed class ManifestReleaseGateTests
{
    const string Repository = "VincentH-Net/dotnet-agentic-engineering";

    static readonly SkillManifestEntry Gated = new(Repository, "orleans-multitenant", "orleans-multitenant", TechnologyNames.Orleans, [], minimumRelease: ToolVersion.Parse("2.4.2"));

    [Theory]
    [InlineData("v2.4.1", false)]
    [InlineData("2.4.1", false)]
    [InlineData("v2.4.2", true)]
    [InlineData("v2.5.0", true)]
    [InlineData("v3.0.0", true)]
    [InlineData("latest", true)]
    public void StableOffersAGatedEntryFromItsMinimumReleaseOn(string tag, bool offered)
        => Assert.Equal(offered, StaticSkillManifest.IsOffered(Gated, SourceVersionMode.Stable, new SourceVersionInfo(Repository, tag, DateTimeOffset.MinValue)));

    [Fact]
    public void PreviewAndADefaultBranchAlwaysOfferAGatedEntryAndNothingGatesTheRest()
    {
        SourceVersionInfo older = new(Repository, "v2.4.1", DateTimeOffset.MinValue);

        Assert.True(StaticSkillManifest.IsOffered(Gated, SourceVersionMode.Preview, older));
        Assert.True(StaticSkillManifest.IsOffered(Gated, SourceVersionMode.Stable, older with { IsDefaultBranch = true }));
        Assert.True(StaticSkillManifest.IsOffered(Gated with { MinimumRelease = null }, SourceVersionMode.Stable, older));
    }

    [Fact]
    public void TheMultitenantSkillIsInBothSetsAndGatedOnTheReleaseThatShipsIt()
    {
        var stable = Assert.Single(StaticSkillManifest.All, skill => skill.InstallArg == "orleans-multitenant");
        var preview = Assert.Single(StaticSkillManifest.Preview, skill => skill.InstallArg == "orleans-multitenant");

        Assert.Equal(ToolVersion.Parse("2.4.2"), stable.MinimumRelease);
        Assert.Same(stable, preview);
        Assert.All(StaticSkillManifest.All.Where(skill => skill != stable), skill => Assert.Null(skill.MinimumRelease));
    }

    [Theory]
    [InlineData("v2.4.1", false)]
    [InlineData("v2.4.2", true)]
    public async Task AStableCheckRecommendsAGatedSkillOnlyFromItsRelease(string stableRef, bool recommended)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "No updates available.", string.Empty));
        SkillManifestEntry[] manifest =
        [
            new(Repository, "dotnet-livecharts2", "dotnet-livecharts2", TechnologyNames.Dotnet, [], "dotnet"),
            new(Repository, "dotnet-later", "dotnet-later", TechnologyNames.Dotnet, [], "dotnet", minimumRelease: ToolVersion.Parse("2.4.2"))
        ];
        CheckWorkflow workflow = new(
            commandRunner,
            new FakePrompts(),
            new RecordingReporter(),
            new FakeDirectiveSource(),
            new FakeSourceVersionResolver { StableRef = stableRef },
            skillManifest: manifest);

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, true, false, null, null, "codex", false),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Report.RecommendedSkills, skill => skill.InstallArg == "dotnet-livecharts2");
        Assert.Equal(recommended, result.Report.RecommendedSkills.Any(skill => skill.InstallArg == "dotnet-later"));
    }
}
