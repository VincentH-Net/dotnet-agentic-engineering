namespace Agentic.Check.Tests;

public sealed class ScopeDuplicateScannerTests
{
    [Fact]
    public async Task FindsDirectiveAndSkillDuplicatesInAncestorsAndDescendants()
    {
        using TempDirectory tempDirectory = new();
        string targetDirectory = tempDirectory.CreateDirectory("repo/backend");
        string skillsDirectory = tempDirectory.CreateDirectory("repo/backend/.agents/skills");
        string claudeSkillsDirectory = tempDirectory.CreateDirectory("repo/backend/.claude/skills");
        _ = tempDirectory.CreateDirectory("repo/.git");
        tempDirectory.Write(
            "repo/AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            # foundation-prompt-log
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        tempDirectory.Write("repo/.agents/skills/dotnet-livecharts2/SKILL.md", "# parent skill");
        tempDirectory.Write("repo/.claude/skills/dotnet-livecharts2/SKILL.md", "# parent claude skill");
        tempDirectory.Write(
            "repo/backend/api/AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            # foundation-prompt-log
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        tempDirectory.Write("repo/backend/api/.agents/skills/dotnet-livecharts2/SKILL.md", "# child skill");
        tempDirectory.Write("repo/backend/.hidden/AGENTS.md", "dotnet-agentic-engineering:foundation-prompt-log:start");

        var directive = new DirectivePlanItem("foundation-prompt-log", DirectiveStatuses.Missing, "content");
        var skill = new SkillManifestEntry("owner/repo", "dotnet-livecharts2", "dotnet-livecharts2", TechnologyNames.Dotnet, []);
        var items = new[]
        {
            new RecommendationSelectionItem("directive:foundation-prompt-log", "foundation-prompt-log (install)", RecommendationSelectionKind.Directive, directive, null),
            new RecommendationSelectionItem(RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg), "dotnet-livecharts2 (install)", RecommendationSelectionKind.Skill, null, skill)
        };

        var result = await ScopeDuplicateScanner
            .ScanAsync(items, targetDirectory, [skillsDirectory, claudeSkillsDirectory], null, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(2, result.ActionCount);
        Assert.Equal(
            ["../AGENTS.md", Path.Combine("api", "AGENTS.md")],
            result.LocationsByKey["directive:foundation-prompt-log"]);
        Assert.Equal(
            [
                Path.Combine("..", ".agents", "skills", "dotnet-livecharts2", "SKILL.md"),
                Path.Combine("..", ".claude", "skills", "dotnet-livecharts2", "SKILL.md"),
                Path.Combine("api", ".agents", "skills", "dotnet-livecharts2", "SKILL.md")
            ],
            result.LocationsByKey[RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg)]);
        Assert.Equal(2, result.ScopeCountsByKey[RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg)]);
    }
}
