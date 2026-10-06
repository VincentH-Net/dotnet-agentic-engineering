namespace Agentic.Check;

// A skill folder in a skills directory, with the origin gh stamped into its SKILL.md, if any.
sealed record InstalledSkill(string SkillsDirectory, string Folder, string? SourceRepo, string? SourcePath);

static class InstalledSkills
{
    internal static IReadOnlyList<InstalledSkill> Scan(IReadOnlyList<string> skillsDirectories)
    {
        List<InstalledSkill> installed = [];
        foreach (string directory in skillsDirectories.Where(Directory.Exists))
        {
            foreach (string folder in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
            {
                string skillFile = Path.Combine(folder, "SKILL.md");
                if (File.Exists(skillFile))
                {
                    installed.Add(new(
                        directory,
                        Path.GetFileName(folder),
                        SourceRepoOf(SkillInstaller.ReadFrontMatterValue(skillFile, "github-repo:")),
                        SkillInstaller.ReadFrontMatterValue(skillFile, "github-path:")));
                }
            }
        }

        return installed;
    }

    // gh stamps the repository as a URL; the manifest names it owner/repo.
    internal static string? SourceRepoOf(string? stamped)
    {
        if (string.IsNullOrWhiteSpace(stamped))
        {
            return null;
        }

        string value = stamped.Trim().TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        const string host = "github.com/";
        int index = value.IndexOf(host, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? value[(index + host.Length)..] : value;
    }
}
