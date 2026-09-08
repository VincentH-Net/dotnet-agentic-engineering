using System.Globalization;
using System.Text;

namespace Agentic.Check.LiveTests;

sealed record SkillRepositoryScan(SkillSourceReview Review, SkillSourceSnapshot Stable, SkillSourceSnapshot Preview, IReadOnlyList<SkillDiscoveryItem> Items);

sealed record SkillScanError(string SourceRepo, string Message);

static class SkillDiscoveryReport
{
    internal static string Render(IReadOnlyList<SkillRepositoryScan> scans, IReadOnlyList<SkillScanError> errors, DateTimeOffset generatedAt)
    {
        StringBuilder report = new("# Skill maintenance review\n\n");
        _ = report.AppendLine(CultureInfo.InvariantCulture, $"Generated: {generatedAt:yyyy-MM-dd HH:mm zzz}\n");
        _ = report.AppendLine("[New candidates](#new-candidates) | [Missing / moved skills](#missing--moved-skills) | [Alternative locations](#alternative-locations) | [Excluded skills](#excluded-skills)\n");
        if (errors.Count > 0)
        {
            _ = report.AppendLine("## Scan errors\n\nThese repos are incomplete. Do not advance their review baselines.\n");
            _ = report.AppendLine("| Repo | Error |\n| --- | --- |");
            foreach (var error in errors.OrderBy(error => error.SourceRepo, StringComparer.Ordinal))
            {
                _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(error.SourceRepo)} | {Escape(error.Message)} |");
            }

            _ = report.AppendLine();
        }

        var rows = scans.SelectMany(scan => scan.Items.Select(item => (Scan: scan, Item: item)))
            .OrderBy(row => SkillOrdering.GetSourceRepoOrder(row.Scan.Review.SourceRepo))
            .ThenBy(row => row.Scan.Review.SourceRepo, StringComparer.Ordinal)
            .ThenBy(row => row.Item.Skill.Path, StringComparer.Ordinal)
            .ThenBy(row => row.Item.Availability, StringComparer.Ordinal).ToArray();

        _ = report.AppendLine("## New candidates\n");
        var candidates = rows.Where(row => row.Item.Kind == SkillDiscoveryKind.NewCandidate).ToArray();
        _ = report.AppendLine(candidates.Length == 0 ? "None.\n" : "| Repo | Folder | Skill | Available in |\n| --- | --- | --- | --- |");
        foreach (var (scan, item) in candidates)
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(scan.Review.SourceRepo)} | {Escape(ParentFolder(item.Skill.Path))} | {SkillLink(scan, item)} | {item.Availability} |");
        }

        _ = report.AppendLine("\n## Missing / moved skills\n");
        var missing = rows.Where(row => row.Item.Kind is SkillDiscoveryKind.MissingUpstream or SkillDiscoveryKind.PossibleMove).ToArray();
        _ = report.AppendLine(missing.Length == 0 ? "None.\n" : "| Repo | Skill path | Status | Source | Previous path / issue |\n| --- | --- | --- | --- | --- |");
        foreach (var (scan, item) in missing)
        {
            string status = item.Kind == SkillDiscoveryKind.PossibleMove ? "Possible move" : "Missing";
            string issue = item.RelatedPaths is { Count: > 0 } ? string.Join(", ", item.RelatedPaths)
                : item.Detail.StartsWith("Ambiguous", StringComparison.Ordinal) ? "Ambiguous match" : "No matching path";
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(scan.Review.SourceRepo)} | {Escape(item.Skill.Path)} | {status} | {item.Availability} | {Escape(issue)} |");
        }

        _ = report.AppendLine("\n## Alternative locations\n");
        var alternatives = rows.Where(row => row.Item.Kind == SkillDiscoveryKind.AlternativeLocation)
            .GroupBy(row => (Repo: row.Scan.Review.SourceRepo, Folder: ParentFolder(row.Item.Skill.Path))).ToArray();
        _ = report.AppendLine(alternatives.Length == 0 ? "None.\n" : "Copies differ or could not be verified identical.\n\n| Repo | Alternative parent folder | Included parent folders |\n| --- | --- | --- |");
        foreach (var group in alternatives)
        {
            string included = string.Join(", ", group.SelectMany(row => row.Item.RelatedPaths ?? []).Select(ParentFolder).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(group.Key.Repo)} | {Escape(group.Key.Folder)} | {Escape(included)} |");
        }

        _ = report.AppendLine("\n## Excluded skills\n");
        var excluded = rows.Where(row => row.Item.Kind == SkillDiscoveryKind.ExcludedAtBaseline).GroupBy(row => row.Scan.Review.SourceRepo).ToArray();
        _ = report.AppendLine(excluded.Length == 0 ? "None.\n" : string.Join(" | ", excluded.Select(group => $"[{Escape(group.Key)}](#excluded-{RepoAnchor(group.Key)})")) + "\n");
        foreach (var group in excluded)
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"### Excluded: {group.Key}\n");
            _ = report.AppendLine("| Folder | Skill | Available in |\n| --- | --- | --- |");
            foreach (var (scan, item) in group)
            {
                _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {Escape(ParentFolder(item.Skill.Path))} | {SkillLink(scan, item)} | {item.Availability} |");
            }

            _ = report.AppendLine();
        }

        _ = report.AppendLine("[Source snapshots and review baselines](skill-discovery-sources.json)");
        return report.ToString();
    }

    internal static string ParentFolder(string skillPath)
    {
        string[] parts = skillPath.Split('/');
        return parts.Length >= 3 ? string.Join('/', parts[..^2]) : "(root)";
    }

    static string SkillLink(SkillRepositoryScan scan, SkillDiscoveryItem item)
    {
        string sha = item.Availability.StartsWith("stable", StringComparison.Ordinal) && !item.Availability.Contains("preview", StringComparison.Ordinal)
            ? scan.Stable.CommitSha : scan.Preview.CommitSha;
        string path = string.Join('/', item.Skill.Path.Split('/').Select(Uri.EscapeDataString));
        return $"[{Escape(item.Skill.Name)}](https://github.com/{scan.Review.SourceRepo}/blob/{sha}/{path})";
    }

    // GitHub Markdown heading anchors use lowercase characters, not comparison-normalized identifiers.
    static string RepoAnchor(string repo)
        => string.Concat(repo.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').Select(char.ToLowerInvariant));

    static string Escape(string value) => SkillDiscovery.Escape(value).Replace("[", "&#91;", StringComparison.Ordinal).Replace("]", "&#93;", StringComparison.Ordinal);
}
