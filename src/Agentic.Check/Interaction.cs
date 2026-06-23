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
        _ = table.AddColumn("Item");
        _ = table.AddColumn("Value");
        _ = table.AddRow("Repository", Markup.Escape(repoRoot));
        _ = table.AddRow("Stack", Markup.Escape(string.Join(", ", technologies.Order(StringComparer.OrdinalIgnoreCase))));
        _ = table.AddRow("Target agents", Markup.Escape(targetAgents));
        _ = table.AddRow("Skills directories", Markup.Escape(string.Join(Environment.NewLine, skillsDirectories)));
        _ = table.AddRow("Create AGENTS.md", directiveSummary.CreateAgentsFile ? "yes" : "no");
        _ = table.AddRow("Create CLAUDE.md", directiveSummary.CreateClaudeFile ? "yes" : "no");
        _ = table.AddRow("Recommended directives", directiveSummary.RecommendedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Missing directives", directiveSummary.MissingCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Outdated directives", directiveSummary.OutdatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Recommended skills", recommendedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Missing skills", missingCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Outdated skills", outdatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        console.Write(table);
    }
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
