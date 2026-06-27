using Spectre.Console;

namespace Agentic.Check;

interface IUserPrompts
{
    Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken);

    Task<RecommendationSelectionResult> SelectRecommendationsAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        CancellationToken cancellationToken);
}

sealed record RecommendationSelectionResult(
    IReadOnlyList<DirectivePlanItem> SelectedDirectives,
    IReadOnlyList<SkillManifestEntry> SelectedSkills);

sealed class SpectreUserPrompts(IAnsiConsole console) : IUserPrompts
{
    public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfirmationPrompt confirmationPrompt = new(prompt)
        {
            DefaultValue = defaultValue
        };
        return Task.FromResult(console.Prompt(confirmationPrompt));
    }

    public Task<RecommendationSelectionResult> SelectRecommendationsAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new RecommendationSelectionPrompt(console).PromptAsync(recommendedDirectives, missingSkills, cancellationToken);
    }
}

interface IReporter
{
    void Plain(string message);

    void Bold(string message);

    void Info(string message);

    void Success(string message);

    void Warning(string message);

    void Error(string message);

    void Summary(
        string repoRoot,
        IReadOnlySet<string> technologies,
        IReadOnlyList<UnoGateReport> unoGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount);

    Task RunProgressAsync(
        string description,
        int total,
        Func<Action, Task> action,
        CancellationToken cancellationToken);
}

sealed class SpectreReporter(IAnsiConsole console) : IReporter
{
    internal const string SummaryLabelColumnHeader = "Check";
    internal const string SummaryValueColumnHeader = "Status";

    public void Header()
    {
        foreach (var line in ToolHeader.Lines)
        {
            console.MarkupLine(
                Styled(ToolHeader.AgenticColor, line.Agentic)
                + Markup.Escape(line.Separator)
                + Styled(ToolHeader.CheckColor, line.Check));
        }

        console.MarkupLine(ToolHeader.ProductLineMarkup);
        console.MarkupLine(ToolHeader.SeparatorMarkup(console.Profile.Width));
        console.MarkupLine(Markup.Escape(ToolHeader.Description));
        console.MarkupLine(ToolHeader.RepositoryLinkMarkup);
        console.MarkupLine(ToolHeader.SeparatorMarkup(console.Profile.Width));
        console.WriteLine();
    }

    static string Styled(string color, string value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : $"[bold {color}]{Markup.Escape(value)}[/]";

    public void Plain(string message)
        => console.MarkupLine(Markup.Escape(message));

    public void Bold(string message)
        => console.MarkupLine($"[bold]{Markup.Escape(message)}[/]");

    public void Info(string message)
        => console.MarkupLineInterpolated($"[grey]{message}[/]");

    public void Success(string message)
        => console.MarkupLineInterpolated($"[green]{message}[/]");

    public void Warning(string message)
        => console.MarkupLineInterpolated($"[yellow]{message}[/]");

    public void Error(string message)
        => console.MarkupLineInterpolated($"[red]{message}[/]");

    public void Summary(
        string repoRoot,
        IReadOnlySet<string> technologies,
        IReadOnlyList<UnoGateReport> unoGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
        => console.Write(CreateSummaryTable(
            repoRoot,
            technologies,
            unoGates,
            targetAgents,
            skillsDirectories,
            directiveSummary,
            recommendedCount,
            missingCount,
            outdatedCount));

    internal static Table CreateSummaryTable(
        string repoRoot,
        IReadOnlySet<string> technologies,
        IReadOnlyList<UnoGateReport> unoGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
    {
        Table table = new()
        {
            Border = TableBorder.HeavyHead,
            ShowRowSeparators = true
        };
        _ = table.AddColumn(new TableColumn(new Markup(SummaryHeaderMarkup(SummaryLabelColumnHeader, ToolHeader.CheckColor))));
        _ = table.AddColumn(new TableColumn(new Markup(SummaryHeaderMarkup(SummaryValueColumnHeader, ToolHeader.AgenticColor))));
        _ = table.AddRow("Repository", Markup.Escape(repoRoot));
        _ = table.AddRow("Stack", Markup.Escape(FormatStack(technologies, unoGates)));
        _ = table.AddRow("Target agents", Markup.Escape(targetAgents));
        _ = table.AddRow("Repo skills directories", Markup.Escape(FormatSkillsDirectories(repoRoot, skillsDirectories)));
        _ = table.AddRow("Create AGENTS.md", directiveSummary.CreateAgentsFile ? "yes" : "no");
        _ = table.AddRow("Create CLAUDE.md", directiveSummary.CreateClaudeFile ? "yes" : "no");
        _ = table.AddRow("Recommended directives", Markup.Escape(FormatDirectiveSummary(directiveSummary)));
        _ = table.AddRow("Recommended skills", Markup.Escape(FormatSkillSummary(recommendedCount, missingCount, outdatedCount)));
        return table;
    }

    internal static string SummaryHeaderMarkup(string header, string color)
        => $"[bold {color}]{Markup.Escape(header)}[/]";

    public async Task RunProgressAsync(
        string description,
        int total,
        Func<Action, Task> action,
        CancellationToken cancellationToken)
        => await console.Progress()
            .AutoClear(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            ])
            .StartAsync(async context =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var task = context.AddTask(description, maxValue: Math.Max(total, 1));
                await action(() => task.Increment(1)).ConfigureAwait(false);
                if (!task.IsFinished)
                {
                    task.Value = task.MaxValue;
                }
            }).ConfigureAwait(false);

    internal static string FormatDirectiveSummary(DirectiveSummary directiveSummary)
        => FormatRecommendationStatus(
            directiveSummary.RecommendedCount,
            directiveSummary.MissingCount,
            directiveSummary.OutdatedCount);

    internal static string FormatSkillSummary(int recommendedCount, int missingCount, int outdatedCount)
        => FormatRecommendationStatus(recommendedCount, missingCount, outdatedCount);

    internal static string FormatStack(IReadOnlySet<string> technologies, IReadOnlyList<UnoGateReport> unoGates)
    {
        List<string> lines = [];
        if (technologies.Contains(TechnologyNames.Uno, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("Uno Platform");
            lines.Add($"  UI update pattern: {FormatUnoGateValues(unoGates, "presentation")}");
            lines.Add($"  Markup type: {FormatUnoGateValues(unoGates, "markup")}");
            lines.Add($"  Design system: {FormatUnoGateValues(unoGates, "theme")}");
        }

        if (technologies.Contains(TechnologyNames.Orleans, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("Microsoft Orleans");
        }

        if (technologies.Contains(TechnologyNames.AspNetCore, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("ASP.NET");
        }

        if (technologies.Contains(TechnologyNames.Dotnet, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(".NET");
        }

        if (technologies.Contains(TechnologyNames.Foundation, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("Agentic Foundation");
        }

        return string.Join(Environment.NewLine, lines);
    }

    static string FormatUnoGateValues(IReadOnlyList<UnoGateReport> unoGates, string gate)
    {
        string[] values = [.. unoGates
            .SelectMany(report => report.GetValues(gate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    internal static string FormatRecommendationStatus(int recommendedCount, int missingCount, int outdatedCount)
    {
        int upToDateCount = Math.Max(0, recommendedCount - missingCount - outdatedCount);
        List<string> parts = [];
        if (missingCount > 0)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{missingCount} missing"));
        }

        if (outdatedCount > 0)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{outdatedCount} update(s) available"));
        }

        if (upToDateCount > 0)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{upToDateCount} up to date"));
        }

        return parts.Count switch
        {
            0 => "up to date",
            1 => $"all {parts[0]}",
            _ => string.Join(", ", parts)
        };
    }

    internal static string FormatSkillsDirectories(string repoRoot, IReadOnlyList<string> skillsDirectories)
        => string.Join(Environment.NewLine, skillsDirectories.Select(directory => FormatSkillsDirectory(repoRoot, directory)));

    static string FormatSkillsDirectory(string repoRoot, string skillsDirectory)
    {
        string relativePath = Path.GetRelativePath(repoRoot, skillsDirectory);
        return relativePath.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            ? skillsDirectory
            : relativePath;
    }
}

sealed class NullReporter : IReporter
{
    public void Plain(string message)
    {
    }

    public void Bold(string message)
    {
    }

    public void Info(string message)
    {
    }

    public void Success(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message)
    {
    }

    public void Summary(
        string repoRoot,
        IReadOnlySet<string> technologies,
        IReadOnlyList<UnoGateReport> unoGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
    {
    }

    public async Task RunProgressAsync(
        string description,
        int total,
        Func<Action, Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await action(() => { }).ConfigureAwait(false);
    }
}
