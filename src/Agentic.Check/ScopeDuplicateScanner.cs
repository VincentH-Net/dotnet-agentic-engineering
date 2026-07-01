namespace Agentic.Check;

sealed record ScopeDuplicateScanResult
{
    public ScopeDuplicateScanResult(IReadOnlyDictionary<string, IReadOnlyList<string>> locationsByKey)
        : this(
            locationsByKey,
            locationsByKey.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal))
    {
    }

    public ScopeDuplicateScanResult(
        IReadOnlyDictionary<string, IReadOnlyList<string>> locationsByKey,
        IReadOnlyDictionary<string, int> scopeCountsByKey)
    {
        LocationsByKey = locationsByKey;
        ScopeCountsByKey = scopeCountsByKey;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> LocationsByKey { get; }

    public IReadOnlyDictionary<string, int> ScopeCountsByKey { get; }

    public int ActionCount => LocationsByKey.Count;
}

static class ScopeDuplicateScanner
{
    static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "TestResults",
        "packages"
    };

    internal static Task<ScopeDuplicateScanResult> ScanAsync(
        IReadOnlyList<RecommendationSelectionItem> items,
        string targetDirectory,
        IReadOnlyList<string> skillsDirectories,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        string[] relativeSkillsDirectories = [.. skillsDirectories
            .Select(directory => SpectreReporter.FormatSkillsDirectory(fullTargetDirectory, directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        string[] candidateDirectories = [.. EnumerateCandidateDirectories(fullTargetDirectory)];
        Dictionary<string, HashSet<string>> locationsByKey = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> scopeKeysByKey = new(StringComparer.Ordinal);

        for (int index = 0; index < candidateDirectories.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanDirectory(items, fullTargetDirectory, candidateDirectories[index], relativeSkillsDirectories, locationsByKey, scopeKeysByKey);
            progress?.Invoke(index + 1, candidateDirectories.Length);
        }

        return Task.FromResult(new ScopeDuplicateScanResult(
            locationsByKey.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)[.. pair.Value.Order(StringComparer.OrdinalIgnoreCase)],
                StringComparer.Ordinal),
            scopeKeysByKey.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal)));
    }

    static void ScanDirectory(
        IReadOnlyList<RecommendationSelectionItem> items,
        string targetDirectory,
        string candidateDirectory,
        IReadOnlyList<string> relativeSkillsDirectories,
        Dictionary<string, HashSet<string>> locationsByKey,
        Dictionary<string, HashSet<string>> scopeKeysByKey)
    {
        string agentsFile = Path.Combine(candidateDirectory, "AGENTS.md");
        string? agentsContent = File.Exists(agentsFile) ? File.ReadAllText(agentsFile) : null;
        foreach (var item in items)
        {
            if (item.Directive is not null)
            {
                if (agentsContent?.Contains($"dotnet-agentic-engineering:{item.Directive.Name}:", StringComparison.Ordinal) == true)
                {
                    AddDuplicate(locationsByKey, scopeKeysByKey, item.Key, targetDirectory, candidateDirectory, agentsFile);
                }

                continue;
            }

            if (item.Skill is null)
            {
                continue;
            }

            foreach (string relativeSkillsDirectory in relativeSkillsDirectories)
            {
                string skillFile = Path.Combine(candidateDirectory, relativeSkillsDirectory, item.Skill.LocalFolder, "SKILL.md");
                if (File.Exists(skillFile))
                {
                    AddDuplicate(locationsByKey, scopeKeysByKey, item.Key, targetDirectory, candidateDirectory, skillFile);
                }
            }
        }
    }

    static void AddDuplicate(
        Dictionary<string, HashSet<string>> locationsByKey,
        Dictionary<string, HashSet<string>> scopeKeysByKey,
        string key,
        string targetDirectory,
        string scopeDirectory,
        string location)
    {
        if (!locationsByKey.TryGetValue(key, out var locations))
        {
            locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            locationsByKey[key] = locations;
        }

        _ = locations.Add(Path.GetRelativePath(targetDirectory, location));

        if (!scopeKeysByKey.TryGetValue(key, out var scopeKeys))
        {
            scopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            scopeKeysByKey[key] = scopeKeys;
        }

        _ = scopeKeys.Add(Path.GetRelativePath(targetDirectory, scopeDirectory));
    }

    static IEnumerable<string> EnumerateCandidateDirectories(string targetDirectory)
    {
        foreach (string ancestor in EnumerateAncestorDirectories(targetDirectory))
        {
            yield return ancestor;
        }

        foreach (string descendant in EnumerateDescendantDirectories(targetDirectory))
        {
            yield return descendant;
        }
    }

    static IEnumerable<string> EnumerateAncestorDirectories(string targetDirectory)
    {
        string? boundary = FindGitBoundary(targetDirectory);
        string? current = Directory.GetParent(targetDirectory)?.FullName;
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            if (string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            current = Directory.GetParent(current)?.FullName;
        }
    }

    static string? FindGitBoundary(string targetDirectory)
    {
        string? current = targetDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    static IEnumerable<string> EnumerateDescendantDirectories(string targetDirectory)
    {
        foreach (string child in Directory.EnumerateDirectories(targetDirectory))
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith('.') || IgnoredDirectoryNames.Contains(name))
            {
                continue;
            }

            yield return child;
            foreach (string descendant in EnumerateDescendantDirectories(child))
            {
                yield return descendant;
            }
        }
    }
}
