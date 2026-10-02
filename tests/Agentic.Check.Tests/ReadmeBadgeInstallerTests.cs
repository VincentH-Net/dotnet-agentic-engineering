namespace Agentic.Check.Tests;

public sealed class ReadmeBadgeInstallerTests
{
    [Fact]
    public void NoReadmeMeansNoOffer()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");

        Assert.Null(ReadmeBadgeInstaller.Plan(tempDirectory.Path));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("readme.md")]
    public void PlanOffersTheBadgeForAReadmeWithoutALinkToTheRepository(string fileName)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(fileName, "# Sample\n");

        var plan = ReadmeBadgeInstaller.Plan(tempDirectory.Path);

        Assert.NotNull(plan);
        Assert.False(plan.IsCurrent);
        Assert.Equal("add", plan.Status);
        Assert.Equal(fileName, plan.Display);
        var action = ReadmeBadgeInstaller.Action(plan);
        Assert.True(action.IsReadmeBadge);
        Assert.Equal("README badge: built with dna (add)", RecommendationSelectionPrompt.FormatSkillListItem(action));
    }

    [Theory]
    [InlineData("# Sample\n\nSee https://github.com/VincentH-Net/dotnet-agentic-engineering for the setup.\n")]
    [InlineData("# Sample\n\n" + ReadmeBadgeInstaller.Badge + "\n")]
    public void AReadmeThatAlreadyLinksHereIsCovered(string content)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("README.md", content);

        var plan = ReadmeBadgeInstaller.Plan(tempDirectory.Path);

        Assert.NotNull(plan);
        Assert.True(plan.IsCurrent);
        Assert.Equal("up to date", plan.Status);
        Assert.Same(content, ReadmeBadgeInstaller.Insert(content));
    }

    [Theory]
    [InlineData("# Sample\n\nHello.\n", "# Sample\n\n{badge}\n\nHello.\n")]
    [InlineData("# Sample\nHello.\n", "# Sample\n\n{badge}\n\nHello.\n")]
    [InlineData("# Sample", "# Sample\n\n{badge}\n")]
    [InlineData("Intro first.\n\n## Not a title\n", "{badge}\n\nIntro first.\n\n## Not a title\n")]
    [InlineData("", "{badge}\n\n")]
    [InlineData("# Sample\r\n\r\nHello.\r\n", "# Sample\r\n\r\n{badge}\r\n\r\nHello.\r\n")]
    public void BadgeFollowsTheTitleOrComesFirst(string content, string expected)
        => Assert.Equal(Expand(expected), ReadmeBadgeInstaller.Insert(content));

    static string Expand(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Replace("{badge}", ReadmeBadgeInstaller.Badge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureWritesTheBadgeOnceAndASecondPlanIsCurrent()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("README.md", "# Sample\n\nHello.\n");
        var plan = ReadmeBadgeInstaller.Plan(tempDirectory.Path)!;

        var dryRun = await ReadmeBadgeInstaller.EnsureAsync(plan, true, CancellationToken.None);
        Assert.True(dryRun.Success);
        Assert.Equal("# Sample\n\nHello.\n", await File.ReadAllTextAsync(plan.File, CancellationToken.None));

        var result = await ReadmeBadgeInstaller.EnsureAsync(plan, false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("add", result.Action);
        string content = await File.ReadAllTextAsync(plan.File, CancellationToken.None);
        Assert.Equal($"# Sample\n\n{ReadmeBadgeInstaller.Badge}\n\nHello.\n", content);
        var replan = ReadmeBadgeInstaller.Plan(tempDirectory.Path)!;
        Assert.True(replan.IsCurrent);
        _ = await ReadmeBadgeInstaller.EnsureAsync(replan, false, CancellationToken.None);
        Assert.Equal(content, await File.ReadAllTextAsync(plan.File, CancellationToken.None));
    }
}
