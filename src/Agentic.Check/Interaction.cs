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
    void Info(string message);

    void Success(string message);

    void Warning(string message);

    void Error(string message);

    void Summary(
        string repoRoot,
        IReadOnlySet<string> technologies,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount);
}

sealed class SpectreReporter(IAnsiConsole console) : IReporter
{
    internal const string SummaryLabelColumnHeader = "Check";
    internal const string SummaryValueColumnHeader = "Status";

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
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
    {
        Table table = new();
        _ = table.AddColumn(SummaryLabelColumnHeader);
        _ = table.AddColumn(SummaryValueColumnHeader);
        _ = table.AddRow("Repository", Markup.Escape(repoRoot));
        _ = table.AddRow("Stack", Markup.Escape(string.Join(", ", technologies.Order(StringComparer.OrdinalIgnoreCase))));
        _ = table.AddRow("Target agents", Markup.Escape(targetAgents));
        _ = table.AddRow("Skills directories", Markup.Escape(string.Join(Environment.NewLine, skillsDirectories)));
        _ = table.AddRow("Create AGENTS.md", directiveSummary.CreateAgentsFile ? "yes" : "no");
        _ = table.AddRow("Create CLAUDE.md", directiveSummary.CreateClaudeFile ? "yes" : "no");
        _ = table.AddRow("Directives", Markup.Escape(FormatDirectiveSummary(directiveSummary)));
        _ = table.AddRow("Skills", Markup.Escape(FormatSkillSummary(recommendedCount, missingCount, outdatedCount)));
        console.Write(table);
    }

    internal static string FormatDirectiveSummary(DirectiveSummary directiveSummary)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"recommended: {directiveSummary.RecommendedCount}, with {directiveSummary.MissingCount} missing and {directiveSummary.OutdatedCount} outdated");

    internal static string FormatSkillSummary(int recommendedCount, int missingCount, int outdatedCount)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"recommended: {recommendedCount}, with {missingCount} missing and {outdatedCount} outdated");
}

sealed class NullReporter : IReporter
{
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
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
    {
    }
}
