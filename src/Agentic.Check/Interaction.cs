using Spectre.Console;
using Spectre.Console.Rendering;

namespace Agentic.Check;

interface IUserPrompts
{
    Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken);

    Task<RecommendationSelectionResult> SelectRecommendationsAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        string targetDirectory,
        IReadOnlyList<string> skillsDirectories,
        CancellationToken cancellationToken);

    Task WaitForHelpKeyAsync(string url, string purpose, CancellationToken cancellationToken);
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
        string targetDirectory,
        IReadOnlyList<string> skillsDirectories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new RecommendationSelectionPrompt(console).PromptAsync(
            recommendedDirectives,
            missingSkills,
            targetDirectory,
            skillsDirectories,
            cancellationToken);
    }

    public async Task WaitForHelpKeyAsync(string url, string purpose, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        console.MarkupLine(ToolHeader.KeyMarkup("F2") + $" to open [link={url}]{Markup.Escape(url)}[/] for {Markup.Escape(purpose)}");
        console.MarkupLine($"[{SpectreReporter.InfoColor}]Press any other key to exit[/]");
        if (!console.Profile.Capabilities.Interactive)
        {
            return;
        }

        ConsoleKeyInfo? key;
        try
        {
            key = await console.Input
                .ReadKeyAsync(true, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;
        }

        if (key?.Key == ConsoleKey.F1)
        {
            _ = BrowserLauncher.Open(ToolHeader.RepositoryUrl);
        }
        else if (key?.Key == ConsoleKey.F2)
        {
            _ = BrowserLauncher.Open(url);
        }
    }
}

interface IReporter
{
    void Plain(string message);

    void Bold(string message);

    void Bold(string message, string color);

    void Info(string message);

    void Success(string message);

    void Warning(string message);

    void Error(string message);

    void Summary(
        string targetDirectory,
        IReadOnlySet<string> technologies,
        IReadOnlyList<InstallGateReport> installGates,
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
    internal const string InfoColor = "grey";

    public void Header()
    {
        int headerContentWidth = ToolHeader.HeaderContentWidth;
        foreach (var line in ToolHeader.Lines)
        {
            console.MarkupLine(
                CenterMarkup(
                    Styled(ToolHeader.AgenticColor, line.Agentic)
                    + Markup.Escape(line.Separator)
                    + Styled(ToolHeader.CheckColor, line.Check),
                    ToolHeader.HeaderArtWidth,
                    headerContentWidth));
        }

        console.WriteLine();
        console.MarkupLine(CenterMarkup(ToolHeader.ProductLineMarkupContent, ToolHeader.ProductLineContent.Length, headerContentWidth));
        console.MarkupLine(ToolHeader.SeparatorMarkup(headerContentWidth));
        foreach (string line in ToolHeader.DescriptionLines)
        {
            console.MarkupLine(CenterMarkup(Markup.Escape(line), line.Length, headerContentWidth));
        }

        console.MarkupLine(CenterMarkup(ToolHeader.RepositoryHelpMarkup, ToolHeader.RepositoryHelp.Length, headerContentWidth));
        console.MarkupLine(ToolHeader.SeparatorMarkup(headerContentWidth));
        console.WriteLine();
    }

    static string CenterMarkup(string markup, int visibleLength, int width)
    {
        int padding = Math.Max(0, (width - visibleLength) / 2);
        return new string(' ', padding) + markup;
    }

    static string Styled(string color, string value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : $"[bold {color}]{Markup.Escape(value)}[/]";

    public void Plain(string message)
        => console.MarkupLine(Markup.Escape(message));

    public void Bold(string message)
        => console.MarkupLine($"[bold]{Markup.Escape(message)}[/]");

    public void Bold(string message, string color)
        => console.MarkupLine($"[bold {color}]{Markup.Escape(message)}[/]");

    public void Info(string message)
        => console.MarkupLineInterpolated($"[{InfoColor}]{message}[/]");

    public void Success(string message)
        => console.MarkupLineInterpolated($"[green]{message}[/]");

    public void Warning(string message)
        => console.MarkupLineInterpolated($"[yellow]{message}[/]");

    public void Error(string message)
        => console.MarkupLineInterpolated($"[red]{message}[/]");

    public void Summary(
        string targetDirectory,
        IReadOnlySet<string> technologies,
        IReadOnlyList<InstallGateReport> installGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
        => console.Write(CreateSummaryTable(
            targetDirectory,
            technologies,
            installGates,
            targetAgents,
            skillsDirectories,
            directiveSummary,
            recommendedCount,
            missingCount,
            outdatedCount));

    internal static Table CreateSummaryTable(
        string targetDirectory,
        IReadOnlySet<string> technologies,
        IReadOnlyList<InstallGateReport> installGates,
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
            BorderStyle = Style.Parse(InfoColor),
            ShowRowSeparators = true
        };
        _ = table.AddColumn(new TableColumn(new Markup(SummaryHeaderMarkup(SummaryLabelColumnHeader, ToolHeader.CheckColor))));
        _ = table.AddColumn(new TableColumn(new Markup(SummaryHeaderMarkup(SummaryValueColumnHeader, ToolHeader.AgenticColor))));
        _ = table.AddRow("Target directory", Markup.Escape(targetDirectory));
        _ = table.AddRow("Stack", Markup.Escape(FormatStack(technologies, installGates)));
        _ = table.AddRow("Target agents", Markup.Escape(targetAgents));
        _ = table.AddRow("Skills directories", Markup.Escape(FormatSkillsDirectories(targetDirectory, skillsDirectories)));
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
            .Columns(CreateProgressColumns(description))
            .StartAsync(async context =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string taskDescription = description == ActionOutputFormatter.ProgressIndent ? "Applying actions" : description;
                var task = context.AddTask(taskDescription, maxValue: Math.Max(total, 1));
                await action(() => task.Increment(1)).ConfigureAwait(false);
                if (!task.IsFinished)
                {
                    task.Value = task.MaxValue;
                }
            }).ConfigureAwait(false);

    internal static ProgressColumn[] CreateProgressColumns(string description)
        => description == ActionOutputFormatter.ProgressIndent
            ? [new FixedTextProgressColumn(ActionOutputFormatter.ProgressIndent), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn()]
            : CreateProgressColumns();

    internal static ProgressColumn[] CreateProgressColumns()
        => [new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn()];

    sealed class FixedTextProgressColumn(string text) : ProgressColumn
    {
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
            => new Text(text);

        public override int? GetColumnWidth(RenderOptions options)
            => text.Length;
    }

    internal static string FormatDirectiveSummary(DirectiveSummary directiveSummary)
        => FormatRecommendationStatus(
            directiveSummary.RecommendedCount,
            directiveSummary.MissingCount,
            directiveSummary.OutdatedCount);

    internal static string FormatSkillSummary(int recommendedCount, int missingCount, int outdatedCount)
        => FormatRecommendationStatus(recommendedCount, missingCount, outdatedCount);

    internal static string FormatStack(IReadOnlySet<string> technologies, IReadOnlyList<InstallGateReport> installGates)
    {
        List<string> lines = [];
        if (technologies.Contains(TechnologyNames.Uno, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("Uno Platform");
            lines.Add($"  UI update pattern: {FormatGateValues(installGates, TechnologyNames.Uno, "presentation")}");
            lines.Add($"  Markup type: {FormatGateValues(installGates, TechnologyNames.Uno, "markup")}");
            lines.Add($"  Design system: {FormatGateValues(installGates, TechnologyNames.Uno, "theme")}");
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
            if (HasGate(installGates, TechnologyNames.Dotnet, "cli"))
            {
                lines.Add("  CLI");
            }
        }

        if (technologies.Contains(TechnologyNames.Foundation, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add("Agentic Foundation");
        }

        return string.Join(Environment.NewLine, lines);
    }

    static string FormatGateValues(IReadOnlyList<InstallGateReport> installGates, string technology, string gate)
    {
        string[] values = [.. installGates
            .Where(report => report.Technology.Equals(technology, StringComparison.OrdinalIgnoreCase))
            .SelectMany(report => report.GetValues(gate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    static bool HasGate(IReadOnlyList<InstallGateReport> installGates, string technology, string gate)
        => installGates
            .Where(report => report.Technology.Equals(technology, StringComparison.OrdinalIgnoreCase))
            .Any(report => report.GetValues(gate).Count > 0);

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

    internal static string FormatSkillsDirectory(string repoRoot, string skillsDirectory)
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

    public void Bold(string message, string color)
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
        IReadOnlyList<InstallGateReport> installGates,
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
