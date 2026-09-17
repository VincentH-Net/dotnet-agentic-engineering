using System.Text.Json;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class SourceOracleTests
{
    const string Repository = "example/skills";
    const string Commit = "0123456789abcdef0123456789abcdef01234567";
    const string SourcePath = "skills/example";
    const string SourceSkill = "---\nname: example\ndescription: Selected description\n---\nSelected source body.\n";
    const string InstalledSkill = "---\nname: example\ndescription: Selected description\nmetadata:\n"
        + "  github-repo: https://github.com/example/skills\n  github-ref: refs/tags/v1\n"
        + "  github-path: skills/example\n  github-tree-sha: selected-tree\n---\nSelected source body.\n";

    [Theory]
    [InlineData(".agents/skills")]
    [InlineData(".claude/skills")]
    public async Task MigrationVerifiesSourceAndReportsUnchangedRetainedAssets(string agentDirectory)
    {
        using FixtureWorkspace workspace = new();
        var source = await SeedAsync(workspace, agentDirectory).ConfigureAwait(true);
        var before = FixtureFiles.Inventory(workspace.Target);
        string localPath = agentDirectory + "/example";
        await File.WriteAllTextAsync(Path.Combine(workspace.Target, localPath, "SKILL.md"), InstalledSkill).ConfigureAwait(true);
        SortedDictionary<string, string> retained = new(StringComparer.Ordinal);
        using SourceOracle oracle = new(workspace.Process, workspace.Root);

        var origins = await oracle.VerifySkillsAsync(workspace.Target,
            new Dictionary<string, SourceSnapshot> { [Repository] = source }, previousFiles: before,
            reportRetainedFile: retained.Add).ConfigureAwait(true);

        var origin = Assert.Single(origins);
        Assert.Equal(localPath, origin.LocalPath);
        Assert.Equal(Commit, origin.Commit);
        Assert.Equal(["SKILL.md", "references/required.md"], origin.SourceFiles.Keys);
        var oldAsset = Assert.Single(retained);
        Assert.Equal(localPath + "/references/old.md", oldAsset.Key);
        Assert.Equal(before[oldAsset.Key], oldAsset.Value);

        // The identical result remains invalid for a fresh install or baseline preparation.
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => oracle.VerifySkillsAsync(workspace.Target,
            new Dictionary<string, SourceSnapshot> { [Repository] = source })).ConfigureAwait(true);
        Assert.Contains("Unexpected skill asset", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-asset", "Missing skill asset")]
    [InlineData("changed-source-asset", "Skill asset differs")]
    [InlineData("changed-retained-asset", "Pre-existing skill asset changed")]
    [InlineData("new-extra-asset", "Unexpected skill asset")]
    [InlineData("wrong-body", "Skill body differs")]
    [InlineData("wrong-authored-metadata", "Authored frontmatter differs")]
    [InlineData("wrong-tree", "Wrong source tree")]
    [InlineData("wrong-pin", "Wrong pin")]
    public async Task RetentionAllowanceDoesNotHideIncorrectDelivery(string mutation, string expectedError)
    {
        using FixtureWorkspace workspace = new();
        var source = await SeedAsync(workspace, ".agents/skills").ConfigureAwait(true);
        var before = FixtureFiles.Inventory(workspace.Target);
        string directory = Path.Combine(workspace.Target, ".agents/skills/example");
        string skill = InstalledSkill;
        switch (mutation)
        {
            case "missing-asset": File.Delete(Path.Combine(directory, "references/required.md")); break;
            case "changed-source-asset": await File.WriteAllTextAsync(Path.Combine(directory, "references/required.md"), "wrong").ConfigureAwait(true); break;
            case "changed-retained-asset": await File.WriteAllTextAsync(Path.Combine(directory, "references/old.md"), "changed").ConfigureAwait(true); break;
            case "new-extra-asset": await File.WriteAllTextAsync(Path.Combine(directory, "references/unexpected.md"), "new").ConfigureAwait(true); break;
            case "wrong-body": skill = skill.Replace("Selected source body.", "Wrong body.", StringComparison.Ordinal); break;
            case "wrong-authored-metadata": skill = skill.Replace("Selected description", "Wrong description", StringComparison.Ordinal); break;
            case "wrong-tree": skill = skill.Replace("selected-tree", "wrong-tree", StringComparison.Ordinal); break;
            case "wrong-pin": skill = skill.Replace("metadata:\n", "metadata:\n  github-pinned: 1111111111111111111111111111111111111111\n", StringComparison.Ordinal); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown delivery mutation.");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "SKILL.md"), skill).ConfigureAwait(true);
        using SourceOracle oracle = new(workspace.Process, workspace.Root);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => oracle.VerifySkillsAsync(workspace.Target,
            new Dictionary<string, SourceSnapshot> { [Repository] = source }, previousFiles: before)).ConfigureAwait(true);

        Assert.Contains(expectedError, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".claude/skills/example/references/old.md")]
    [InlineData(".agents/skills/another/references/old.md")]
    [InlineData(".agents/skills/example/references/renamed.md")]
    public void RetainedAssetMustHaveExistedAtTheSamePath(string previousPath)
    {
        Dictionary<string, string> source = new() { ["SKILL.md"] = "source-hash" };
        Dictionary<string, string> installed = new() { ["SKILL.md"] = "gh-metadata-hash", ["references/old.md"] = "old-hash" };
        Dictionary<string, string> before = new() { [previousPath] = "old-hash" };

        var exception = Assert.Throws<InvalidDataException>(() => SourceOracle.VerifyAssetInventory(
            ".agents/skills/example", source, installed, before));

        Assert.Contains("Unexpected skill asset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactInventoryPassesWithOrWithoutPreviouslyRemovedAssets()
    {
        Dictionary<string, string> source = new() { ["SKILL.md"] = "source-hash", ["references/required.md"] = "required-hash" };
        Dictionary<string, string> installed = new(source) { ["SKILL.md"] = "gh-metadata-hash" };
        Dictionary<string, string> before = new() { [".agents/skills/example/references/removed.md"] = "old-hash" };

        Assert.Empty(SourceOracle.VerifyAssetInventory(".agents/skills/example", source, installed));
        Assert.Empty(SourceOracle.VerifyAssetInventory(".agents/skills/example", source, installed, before));
    }

    static async Task<SourceSnapshot> SeedAsync(FixtureWorkspace workspace, string agentDirectory)
    {
        string sourceRoot = Path.Combine(workspace.Root, "source");
        string sourceFolder = Path.Combine(sourceRoot, SourcePath);
        _ = Directory.CreateDirectory(Path.Combine(sourceFolder, "references"));
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "SKILL.md"), SourceSkill).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "references/required.md"), "Required source asset.\n").ConfigureAwait(false);
        string[] files = [.. FixtureFiles.Inventory(sourceFolder).Keys];
        string hashes = await workspace.Process.SuccessAsync("git", ["hash-object", "--no-filters",
            .. files.Select(file => Path.Combine(sourceFolder, file))], workspace.Root).ConfigureAwait(false);
        string[] blobs = hashes.Split('\n');
        List<object> entries = [new { path = SourcePath, type = "tree", sha = "selected-tree" }];
        for (int index = 0; index < files.Length; index++)
            entries.Add(new { path = SourcePath + "/" + files[index], type = "blob", sha = blobs[index].TrimEnd('\r') });

        string installed = Path.Combine(workspace.Target, agentDirectory, "example");
        FixtureFiles.Copy(sourceFolder, installed);
        await File.WriteAllTextAsync(Path.Combine(installed, "references/old.md"), "Old preview asset.\n").ConfigureAwait(false);
        return new(Repository, "v1", Commit, sourceRoot, JsonSerializer.SerializeToElement(new { truncated = false, tree = entries }));
    }
}
