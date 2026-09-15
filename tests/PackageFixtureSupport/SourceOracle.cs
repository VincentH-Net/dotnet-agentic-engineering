using System.IO.Compression;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Agentic.PackageFixtures;

sealed record SourceSnapshot(string Repository, string Reference, string Commit, string Directory, JsonElement Tree);
sealed record SkillOrigin(string LocalPath, string Repository, string Reference, string Commit, string Tree, string SourcePath, string? Pin, SortedDictionary<string, string> SourceFiles);

sealed class SourceOracle(RealProcess process, string workingDirectory) : IDisposable
{
    internal const string OwnRepository = "VincentH-Net/dotnet-agentic-engineering";
    readonly List<string> extractedDirectories = [];
    readonly Dictionary<string, SourceSnapshot> snapshots = new(StringComparer.Ordinal);

    public void Dispose()
    {
        foreach (string directory in extractedDirectories)
            Directory.Delete(directory, true);
    }

    internal async Task<JsonElement> ApiAsync(string endpoint)
    {
        string json = await process.SuccessAsync("gh", ["api", "--hostname", "github.com", endpoint], workingDirectory).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal async Task<SourceSnapshot> SnapshotAsync(string repository, string reference)
    {
        string key = repository + "@" + reference;
        if (snapshots.TryGetValue(key, out var existing))
            return existing;
        var commit = await ApiAsync($"repos/{repository}/commits/{Uri.EscapeDataString(reference)}").ConfigureAwait(false);
        string sha = commit.GetProperty("sha").GetString()!;
        if (snapshots.TryGetValue(repository + "@" + sha, out var immutable))
        {
            var alias = immutable with { Reference = reference };
            snapshots.Add(key, alias);
            return alias;
        }
        string zipPath = Path.Combine(FixtureFiles.Cache, "source-archives", repository, sha + ".zip");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (!File.Exists(zipPath))
        {
            using HttpClient client = new();
            byte[] bytes = await client.GetByteArrayAsync(new Uri($"https://codeload.github.com/{repository}/zip/{sha}")).ConfigureAwait(false);
            await File.WriteAllBytesAsync(zipPath, bytes).ConfigureAwait(false);
            await File.WriteAllTextAsync(zipPath + ".sha256", FixtureFiles.Hash(zipPath)).ConfigureAwait(false);
        }
        FixtureFiles.Require(File.Exists(zipPath + ".sha256") && FixtureFiles.Hash(zipPath) == (await File.ReadAllTextAsync(zipPath + ".sha256").ConfigureAwait(false)), "Source archive cache checksum differs.");
        string extracted = Directory.CreateTempSubdirectory("agentic-source-").FullName;
        extractedDirectories.Add(extracted);
        await ZipFile.ExtractToDirectoryAsync(zipPath, extracted).ConfigureAwait(false);
        string directory = Directory.GetDirectories(extracted).Single();
        var tree = await ApiAsync($"repos/{repository}/git/trees/{sha}?recursive=1").ConfigureAwait(false);
        FixtureFiles.Require(!tree.GetProperty("truncated").GetBoolean(), $"Truncated source tree: {key}");
        SourceSnapshot snapshot = new(repository, reference, sha, directory, tree);
        snapshots[key] = snapshot;
        _ = snapshots.TryAdd(repository + "@" + sha, snapshot);
        return snapshot;
    }

    internal async Task<SourceSnapshot> SelectedAsync(string repository, bool preview, string? ownSha = null)
    {
        if (repository == OwnRepository && ownSha is not null)
            return await SnapshotAsync(repository, ownSha).ConfigureAwait(false);
        string reference;
        if (!preview)
        {
            for (int page = 1; ; page++)
            {
                var releases = await ApiAsync($"repos/{repository}/releases?per_page=100&page={page}").ConfigureAwait(false);
                if (releases.EnumerateArray().Any(release => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean()))
                {
                    var latest = await ApiAsync($"repos/{repository}/releases/latest").ConfigureAwait(false);
                    reference = latest.GetProperty("tag_name").GetString()!;
                    return await SnapshotAsync(repository, reference).ConfigureAwait(false);
                }
                if (releases.GetArrayLength() < 100)
                    break;
            }
        }
        var metadata = await ApiAsync($"repos/{repository}").ConfigureAwait(false);
        reference = metadata.GetProperty("default_branch").GetString()!;
        return await SnapshotAsync(repository, reference).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SkillOrigin>> VerifySkillsAsync(string target, IReadOnlyDictionary<string, SourceSnapshot>? selected = null, IReadOnlySet<string>? allowedFolders = null)
    {
        List<SkillOrigin> origins = [];
        foreach (string agent in new[] { ".agents/skills", ".claude/skills" })
        {
            string skills = Path.Combine(target, agent);
            if (!Directory.Exists(skills))
                continue;
            foreach (string folder in Directory.GetDirectories(skills))
            {
                if (allowedFolders is not null && !allowedFolders.Contains(Path.GetFileName(folder)))
                    continue;
                string installedFile = Path.Combine(folder, "SKILL.md");
                var (yaml, body) = ParseSkill(await File.ReadAllTextAsync(installedFile).ConfigureAwait(false));
                var metadata = (YamlMappingNode)yaml.Children[new YamlScalarNode("metadata")];
                string Value(string name) => ((YamlScalarNode)metadata.Children[new YamlScalarNode(name)]).Value!;
                var repoUri = new Uri(Value("github-repo"));
                FixtureFiles.Require(repoUri.Host == "github.com", $"Unexpected source host: {repoUri.Host}");
                string repository = repoUri.AbsolutePath.Trim('/');
                string reference = Value("github-ref");
                string tree = Value("github-tree-sha");
                string sourcePath = Value("github-path");
                string? pin = metadata.Children.TryGetValue(new YamlScalarNode("github-pinned"), out var pinned) ? ((YamlScalarNode)pinned).Value : null;
                var snapshot = selected is not null && selected.TryGetValue(repository, out var expected)
                    ? expected : await SnapshotAsync(repository, reference).ConfigureAwait(false);
                string expectedTree = snapshot.Tree.GetProperty("tree").EnumerateArray()
                    .Single(item => item.GetProperty("path").GetString() == sourcePath && item.GetProperty("type").GetString() == "tree")
                    .GetProperty("sha").GetString()!;
                FixtureFiles.Require(tree == expectedTree, $"Wrong source tree for {folder}: {tree} != {expectedTree} ({snapshot.Repository}@{snapshot.Commit})");
                if (pin is not null && pin.Length == 40)
                    FixtureFiles.Require(pin == snapshot.Commit, $"Wrong pin for {folder}: {pin} != {snapshot.Commit}");
                string sourceFolder = Path.GetFullPath(Path.Combine(snapshot.Directory, sourcePath));
                FixtureFiles.Require(sourceFolder.StartsWith(snapshot.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Invalid source skill path.");
                var sourceFiles = FixtureFiles.Inventory(sourceFolder);
                string[] sourcePaths = [.. sourceFiles.Keys.Select(file => Path.Combine(sourceFolder, file))];
                string hashes = await process.SuccessAsync("git", ["hash-object", "--no-filters", .. sourcePaths], workingDirectory).ConfigureAwait(false);
                string[] blobHashes = hashes.Split('\n');
                for (int index = 0; index < sourcePaths.Length; index++)
                {
                    string path = sourcePath + "/" + sourceFiles.Keys.ElementAt(index);
                    string expectedBlob = snapshot.Tree.GetProperty("tree").EnumerateArray().Single(item => item.GetProperty("path").GetString() == path).GetProperty("sha").GetString()!;
                    FixtureFiles.Require(blobHashes[index].TrimEnd('\r') == expectedBlob, $"Independent source cache differs from GitHub blob: {snapshot.Repository}@{snapshot.Commit}/{path}");
                }
                var installedFiles = FixtureFiles.Inventory(folder);
                FixtureFiles.Require(sourceFiles.Keys.SequenceEqual(installedFiles.Keys), $"Skill asset inventory differs: {folder}");
                foreach (var (file, hash) in sourceFiles)
                {
                    if (file == "SKILL.md")
                        continue;
                    FixtureFiles.Require(hash == installedFiles[file], $"Skill asset differs: {folder}/{file}");
                }
                var (sourceYaml, sourceBody) = ParseSkill(await File.ReadAllTextAsync(Path.Combine(sourceFolder, "SKILL.md")).ConfigureAwait(false));
                FixtureFiles.Require(body == sourceBody, $"Skill body differs: {folder}");
                RemoveTracking(yaml);
                RemoveTracking(sourceYaml);
                FixtureFiles.Require(yaml.Equals(sourceYaml), $"Authored frontmatter differs: {folder}");
                origins.Add(new(Path.GetRelativePath(target, folder).Replace('\\', '/'), repository, reference, snapshot.Commit, tree, sourcePath, pin, sourceFiles));
            }
        }
        return origins;
    }

    // gh 2.100 parses leading body newlines and ensures one final newline; other body bytes stay significant.
    internal static (YamlMappingNode Yaml, string Body) ParseSkill(string text)
    {
        text = text.TrimStart('\r', '\n');
        FixtureFiles.Require(text.StartsWith("---", StringComparison.Ordinal), "Missing skill frontmatter.");
        int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        FixtureFiles.Require(end >= 0, "Unclosed skill frontmatter.");
        YamlStream stream = [];
        stream.Load(new StringReader(text[3..end]));
        string body = text[(end + 4)..].TrimStart('\r', '\n');
        if (body.Length > 0 && !body.EndsWith('\n'))
            body += "\n";
        return ((YamlMappingNode)stream.Documents.Single().RootNode, body);
    }

    internal static void RemoveTracking(YamlMappingNode yaml)
    {
        YamlScalarNode metadataKey = new("metadata");
        if (!yaml.Children.TryGetValue(metadataKey, out var node) || node is not YamlMappingNode metadata)
            return;
        foreach (string key in new[] { "github-repo", "github-ref", "github-tree-sha", "github-path", "github-pinned", "github-owner", "github-sha" })
            _ = metadata.Children.Remove(new YamlScalarNode(key));
        if (metadata.Children.Count == 0)
            _ = yaml.Children.Remove(metadataKey);
    }
}
