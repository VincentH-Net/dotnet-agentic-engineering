namespace Agentic.Check;

// The rows a check offers for installed skills its manifest does not: obsolete ones to remove, and the
// choice to make when another repository now offers a skill under an installed name.
static class SkillRemovalPlanner
{
    // Removal rows for the installed skills, one per repository and folder across the skills directories.
    // A stamped skill the channel no longer offers is selected; the other rows wait for the user.
    internal static IReadOnlyList<SkillManifestEntry> PlanRemovals(
        IReadOnlyList<InstalledSkill> installed,
        IReadOnlyList<SkillManifestEntry> recommended,
        IReadOnlySet<SkillIdentity> obsolete)
    {
        var offeredByName = OfferedByName(recommended);
        Dictionary<string, SkillManifestEntry> rows = new(StringComparer.Ordinal);
        foreach (var skill in installed)
        {
            SkillManifestEntry? row = null;
            if (skill.SourceRepo is { } sourceRepo)
            {
                if (obsolete.Contains(new(sourceRepo, skill.Folder)))
                {
                    row = Removal(sourceRepo, skill.Folder, selected: true, $"No longer offered by {sourceRepo}.");
                }
                else if (offeredByName.TryGetValue(skill.Folder, out var offered) && !offered.SourceRepo.Equals(sourceRepo, StringComparison.OrdinalIgnoreCase))
                {
                    row = Removal(sourceRepo, skill.Folder, selected: false,
                        $"Installed from {sourceRepo}; a skill with this name is now offered from {offered.SourceRepo}.",
                        "This may be the same skill moved, or an unrelated one. Select the install to replace it.");
                }
            }
            else if (!offeredByName.ContainsKey(skill.Folder)
                && obsolete.FirstOrDefault(identity => identity.Name.Equals(skill.Folder, StringComparison.OrdinalIgnoreCase)) is { SourceRepo.Length: > 0 } retired)
            {
                row = Removal(retired.SourceRepo, skill.Folder, selected: false,
                    $"No origin recorded; remove only if this is the {skill.Folder} that dna check installed.");
            }

            if (row is not null)
            {
                _ = rows.TryAdd(row.Key, row);
            }
        }

        return [.. rows.Values];
    }

    // The offered skills whose name an installed skill from another repository holds. Such an install is
    // not selected, and it depends on the removal of the other skill, so selecting it brings that along.
    internal static IReadOnlyList<SkillManifestEntry> WithReplacements(
        IReadOnlyList<SkillManifestEntry> actions,
        IReadOnlyList<SkillManifestEntry> recommended,
        IReadOnlyList<InstalledSkill> installed)
    {
        List<SkillManifestEntry> result = [.. actions];
        foreach (var skill in recommended)
        {
            string? other = installed
                .Select(candidate => candidate.SourceRepo)
                .FirstOrDefault(sourceRepo => sourceRepo is not null && !sourceRepo.Equals(skill.SourceRepo, StringComparison.OrdinalIgnoreCase)
                    && installed.Any(candidate => candidate.SourceRepo == sourceRepo && candidate.Folder.Equals(skill.LocalFolder, StringComparison.OrdinalIgnoreCase)));
            if (other is null)
            {
                continue;
            }

            var replacement = skill with
            {
                RecommendationAction = "install",
                ForceInstall = true,
                SelectedByDefault = false,
                Dependencies = [.. skill.Dependencies, new SkillDependency(other, skill.LocalFolder)],
                Notes = [$"Replaces {skill.LocalFolder} from {other}, which is installed now; select to remove that one and install this."]
            };
            int index = result.FindIndex(action => action.Key == skill.Key);
            if (index >= 0)
            {
                result[index] = replacement;
            }
            else
            {
                result.Add(replacement);
            }
        }

        return result;
    }

    // Installed skills from the right repository at a path the manifest no longer names, so gh skill update
    // would look in the wrong place. A name-only entry has no path to compare.
    internal static IReadOnlyList<SkillManifestEntry> FindMoved(
        IReadOnlyList<InstalledSkill> installed,
        IReadOnlyList<SkillManifestEntry> recommended)
        => [.. recommended.Where(skill => skill.InstallArg.Contains('/', StringComparison.Ordinal)
            && installed.Any(candidate => candidate.Folder.Equals(skill.LocalFolder, StringComparison.OrdinalIgnoreCase)
                && candidate.SourceRepo is { } sourceRepo && sourceRepo.Equals(skill.SourceRepo, StringComparison.OrdinalIgnoreCase)
                && candidate.SourcePath is { } path && !path.Trim('/').Equals(skill.InstallArg.Trim('/'), StringComparison.OrdinalIgnoreCase)))];

    internal static bool IsRemoval(SkillManifestEntry skill)
        => skill.RecommendationAction == SkillInstaller.RemoveAction;

    static Dictionary<string, SkillManifestEntry> OfferedByName(IReadOnlyList<SkillManifestEntry> recommended)
    {
        Dictionary<string, SkillManifestEntry> offered = new(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in recommended.Where(skill => skill is { IsCompanion: false, IsDna: false, IsCodexRules: false, IsReadmeBadge: false }))
        {
            _ = offered.TryAdd(skill.LocalFolder, skill);
        }

        return offered;
    }

    static SkillManifestEntry Removal(string sourceRepo, string folder, bool selected, params string[] notes)
        => new(sourceRepo, folder, folder, string.Empty, [], recommendationAction: SkillInstaller.RemoveAction)
        {
            Notes = notes,
            SelectedByDefault = selected
        };
}
