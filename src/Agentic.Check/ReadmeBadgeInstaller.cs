namespace Agentic.Check;

sealed record ReadmeBadgePlan(string File, bool IsCurrent, string Display)
{
    public const string Action = "add";

    public string Status => IsCurrent ? "up to date" : Action;
}

sealed record ReadmeBadgeReport(string File, string Action, bool Success, string? Error);

// A README badge that links to this repository shows, on the page people look at first and at no
// cost in agent context, that a repository is built with dna. It is offered whenever a check has
// something else to apply, selected like every other recommendation and deselectable there, so a
// declined offer returns only with the next piece of work. A README that already links to the
// repository counts as covered.
static class ReadmeBadgeInstaller
{
    internal static SkillDependency Identity { get; } = new(string.Empty, "readme-badge");

    internal const string Badge = "[![built with: dna](https://img.shields.io/badge/built%20with-dna-512BD4)](" + ToolHeader.RepositoryUrl + ")";

    internal static ReadmeBadgePlan? Plan(string targetDirectory)
    {
        string? file = Directory.Exists(targetDirectory)
            ? Directory.EnumerateFiles(targetDirectory).FirstOrDefault(path => Path.GetFileName(path).Equals("README.md", StringComparison.OrdinalIgnoreCase))
            : null;
        if (file is null)
        {
            return null;
        }

        bool current = File.ReadAllText(file).Contains(ToolHeader.RepositoryUrl, StringComparison.OrdinalIgnoreCase);
        return new(file, current, Path.GetRelativePath(targetDirectory, file));
    }

    internal static SkillManifestEntry Action(ReadmeBadgePlan plan)
        => new(string.Empty, Identity.InstallArg, "README badge: built with dna", string.Empty, [],
            recommendationAction: ReadmeBadgePlan.Action,
            version: plan.Display);

    internal static async Task<ReadmeBadgeReport> EnsureAsync(ReadmeBadgePlan plan, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return new(plan.File, ReadmeBadgePlan.Action, true, null);
        }

        try
        {
            string content = await File.ReadAllTextAsync(plan.File, cancellationToken).ConfigureAwait(false);
            string updated = Insert(content);
            if (!ReferenceEquals(updated, content))
            {
                await File.WriteAllTextAsync(plan.File, updated, cancellationToken).ConfigureAwait(false);
            }

            string written = await File.ReadAllTextAsync(plan.File, cancellationToken).ConfigureAwait(false);
            return written.Contains(ToolHeader.RepositoryUrl, StringComparison.OrdinalIgnoreCase)
                ? new(plan.File, ReadmeBadgePlan.Action, true, null)
                : new(plan.File, ReadmeBadgePlan.Action, false, $"Validation failed: {plan.File} does not link to {ToolHeader.RepositoryUrl}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(plan.File, ReadmeBadgePlan.Action, false, exception.Message);
        }
    }

    // Badges conventionally follow the level-one heading, separated by blank lines; a README without
    // one gets the badge first. A README that already links to the repository is returned unchanged.
    internal static string Insert(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Contains(ToolHeader.RepositoryUrl, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = [.. content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        int heading = lines.FindIndex(line => line.StartsWith("# ", StringComparison.Ordinal));
        int index = heading < 0 ? 0 : heading + 1;
        bool afterBlankLine = heading >= 0 && index < lines.Count && lines[index].Trim().Length == 0;
        if (afterBlankLine)
        {
            index++;
        }

        lines.InsertRange(index, heading >= 0 && !afterBlankLine ? [string.Empty, Badge, string.Empty] : [Badge, string.Empty]);
        return string.Join(newline, lines);
    }
}
