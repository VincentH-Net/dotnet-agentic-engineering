using System.Text.RegularExpressions;

namespace Agentic.Check;

// What F3 shows for the highlighted row of the recommendation prompt.
sealed record RecommendationPreview(string Title, IReadOnlyList<string> Lines, string? Error = null)
{
    public bool Success => Error is null;
}

interface IRecommendationPreviewSource
{
    Task<RecommendationPreview> LoadAsync(RecommendationSelectionItem item, CancellationToken cancellationToken);
}

// Directives are previewed from the content the plan already holds. Skills are previewed through
// `gh skill preview` at the ref an install would use, with gh's output captured instead of paged,
// so the prompt can show it in place and redraw the list afterwards.
sealed partial class RecommendationPreviewSource(ICommandRunner githubRunner, string workingDirectory) : IRecommendationPreviewSource
{
    // gh must not open its own pager into a captured stream.
    static readonly IReadOnlyDictionary<string, string?> PreviewEnvironment = new Dictionary<string, string?>
    {
        ["GH_PAGER"] = "cat",
        ["GH_FORCE_TTY"] = null,
        ["NO_COLOR"] = "1"
    };

    internal static bool CanPreview(RecommendationSelectionItem item)
        => item.Kind switch
        {
            RecommendationSelectionKind.Directive => item.Directive is not null,
            RecommendationSelectionKind.Skill => item.Skill is not null,
            _ => false
        };

    public async Task<RecommendationPreview> LoadAsync(RecommendationSelectionItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!CanPreview(item))
        {
            return new(item.Display, [], "No preview is available for this item.");
        }

        if (item.Directive is { } directive)
        {
            return new(FormatDirectiveTitle(directive), SplitLines(directive.Content));
        }

        var skill = item.Skill!;
        string title = FormatSkillTitle(skill);
        var result = await githubRunner
            .RunAsync("gh", PreviewArguments(skill), workingDirectory, cancellationToken, PreviewEnvironment)
            .ConfigureAwait(false);
        return result.Success
            ? new(title, SplitLines(result.StandardOutput))
            : new(title, [], FormatError(result));
    }

    internal static IReadOnlyList<string> PreviewArguments(SkillManifestEntry skill)
        => ["skill", "preview", skill.SourceRepo, string.IsNullOrWhiteSpace(skill.ResolvedSourceRef) ? skill.InstallArg : $"{skill.InstallArg}@{skill.ResolvedSourceRef}"];

    internal static string FormatDirectiveTitle(DirectivePlanItem directive)
        => $"{directive.Name} directive";

    internal static string FormatSkillTitle(SkillManifestEntry skill)
        => string.IsNullOrWhiteSpace(skill.ResolvedSourceRef)
            ? $"{skill.LocalFolder} from {skill.SourceRepo}"
            : $"{skill.LocalFolder} from {skill.SourceRepo} at {skill.ResolvedSourceRef}";

    // gh prints "!" notices before the actual error; the notices do not explain the failure.
    internal static string FormatError(CommandResult result)
    {
        string[] lines = [.. $"{result.StandardError}\n{result.StandardOutput}"
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('!'))];
        return lines.Length > 0
            ? string.Join(' ', lines)
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gh skill preview exited with code {result.ExitCode}.");
    }

    // Terminal escape sequences would corrupt the in-place rendering; gh's rendered text pads every
    // line to its wrap width, so trailing blanks and empty lines at the end are dropped.
    internal static IReadOnlyList<string> SplitLines(string content)
    {
        List<string> lines = [.. content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => EscapeSequences().Replace(line, string.Empty).TrimEnd())];
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant)]
    private static partial Regex EscapeSequences();
}
