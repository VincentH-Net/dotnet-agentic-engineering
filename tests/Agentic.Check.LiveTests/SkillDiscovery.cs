namespace Agentic.Check.LiveTests;

enum SkillDiscoveryKind
{
    NewCandidate,
    Deferred,
    PossibleMove,
    MissingUpstream,
    AlternativeLocation,
    IdenticalIncludedCopy,
    ExcludedAtBaseline,
    Included
}

sealed record SkillDiscoveryItem(SkillDiscoveryKind Kind, SkillTreeFile Skill, string Availability, string Detail = "", IReadOnlyList<string>? RelatedPaths = null);

static class SkillDiscovery
{
    internal static IReadOnlyList<SkillDiscoveryItem> Compare(
        string repo,
        IReadOnlyList<SkillTreeFile> baseline,
        IReadOnlyList<SkillTreeFile> stable,
        IReadOnlyList<SkillTreeFile> preview,
        IReadOnlyList<SkillManifestEntry> stableManifest,
        IReadOnlyList<SkillManifestEntry> previewManifest,
        bool stablePredatesReview = false)
    {
        var allFiles = preview.Concat(stable).Concat(baseline).DistinctBy(skill => skill.Path, StringComparer.Ordinal).ToArray();
        var includedPaths = stableManifest.Concat(previewManifest)
            .Where(skill => skill.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase))
            .SelectMany(skill => Match(skill, allFiles))
            .Select(skill => skill.Path)
            .ToHashSet(StringComparer.Ordinal);
        var baselinePaths = baseline.Select(skill => skill.Path).ToHashSet(StringComparer.Ordinal);
        var stablePaths = stable.Select(skill => skill.Path).ToHashSet(StringComparer.Ordinal);
        var previewPaths = preview.Select(skill => skill.Path).ToHashSet(StringComparer.Ordinal);
        var current = preview.Concat(stable).DistinctBy(skill => skill.Path, StringComparer.Ordinal).ToArray();
        var stableIncluded = IncludedFiles(repo, stableManifest, stable);
        var previewIncluded = IncludedFiles(repo, previewManifest, preview);
        List<SkillDiscoveryItem> result = [];
        foreach (var skill in current)
        {
            if (!baselinePaths.Contains(skill.Path) && !previewPaths.Contains(skill.Path) && stablePredatesReview)
            {
                continue;
            }

            string availability = stablePaths.Contains(skill.Path)
                ? previewPaths.Contains(skill.Path) ? "stable + preview" : "stable only"
                : "preview only";
            var kind = includedPaths.Contains(skill.Path)
                ? SkillDiscoveryKind.Included
                : baselinePaths.Contains(skill.Path) ? SkillDiscoveryKind.ExcludedAtBaseline : SkillDiscoveryKind.NewCandidate;
            string detail = "";
            IReadOnlyList<string>? relatedPaths = null;
            if (kind == SkillDiscoveryKind.ExcludedAtBaseline)
            {
                var alternative = AlternativeLocation(skill.Path, stable, preview, stableIncluded, previewIncluded);
                if (alternative is not null)
                {
                    kind = alternative.Value.Kind;
                    detail = alternative.Value.Detail;
                    relatedPaths = alternative.Value.Paths;
                }
            }
            if (kind == SkillDiscoveryKind.NewCandidate)
            {
                // A path removed on the default branch can still exist in an older stable release.
                string[] oldPaths = [.. baseline.Where(old => old.Path != skill.Path && !previewPaths.Contains(old.Path)
                    && old.Name.Equals(skill.Name, StringComparison.Ordinal)).Select(old => old.Path)];
                if (oldPaths.Length > 0)
                {
                    kind = SkillDiscoveryKind.PossibleMove;
                    detail = $"Previously: {string.Join(", ", oldPaths)}. Requires review; not automatically matched.";
                    relatedPaths = oldPaths;
                }
            }

            result.Add(new(kind, skill, availability, detail, relatedPaths));
        }

        AddMissing(repo, stableManifest, stable, allFiles, "stable", result);
        AddMissing(repo, previewManifest, preview, allFiles, "preview", result);
        return [.. result.OrderBy(item => item.Kind).ThenBy(item => item.Skill.Path, StringComparer.Ordinal).ThenBy(item => item.Availability, StringComparer.Ordinal)];
    }

    static IReadOnlyList<SkillTreeFile> IncludedFiles(string repo, IReadOnlyList<SkillManifestEntry> manifest, IReadOnlyList<SkillTreeFile> files)
        => [.. manifest.Where(entry => entry.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase))
            .Select(entry => Match(entry, files)).Where(matches => matches.Count == 1)
            .Select(matches => matches[0]).DistinctBy(file => file.Path, StringComparer.Ordinal)];

    static (SkillDiscoveryKind Kind, string Detail, IReadOnlyList<string> Paths)? AlternativeLocation(string path,
        IReadOnlyList<SkillTreeFile> stable, IReadOnlyList<SkillTreeFile> preview,
        IReadOnlyList<SkillTreeFile> stableIncluded, IReadOnlyList<SkillTreeFile> previewIncluded)
    {
        List<string> locations = [];
        HashSet<string> paths = new(StringComparer.Ordinal);
        bool identical = true;
        foreach (var (mode, files, included) in new[] { (Mode: "stable", Files: stable, Included: stableIncluded), (Mode: "preview", Files: preview, Included: previewIncluded) })
        {
            var copy = files.SingleOrDefault(file => file.Path == path);
            if (copy is null)
            {
                continue;
            }

            var originals = included.Where(file => file.Path != path && file.HasFrontmatter && copy.HasFrontmatter
                && file.Name.Equals(copy.Name, StringComparison.Ordinal)).ToArray();
            if (originals.Length == 0)
            {
                identical = false;
            }

            foreach (var original in originals)
            {
                locations.Add($"{mode}: {original.Path}");
                _ = paths.Add(original.Path);
                identical &= !string.IsNullOrEmpty(copy.DirectoryTreeSha)
                    && copy.DirectoryTreeSha.Equals(original.DirectoryTreeSha, StringComparison.Ordinal);
            }
        }

        return locations.Count == 0 ? null : (
            identical ? SkillDiscoveryKind.IdenticalIncludedCopy : SkillDiscoveryKind.AlternativeLocation,
            $"Included at {string.Join("; ", locations)}. "
                + (identical ? "Identical skill-directory trees." : "Directory contents differ, or equality could not be verified in every available snapshot."),
            [.. paths.Order(StringComparer.Ordinal)]);
    }

    internal static IReadOnlyList<SkillTreeFile> Match(SkillManifestEntry entry, IReadOnlyList<SkillTreeFile> files)
    {
        string argument = entry.InstallArg.Split('@')[0].TrimEnd('/');
        if (argument.Contains('/', StringComparison.Ordinal) || argument == "SKILL.md")
        {
            string exact = argument.EndsWith("SKILL.md", StringComparison.Ordinal) ? argument : $"{argument}/SKILL.md";
            return [.. files.Where(file => file.Path.Equals(exact, StringComparison.Ordinal))];
        }

        var matches = files.Where(file => !file.Path.Split('/').Any(part => part.StartsWith('.')))
            .Where(file => file.Path == $"{argument}/SKILL.md"
                || file.Path.EndsWith($"/{argument}/SKILL.md", StringComparison.Ordinal)).ToArray();
        if (matches.Length <= 1)
        {
            return matches;
        }

        var declaredPlugin = matches.Where(file => file.Path.StartsWith($"plugins/{entry.Plugin}/skills/", StringComparison.Ordinal)).ToArray();
        if (declaredPlugin.Length > 0)
        {
            return declaredPlugin;
        }

        // gh prefers plugins when a repo also keeps a legacy skills layout.
        // Display grouping labels need not be the actual upstream plugin name.
        var plugins = matches.Where(file => file.Path.StartsWith("plugins/", StringComparison.Ordinal)).ToArray();
        return plugins.Length > 0 ? plugins : matches;
    }

    static void AddMissing(string repo, IReadOnlyList<SkillManifestEntry> manifest, IReadOnlyList<SkillTreeFile> files,
        IReadOnlyList<SkillTreeFile> allFiles, string mode, List<SkillDiscoveryItem> result)
    {
        foreach (var entry in manifest.Where(entry => entry.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase)))
        {
            var matches = Match(entry, files);
            if (matches.Count == 1)
            {
                continue;
            }

            var previous = Match(entry, allFiles);
            var skill = previous.Count == 1 ? previous[0] : new SkillTreeFile(entry.InstallArg, "", entry.LocalFolder);
            result.Add(new(SkillDiscoveryKind.MissingUpstream, skill, mode,
                matches.Count == 0 ? $"Manifest argument: {entry.InstallArg}; no matching path." : $"Ambiguous manifest argument: {entry.InstallArg}."));
        }
    }

    internal static bool NeedsReview(SkillDiscoveryItem item)
        => item.Kind is SkillDiscoveryKind.NewCandidate or SkillDiscoveryKind.PossibleMove or SkillDiscoveryKind.MissingUpstream;

    // A deferred skill stays out of the review count until releasedIn reports its trigger release; it then
    // becomes a new candidate again even when the review baseline already contains it.
    internal static IReadOnlyList<SkillDiscoveryItem> ApplyDeferrals(IReadOnlyList<SkillDiscoveryItem> items,
        IReadOnlyList<SkillDeferral> deferrals, Func<SkillDeferral, string?> releasedIn)
        => [.. items.Select(item =>
        {
            if (item.Kind is not (SkillDiscoveryKind.NewCandidate or SkillDiscoveryKind.ExcludedAtBaseline))
                return item;
            var deferral = deferrals.FirstOrDefault(candidate => candidate.SkillName.Equals(item.Skill.Name, StringComparison.Ordinal));
            if (deferral is null)
                return item;
            string? release = releasedIn(deferral);
            return release is null
                ? item with { Kind = SkillDiscoveryKind.Deferred, Detail = $"Waiting for {deferral.WaitingFor} ({deferral.Trigger})." }
                : item with { Kind = SkillDiscoveryKind.NewCandidate, Detail = $"Deferred until {deferral.WaitingFor}; now released in {deferral.TriggerRepo} {release}. Include or exclude it." };
        })];


    internal static string Escape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("|", "&#124;", StringComparison.Ordinal).Replace("`", "&#96;", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
