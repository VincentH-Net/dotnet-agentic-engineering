namespace Agentic.Check.Tests;

public sealed class InteractionTests
{
    [Fact]
    public void SummaryColumnHeadersDescribeCheckAndStatus()
    {
        Assert.Equal("Check", SpectreReporter.SummaryLabelColumnHeader);
        Assert.Equal("Status", SpectreReporter.SummaryValueColumnHeader);
    }

    [Fact]
    public void DirectiveSummaryCombinesRecommendedMissingAndOutdatedCounts()
    {
        DirectiveSummary summary = new(
            CreateAgentsFile: true,
            CreateClaudeFile: false,
            RecommendedCount: 3,
            MissingCount: 1,
            OutdatedCount: 2);

        Assert.Equal(
            "1 missing, 2 update(s) available",
            SpectreReporter.FormatDirectiveSummary(summary));
    }

    [Fact]
    public void SkillSummaryCombinesRecommendedMissingAndOutdatedCounts()
        => Assert.Equal(
            "4 missing, 2 update(s) available, 1 up to date",
            SpectreReporter.FormatSkillSummary(7, 4, 2));

    [Fact]
    public void RecommendationStatusOmitsZeroCountParts()
        => Assert.Equal(
            "all 3 up to date",
            SpectreReporter.FormatRecommendationStatus(3, 0, 0));

    [Fact]
    public void RecommendationStatusReportsUpToDateWhenAllCountsAreZero()
        => Assert.Equal(
            "up to date",
            SpectreReporter.FormatRecommendationStatus(0, 0, 0));

    [Fact]
    public void RecommendationStatusPrefixesSingleNonZeroPartWithAll()
        => Assert.Equal(
            "all 2 missing",
            SpectreReporter.FormatRecommendationStatus(2, 2, 0));

    [Fact]
    public void SkillsDirectoriesAreRenderedRelativeToRepositoryRoot()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "repo");
        string agentsSkills = Path.Combine(repoRoot, ".agents", "skills");
        string claudeSkills = Path.Combine(repoRoot, ".claude", "skills");

        Assert.Equal(
            string.Join(Environment.NewLine, [Path.Combine(".agents", "skills"), Path.Combine(".claude", "skills")]),
            SpectreReporter.FormatSkillsDirectories(repoRoot, [agentsSkills, claudeSkills]));
    }
}
