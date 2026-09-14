using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class PackageFixtureSupportTests
{
    [Fact]
    public void FreshTriggersAreIndependentAndCoverDeclaredGates()
    {
        foreach (string path in Directory.GetDirectories(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions")))
            ValidateDefinition(Path.GetFileName(path));
    }

    static void ValidateDefinition(string name)
    {
        string directory = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions", name);
        var definition = FixtureFiles.ReadJson<FixtureDefinition>(Path.Combine(directory, "definition.json"));
        using FixtureWorkspace workspace = new();
        string trigger = workspace.Target;
        FixtureFiles.MaterializeTrigger(directory, trigger);
        var inventory = FixtureFiles.Inventory(trigger);
        Assert.DoesNotContain(inventory.Keys, path => path is "AGENTS.md" or "CLAUDE.md" || path.Contains("skills/", StringComparison.Ordinal) || path.Contains("dotnet-tools", StringComparison.Ordinal));
        var detected = StackDetector.Detect(trigger);
        Assert.Equal(definition.Technologies.Order(StringComparer.Ordinal), detected.Technologies.Order(StringComparer.Ordinal));
        var actual = detected.InstallGates.SelectMany(report => report.Values).GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        Assert.Equal(definition.Gates.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, values) in definition.Gates)
            Assert.Equal(values.Order(StringComparer.Ordinal), actual[key]);
        if (name == "uno-conflicting-gates")
            Assert.Equal(3, detected.Warnings.Count);
    }

    [Fact]
    public void FourteenDefinitionsAndIndependentPreviewScenariosRemainCovered()
    {
        Assert.Equal(14, Directory.GetDirectories(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions")).Length);
        object[][] rows = [.. PackageFixtureTests.BuildScenarios(null)];
        Assert.Equal(14, rows.Count(row => (string)row[1] == "fresh"));
        foreach (string scenario in new[] { "preview-preview", "preview-stable", "preview-declined", "candidate-preview-stable" })
            Assert.Contains(rows, row => (string)row[0] == "preview-web-cli" && (string)row[1] == scenario && !(bool)row[2]);
    }

    [Fact]
    public void InstalledSnapshotsMatchFrozenInventoriesAndTriggerHashes()
    {
        string root = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baselines");
        Assert.True(Directory.Exists(root), "Prepare at least one baseline collection.");
        string[] baselines = Directory.GetDirectories(root);
        Assert.NotEmpty(baselines);
        foreach (string baseline in baselines)
        {
            var collection = FixtureFiles.ReadJson<BaselineCollection>(Path.Combine(baseline, "collection.json"));
            Assert.NotEmpty(collection.Completed);
            Assert.Contains(collection.Definition.InstallerVersion, collection.Id, StringComparison.Ordinal);
            foreach (string name in collection.Completed)
            {
                var metadata = FixtureFiles.ReadJson<FixtureCapture>(Path.Combine(baseline, name, "metadata.json"));
                using FixtureWorkspace workspace = new();
                string snapshot = workspace.Target;
                string archive = Path.Combine(baseline, name, "snapshot.zip");
                Assert.Equal(metadata.SnapshotSha256, FixtureFiles.Hash(archive));
                FixtureFiles.ExtractSnapshot(archive, snapshot);
                Assert.True(FixtureFiles.EqualInventory(metadata.Files, FixtureFiles.Inventory(snapshot)));
                foreach (var (path, hash) in metadata.TriggerHashes)
                    Assert.Equal(hash, FixtureFiles.Hash(Path.Combine(snapshot, path)));
                Assert.DoesNotContain(metadata.Files.Keys, path => path.Split('/').Any(part => part is ".git" or "bin" or "obj") || path.EndsWith(".nupkg", StringComparison.Ordinal));
                if (!collection.Definition.CompanionExpected)
                    Assert.Null(metadata.Companion);
            }
            Assert.Empty(collection.Failed);
        }
    }

    [Fact]
    public void OraclePreservesBodiesAndAuthoredMetadataWhenIgnoringOnlyGhTracking()
    {
        const string original = "---\nname: example\nmetadata:\n  author: example\n---\n\nKeep  significant spaces.\n";
        const string injected = "---\nmetadata:\n  github-ref: main\n  github-repo: https://github.com/example/repo\n  github-path: skills/example\n  github-tree-sha: abc\n  author: example\nname: example\n---\nKeep  significant spaces.\n";
        var (left, body) = SourceOracle.ParseSkill(original);
        var (right, installedBody) = SourceOracle.ParseSkill(injected);
        SourceOracle.RemoveTracking(left);
        SourceOracle.RemoveTracking(right);
        Assert.True(left.Equals(right));
        Assert.Equal(body, installedBody);
        Assert.NotEqual(body, SourceOracle.ParseSkill(injected.Replace("Keep  ", "Keep ", StringComparison.Ordinal)).Body);
    }
}
