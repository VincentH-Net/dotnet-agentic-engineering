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
        Assert.Equal(100, ToolHeader.MaxSeparatorWidth);
        Assert.Equal(6, ToolHeader.Lines.Count);
        Assert.All(ToolHeader.Lines, line => Assert.NotEmpty(line.Agentic));
        Assert.Contains(ToolHeader.Lines, line => !string.IsNullOrWhiteSpace(line.Separator));
        Assert.All(ToolHeader.Lines, line => Assert.NotEmpty(line.Check));
        Assert.StartsWith("✓ .NET Agentic Engineering Check ", ToolHeader.ProductLine.TrimStart(), StringComparison.Ordinal);
        Assert.StartsWith("✓ .NET Agentic Engineering Check ", ToolHeader.ProductLineContent, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", ToolHeader.ProductLine, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"\n[bold {ToolHeader.CheckColor}]✓ [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.StartsWith($"[bold {ToolHeader.CheckColor}]✓ [/]", ToolHeader.ProductLineMarkupContent, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.DotNetColor}].NET [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.AgenticColor}]Agentic[/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.DotNetColor}] Engineering [/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains($"[bold underline {ToolHeader.CheckColor}]Check[/]", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.Contains("[grey] ", ToolHeader.ProductLineMarkup, StringComparison.Ordinal);
        Assert.NotEmpty(ToolHeader.Description.Trim());
        Assert.StartsWith("Optimizes your repo for agentic engineering with .NET - based technologies\n", ToolHeader.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("technologies.", ToolHeader.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(ToolHeader.RepositoryUrl, ToolHeader.Description, StringComparison.Ordinal);
        Assert.StartsWith("F1 to learn more at ", ToolHeader.RepositoryHelp, StringComparison.Ordinal);
        Assert.Contains(ToolHeader.KeyMarkup("F1"), ToolHeader.RepositoryHelpMarkup, StringComparison.Ordinal);
        Assert.Contains(" to learn more at ", ToolHeader.RepositoryHelpMarkup, StringComparison.Ordinal);
        Assert.Contains($"[link={ToolHeader.RepositoryUrl}]", ToolHeader.RepositoryHelpMarkup, StringComparison.Ordinal);
        Assert.Contains($"{ToolHeader.RepositoryUrl}[/]", ToolHeader.RepositoryHelpMarkup, StringComparison.Ordinal);
        Assert.Equal(
            ToolHeader.Lines.Max(line => line.Agentic.Length + line.Separator.Length + line.Check.Length),
            ToolHeader.HeaderArtWidth);
        Assert.True(ToolHeader.HeaderContentWidth >= ToolHeader.HeaderArtWidth);
        Assert.True(ToolHeader.HeaderContentWidth >= ToolHeader.RepositoryHelp.Length);
        Assert.Equal($"[bold]{new string('─', 12)}[/]", ToolHeader.SeparatorMarkup(12));
        Assert.Equal("[bold]─[/]", ToolHeader.SeparatorMarkup(0));
        Assert.Equal($"[bold]{new string('─', ToolHeader.MaxSeparatorWidth)}[/]", ToolHeader.SeparatorMarkup(120));
        Assert.DoesNotContain("Author:", ToolHeader.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryColumnHeadersDescribeCheckAndStatus()
    {
        Assert.Equal("Check", SpectreReporter.SummaryLabelColumnHeader);
        Assert.Equal("Status", SpectreReporter.SummaryValueColumnHeader);
        Assert.Equal("grey", SpectreReporter.InfoColor);
        Assert.Equal("[bold green]Check[/]", SpectreReporter.SummaryHeaderMarkup(SpectreReporter.SummaryLabelColumnHeader, ToolHeader.CheckColor));
        Assert.Equal("[bold cyan]Status[/]", SpectreReporter.SummaryHeaderMarkup(SpectreReporter.SummaryValueColumnHeader, ToolHeader.AgenticColor));
    }

    [Fact]
    public void AgentHelpLinesIncludeNamesAndIdsInGhOrder()
    {
        string[] lines = AgentSkillRegistry.FormatAgentHelpLines(68).Split(Environment.NewLine);

        Assert.Equal("  - GitHub Copilot (github-copilot)", lines[0]);
        Assert.Equal("  - Claude Code (claude-code)", lines[1]);
        Assert.Contains("  - Codex (codex)", lines);
        Assert.Equal("  - Zencoder (zencoder)", lines[^1]);
    }

    [Fact]
    public void AgentHelpLinesUseMultipleColumnsWhenTheyFit()
    {
        string[] lines = AgentSkillRegistry.FormatAgentHelpLines(180).Split(Environment.NewLine);

        Assert.True(lines.Length < AgentSkillRegistry.AgentIds.Split(',').Length);
        Assert.Contains("GitHub Copilot (github-copilot)", lines[0], StringComparison.Ordinal);
        Assert.Contains("  - ", lines[0][4..], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("Zencoder (zencoder)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentCliDetectorUsesOnlyClearIdentifyingOutput()
    {
        MappedCommandRunner commandRunner = new();
        commandRunner.Set("copilot", ["version"], new CommandResult(0, "GitHub Copilot CLI 1.0.0", string.Empty));
        commandRunner.Set("claude", ["--help"], new CommandResult(0, "Usage: Claude Code", string.Empty));
        commandRunner.Set("codex", ["--version"], new CommandResult(0, "codex 0.1.0", string.Empty));
        commandRunner.Set("gemini", ["--help"], new CommandResult(0, "1.2.3", string.Empty));
        commandRunner.Set("qwen", ["--help"], new CommandResult(0, "Qwen Code help", string.Empty));

        string defaultAgents = await AgentCliDetector
            .DetectDefaultAgentsAsync(commandRunner, "/repo", CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal("github-copilot,claude-code,codex,qwen-code", defaultAgents);
        Assert.Contains(commandRunner.Calls, call => call.FileName == "gemini" && call.Arguments.SequenceEqual(["--help"]));
    }

    [Fact]
    public async Task AgentCliDetectorFallsBackToCodexWhenNothingIsDetected()
    {
        MappedCommandRunner commandRunner = new();

        string defaultAgents = await AgentCliDetector
            .DetectDefaultAgentsAsync(commandRunner, "/repo", CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal("codex", defaultAgents);
    }

    [Fact]
    public void RecommendationListHeadersUseRequestedColors()
    {
        Assert.Equal(
            $"[bold {ToolHeader.CheckColor}]Directives[/]",
            RecommendationSelectionPrompt.FormatRecommendationKindHeaderMarkup(RecommendationSelectionKind.Directive));
        Assert.Equal(
            $"[bold {ToolHeader.CheckColor}]Skills[/]",
            RecommendationSelectionPrompt.FormatRecommendationKindHeaderMarkup(RecommendationSelectionKind.Skill));
        Assert.Equal(
            $"  [bold {ToolHeader.AgenticColor}]dotnet/skills repo[/]",
            RecommendationSelectionPrompt.FormatRecommendationSourceHeaderMarkup("dotnet/skills"));
        Assert.Equal(
            $"    [bold {ToolHeader.AgenticColor}]dotnet-test[/]",
            RecommendationSelectionPrompt.FormatRecommendationPluginHeaderMarkup("dotnet-test"));
    }

    [Fact]
    public void UnknownOptionGuardRejectsOptionLookingTargetDirectory()
    {
        Assert.Equal("--unknown-option", AgenticCheckCli.FindUnknownOption(["--unknown-option"]));
        Assert.Equal("--unknown-option", AgenticCheckCli.FindUnknownOption(["--dry-run", "--unknown-option"]));
        Assert.Null(AgenticCheckCli.FindUnknownOption(["-"]));
        Assert.Null(AgenticCheckCli.FindUnknownOption(["--agents", "--unknown-agent-value"]));
        Assert.Null(AgenticCheckCli.FindUnknownOption(["--report=--option-looking-file"]));
        Assert.True(AgenticCheckCli.IsOptionSpecified(["--agents=codex"], "--agents"));
        Assert.True(AgenticCheckCli.IsOptionSpecified(["--agents", "codex"], "--agents"));
        Assert.False(AgenticCheckCli.IsOptionSpecified(["--", "--agents"], "--agents"));
        Assert.True(AgenticCheckCli.IsHelpRequested(["--help"]));
        Assert.False(AgenticCheckCli.IsHelpRequested(["--", "--help"]));
    }

    [Fact]
    public void SummaryTableShowsRowSeparatorsBetweenChecks()
    {
        var table = SpectreReporter.CreateSummaryTable(
            "/repo",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Dotnet },
            [],
            "codex",
            ["/repo/.agents/skills"],
            new DirectiveSummary(false, false, 3, 0, 0),
            recommendedCount: 2,
            missingCount: 0,
            outdatedCount: 0);

        Assert.True(table.ShowRowSeparators);
        Assert.Same(TableBorder.HeavyHead, table.Border);
        Assert.Equal(Style.Parse(SpectreReporter.InfoColor), table.BorderStyle);
    }

    [Fact]
    public async Task ActionProgressUsesNonEmptyInternalTaskName()
    {
        using StringWriter writer = new();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
        SpectreReporter reporter = new(console);

        var exception = await Record.ExceptionAsync(() => reporter.RunProgressAsync(
            ActionOutputFormatter.ProgressIndent,
            1,
            action =>
            {
                action();
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.Null(exception);
        Assert.DoesNotContain("Applying actions", writer.ToString(), StringComparison.Ordinal);
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
        InstallGateReport dotnetGate = new(
            TechnologyNames.Dotnet,
            "Tool.csproj",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["cli"] = ["cli"]
            });

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
                    "  CLI",
                    "Agentic Foundation"
                ]),
            SpectreReporter.FormatStack(technologies, [unoGate, dotnetGate]));
    }

    [Fact]
    public void StackDisplayOmitsAbsentDotnetSubgates()
    {
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase)
        {
            TechnologyNames.Foundation,
            TechnologyNames.Dotnet
        };

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                [
                    ".NET",
                    "Agentic Foundation"
                ]),
            SpectreReporter.FormatStack(technologies, []));
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
    public void SkillsDirectoriesAreRenderedRelativeToTargetDirectory()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "repo");
        string agentsSkills = Path.Combine(repoRoot, ".agents", "skills");
        string claudeSkills = Path.Combine(repoRoot, ".claude", "skills");

        Assert.Equal(
            string.Join(Environment.NewLine, [Path.Combine(".agents", "skills"), Path.Combine(".claude", "skills")]),
            SpectreReporter.FormatSkillsDirectories(repoRoot, [agentsSkills, claudeSkills]));
    }
}
