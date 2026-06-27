using Spectre.Console;

namespace Agentic.Check.Tests;

public sealed class InteractionTests
{
    [Fact]
    public void ToolHeaderIncludesPurposeAndLinks()
    {
        Assert.Equal("cyan", ToolHeader.AgenticColor);
        Assert.Equal("green", ToolHeader.CheckColor);
        Assert.Equal("#b197fc", ToolHeader.DotNetColor);
        Assert.Equal(6, ToolHeader.Lines.Count);
        Assert.All(ToolHeader.Lines, line => Assert.NotEmpty(line.Agentic));
        Assert.Contains(ToolHeader.Lines, line => !string.IsNullOrWhiteSpace(line.Separator));
        Assert.All(ToolHeader.Lines, line => Assert.NotEmpty(line.Check));
        Assert.StartsWith("✓ .NET Agentic Engineering Check ", ToolHeader.ProductLine.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", ToolHeader.ProductLine, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"\n[bold {ToolHeader.CheckColor}]✓ [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.DotNetColor}].NET [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.AgenticColor}]Agentic[/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.DotNetColor}] Engineering [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.CheckColor}]Check[/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains("[grey] ", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.NotEmpty(ToolHeader.Description.Trim());
        Assert.DoesNotContain(ToolHeader.RepositoryUrl, ToolHeader.Description, StringComparison.Ordinal);
        Assert.Contains($"[link={ToolHeader.RepositoryUrl}]", ToolHeader.RepositoryLinkMarkup, StringComparison.Ordinal);
        Assert.Contains($"{ToolHeader.RepositoryUrl}[/]", ToolHeader.RepositoryLinkMarkup, StringComparison.Ordinal);
        Assert.Equal($"[bold]{new string('─', 12)}[/]", ToolHeader.SeparatorMarkup(12));
        Assert.Equal("[bold]─[/]", ToolHeader.SeparatorMarkup(0));
        Assert.DoesNotContain("Author:", ToolHeader.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryColumnHeadersDescribeCheckAndStatus()
    {
        Assert.Equal("Check", SpectreReporter.SummaryLabelColumnHeader);
        Assert.Equal("Status", SpectreReporter.SummaryValueColumnHeader);
        Assert.Equal("[bold green]Check[/]", SpectreReporter.SummaryHeaderMarkup(SpectreReporter.SummaryLabelColumnHeader, ToolHeader.CheckColor));
        Assert.Equal("[bold cyan]Status[/]", SpectreReporter.SummaryHeaderMarkup(SpectreReporter.SummaryValueColumnHeader, ToolHeader.AgenticColor));
    }

    [Fact]
    public void SummaryTableShowsRowSeparatorsBetweenChecks()
    {
        var table = SpectreReporter.CreateSummaryTable(
            "/repo",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Dotnet },
            [],
            "standard",
            ["/repo/.agents/skills"],
            new DirectiveSummary(false, false, 3, 0, 0),
            recommendedCount: 2,
            missingCount: 0,
            outdatedCount: 0);

        Assert.True(table.ShowRowSeparators);
        Assert.Same(TableBorder.HeavyHead, table.Border);
    }

    [Fact]
    public void StackDisplayUsesLayerOrderAndUnoGateDetails()
    {
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase)
        {
            TechnologyNames.Foundation,
            TechnologyNames.Dotnet,
            TechnologyNames.AspNetCore,
            TechnologyNames.Orleans,
            TechnologyNames.Uno
        };
        UnoGateReport unoGate = new(
            "App.csproj",
            ["mvux"],
            ["csharp2", "xaml"],
            ["material"]);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                [
                    "Uno Platform",
                    "  UI update pattern: mvux",
                    "  Markup type: csharp2, xaml",
                    "  Design system: material",
                    "Microsoft Orleans",
                    "ASP.NET",
                    ".NET",
                    "Agentic Foundation"
                ]),
            SpectreReporter.FormatStack(technologies, [unoGate]));
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
