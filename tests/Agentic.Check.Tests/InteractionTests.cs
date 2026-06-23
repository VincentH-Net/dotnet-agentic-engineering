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
            "recommended: 3, with 1 missing and 2 outdated",
            SpectreReporter.FormatDirectiveSummary(summary));
    }

    [Fact]
    public void SkillSummaryCombinesRecommendedMissingAndOutdatedCounts()
        => Assert.Equal(
            "recommended: 7, with 4 missing and 2 outdated",
            SpectreReporter.FormatSkillSummary(7, 4, 2));
}
