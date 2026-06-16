using Spectre.Console;

namespace Agentic.Check;

interface IUserPrompts
{
    Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkillManifestEntry>> SelectSkillsAsync(IReadOnlyList<SkillManifestEntry> missingSkills, CancellationToken cancellationToken);
}

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

    public Task<IReadOnlyList<SkillManifestEntry>> SelectSkillsAsync(
        IReadOnlyList<SkillManifestEntry> missingSkills,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new SkillSelectionPrompt(console).PromptAsync(missingSkills, cancellationToken);
    }
}

interface IReporter
{
    void Info(string message);

    void Success(string message);

    void Warning(string message);

    void Error(string message);

    void Summary(string repoRoot, IReadOnlySet<string> technologies, string skillsDirectory, int recommendedCount, int missingCount);
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

    public void Summary(string repoRoot, IReadOnlySet<string> technologies, string skillsDirectory, int recommendedCount, int missingCount)
    {
        Table table = new();
        _ = table.AddColumn("Item");
        _ = table.AddColumn("Value");
        _ = table.AddRow("Repository", Markup.Escape(repoRoot));
        _ = table.AddRow("Stack", Markup.Escape(string.Join(", ", technologies.Order(StringComparer.OrdinalIgnoreCase))));
        _ = table.AddRow("Skills directory", Markup.Escape(skillsDirectory));
        _ = table.AddRow("Recommended skills", recommendedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = table.AddRow("Missing skills", missingCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    public void Summary(string repoRoot, IReadOnlySet<string> technologies, string skillsDirectory, int recommendedCount, int missingCount)
    {
    }
}
