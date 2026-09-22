namespace Agentic.Check.Tests;

public sealed class CodexRulesInstallerTests
{
    static StackDetectionResult Stack(params string[] technologies)
        => new(new HashSet<string>(technologies, StringComparer.OrdinalIgnoreCase), [], []);

    [Fact]
    public void PlanMissesEveryRuleInAnEmptyTarget()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");

        var plan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet));

        Assert.False(plan.IsCurrent);
        Assert.False(plan.FileExists);
        Assert.Equal("install", plan.Action);
        Assert.Equal(["dotnet", "dnx", "dna"], plan.Missing.Select(rule => rule.Command));
        Assert.Equal(Path.Combine(tempDirectory.Path, ".codex", "rules", "dna-dotnet.rules"), plan.File);
        Assert.Equal("Codex rules: run dotnet outside the sandbox (install)", RecommendationSelectionPrompt.FormatSkillListItem(CodexRulesInstaller.Action(plan)));
    }

    [Fact]
    public async Task EnsureWritesMissingRulesAndASecondPlanIsCurrent()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet));

        var result = await CodexRulesInstaller.EnsureAsync(plan, false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["dotnet", "dnx", "dna"], result.Rules);
        string content = await File.ReadAllTextAsync(plan.File, CancellationToken.None);
        Assert.StartsWith("# Installed by dna check", content, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"dotnet\"],\n    decision = \"allow\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("New-View", content, StringComparison.Ordinal);
        var replan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet));
        Assert.True(replan.IsCurrent);
        Assert.Equal("up to date", replan.Status);
    }

    [Fact]
    public async Task RulesInOtherFilesCountAsInstalledAndAreNeverRewritten()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".codex/rules/team.rules", """
            # prefix_rule(pattern = ["dna"]) is commented out
            prefix_rule(pattern = ["dotnet"], justification = "team rule (no decision means allow)")
            prefix_rule(
                pattern = ["dnx"],
                decision = "prompt",
            )
            """);
        string teamRulesFile = Path.Combine(tempDirectory.Path, ".codex", "rules", "team.rules");
        string teamRulesBefore = await File.ReadAllTextAsync(teamRulesFile, CancellationToken.None);
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet));

        Assert.Equal(["dnx", "dna"], plan.Missing.Select(rule => rule.Command));
        Assert.Equal("install", plan.Action);

        _ = await CodexRulesInstaller.EnsureAsync(plan, false, CancellationToken.None);

        string installed = await File.ReadAllTextAsync(plan.File, CancellationToken.None);
        Assert.DoesNotContain("pattern = [\"dotnet\"]", installed, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"dnx\"]", installed, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"dna\"]", installed, StringComparison.Ordinal);
        Assert.Equal(teamRulesBefore, await File.ReadAllTextAsync(teamRulesFile, CancellationToken.None));
    }

    [Fact]
    public async Task UnoStackAppendsTheNewViewRuleToTheExistingFile()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        _ = await CodexRulesInstaller.EnsureAsync(CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet)), false, CancellationToken.None);
        string before = await File.ReadAllTextAsync(CodexRulesInstaller.FilePath(tempDirectory.Path), CancellationToken.None);

        var plan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet, TechnologyNames.Uno));
        var result = await CodexRulesInstaller.EnsureAsync(plan, false, CancellationToken.None);

        Assert.Equal("update", plan.Action);
        Assert.Equal(["pwsh ./New-View.ps1"], result.Rules);
        string after = await File.ReadAllTextAsync(plan.File, CancellationToken.None);
        Assert.StartsWith(before, after, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"pwsh\", \"./New-View.ps1\"]", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunReportsWithoutWriting()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path, Stack(TechnologyNames.Dotnet));

        var result = await CodexRulesInstaller.EnsureAsync(plan, true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("install", result.Action);
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.Path, ".codex")));
    }

    [Theory]
    [InlineData("prefix_rule(pattern = [\"dotnet\"])", "dotnet")]
    [InlineData("prefix_rule(pattern=['dotnet','build'], decision=\"allow\")", "dotnet\nbuild")]
    [InlineData("prefix_rule(\n  pattern = [\"pwsh\", \"./New-View.ps1\"], # trailing comment\n  justification = \"has ) and ] inside\",\n)", "pwsh\n./New-View.ps1")]
    public void AllowedPatternKeysReadsStarlarkRules(string content, string expectedKey)
        => Assert.Equal([expectedKey], CodexRulesInstaller.AllowedPatternKeys(content));

    [Theory]
    [InlineData("prefix_rule(pattern = [\"dotnet\"], decision = \"prompt\")")]
    [InlineData("prefix_rule(pattern = [\"dotnet\"], decision = \"forbidden\")")]
    [InlineData("# prefix_rule(pattern = [\"dotnet\"])")]
    [InlineData("prefix_rule(pattern = [\"dotnet\", [\"build\", \"test\"]])")]
    public void NonAllowOrDifferentPatternsDoNotCountAsInstalled(string content)
        => Assert.DoesNotContain("dotnet", CodexRulesInstaller.AllowedPatternKeys(content));
}
