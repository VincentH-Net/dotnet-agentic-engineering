using System.Text.Json;

namespace Agentic.Check.LiveTests;

public sealed class SkillDiscoveryTests
{
    const string Repo = "example/skills";

    [Fact]
    public void SeparatesIncludedExcludedAndNewAcrossStableAndPreview()
    {
        var included = File("skills/included/SKILL.md");
        var excluded = File("skills/excluded/SKILL.md");
        var released = File("skills/new-stable/SKILL.md");
        var candidate = File("new-plugin/skills/new-preview/SKILL.md");
        var previewIncluded = File("skills/preview-included/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [included, excluded], [included, excluded, released],
            [included, excluded, released, candidate, previewIncluded], [Entry("included")], [Entry("included"), Entry("preview-included")]);

        Assert.Equal(SkillDiscoveryKind.Included, Item(items, included.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.ExcludedAtBaseline, Item(items, excluded.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.Included, Item(items, previewIncluded.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.NewCandidate, Item(items, candidate.Path).Kind);
        Assert.Equal("preview only", Item(items, candidate.Path).Availability);
        Assert.Equal("stable + preview", Item(items, released.Path).Availability);
        Assert.Equal(2, items.Count(SkillDiscovery.NeedsReview));
    }

    [Fact]
    public void SameNamesInOtherReposAndFoldersDoNotBecomeIncluded()
    {
        var selected = File("plugins/a/skills/check/SKILL.md");
        var other = File("plugins/b/skills/check/SKILL.md");
        var foreign = File("skills/foreign/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [], [selected, other, foreign], [selected, other, foreign],
            [Entry("plugins/a/skills/check"), Entry("foreign") with { SourceRepo = "other/skills" }], []);

        Assert.Equal(SkillDiscoveryKind.Included, Item(items, selected.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.NewCandidate, Item(items, other.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.NewCandidate, Item(items, foreign.Path).Kind);
    }

    [Fact]
    public void ExplicitPathsAreNotMatchedByFolderNameAfterMove()
    {
        var old = File("plugins/old/skills/check/SKILL.md") with { Name = "actual-name" };
        var moved = File("plugins/new/skills/check/SKILL.md") with { Name = "actual-name" };
        var items = SkillDiscovery.Compare(Repo, [old], [old], [moved], [Entry("plugins/old/skills/check")], [Entry("plugins/old/skills/check")]);

        var move = Item(items, moved.Path);
        Assert.Equal(SkillDiscoveryKind.PossibleMove, move.Kind);
        Assert.Contains(old.Path, move.Detail, StringComparison.Ordinal);
        var missing = Assert.Single(items, item => item.Kind == SkillDiscoveryKind.MissingUpstream);
        Assert.Equal("preview", missing.Availability);
    }

    [Fact]
    public void SharedNamesWithoutRemovedPathsAreNotReportedAsMoves()
    {
        var old = File("a/check/SKILL.md");
        var added = File("b/check/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [old], [old], [old, added], [], []);
        Assert.Equal(SkillDiscoveryKind.NewCandidate, Item(items, added.Path).Kind);
    }

    [Fact]
    public void PreviewOnlyManifestEntryDoesNotRequireStableAvailability()
    {
        var skill = File("skills/new/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [], [], [skill], [], [Entry("new")]);
        Assert.Equal(SkillDiscoveryKind.Included, Assert.Single(items).Kind);
    }

    [Fact]
    public void MissingStableSkillStillFailsWhenPresentInPreview()
    {
        var skill = File("skills/new/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [], [], [skill], [Entry("new")], [Entry("new")]);
        Assert.Equal("stable", Assert.Single(items, item => item.Kind == SkillDiscoveryKind.MissingUpstream).Availability);
    }

    [Fact]
    public void AdvancingBaselineExcludesRejectedCandidates()
    {
        var skill = File("skills/rejected/SKILL.md");
        var before = SkillDiscovery.Compare(Repo, [], [skill], [skill], [], []);
        var after = SkillDiscovery.Compare(Repo, [skill], [skill], [skill], [], []);
        Assert.True(SkillDiscovery.NeedsReview(Assert.Single(before)));
        Assert.False(SkillDiscovery.NeedsReview(Assert.Single(after)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HistoricalStablePathsAreNotMistakenForNewAdditions(bool stablePredatesReview)
    {
        var old = File("old/skills/check/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [], [old], [], [], [], stablePredatesReview);
        if (stablePredatesReview)
        {
            Assert.Empty(items);
        }
        else
        {
            Assert.Equal(SkillDiscoveryKind.NewCandidate, Assert.Single(items).Kind);
        }
    }

    [Fact]
    public void HistoricalFilterDoesNotHideBaselineExclusionsDefaultBranchCandidatesOrMissingManifestEntries()
    {
        var excluded = File("skills/excluded/SKILL.md");
        var current = File("skills/current/SKILL.md");
        var historical = File("skills/historical/SKILL.md");
        var items = SkillDiscovery.Compare(Repo, [excluded], [excluded, historical], [current], [], [Entry("historical")], stablePredatesReview: true);
        Assert.Equal(SkillDiscoveryKind.ExcludedAtBaseline, Item(items, excluded.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.NewCandidate, Item(items, current.Path).Kind);
        Assert.Equal("preview", Assert.Single(items, item => item.Kind == SkillDiscoveryKind.MissingUpstream).Availability);
        Assert.DoesNotContain(items, item => item.Skill.Path == historical.Path && item.Kind != SkillDiscoveryKind.MissingUpstream);
    }

    [Fact]
    public void MatchingSupportsExactSkillFileAndRejectsCaseMismatch()
    {
        var file = File("skills/Check/SKILL.md");
        _ = Assert.Single(SkillDiscovery.Match(Entry(file.Path), [file]));
        Assert.Empty(SkillDiscovery.Match(Entry("skills/check"), [file]));
    }

    [Fact]
    public void NameOnlyEntryUsesPluginToResolveDuplicateFolders()
    {
        var a = File("plugins/a/skills/check/SKILL.md");
        var b = File("plugins/b/skills/check/SKILL.md");
        Assert.Equal(a, Assert.Single(SkillDiscovery.Match(Entry("check") with { Plugin = "a" }, [a, b])));
        Assert.Equal(2, SkillDiscovery.Match(Entry("check"), [a, b]).Count);
    }

    [Fact]
    public void NameOnlyEntriesMatchRootFolders()
    {
        var file = File("check/SKILL.md");
        Assert.Equal(file, Assert.Single(SkillDiscovery.Match(Entry("check"), [file])));
    }

    [Fact]
    public void HiddenCopiesDoNotMakeVisibleNameOnlyEntriesAmbiguous()
    {
        var visible = File("skills/check/SKILL.md");
        var hidden = File(".agents/skills/check/SKILL.md");
        Assert.Equal(visible, Assert.Single(SkillDiscovery.Match(Entry("check"), [visible, hidden])));
    }

    [Fact]
    public void NameOnlyEntriesPreferPluginsOverLegacyLayoutsWithDifferentDisplayLabels()
    {
        var plugin = File("plugins/actual-plugin/skills/check/SKILL.md");
        var legacy = File("skills/check/SKILL.md");
        Assert.Equal(plugin, Assert.Single(SkillDiscovery.Match(Entry("check") with { Plugin = "display-label" }, [plugin, legacy])));
    }

    [Fact]
    public void RepositoriesWithoutStableReleasesFallBackToDefaultBranch()
    {
        using var empty = JsonDocument.Parse("[]");
        using var prereleases = JsonDocument.Parse("""[{"draft":false,"prerelease":true,"tag_name":"nightly"},{"draft":true,"prerelease":false,"tag_name":"draft"}]""");
        using var stable = JsonDocument.Parse("""[{"draft":false,"prerelease":false,"tag_name":"v1"}]""");
        Assert.Null(MaintenanceGh.FindStableTag(empty.RootElement));
        Assert.Null(MaintenanceGh.FindStableTag(prereleases.RootElement));
        Assert.Equal("v1", MaintenanceGh.FindStableTag(stable.RootElement));
    }

    [Fact]
    public void TruncatedTreesCannotProducePassingInventories()
    {
        using var document = JsonDocument.Parse("""{"truncated":true,"tree":[]}""");
        _ = Assert.Throws<IOException>(() => MaintenanceGh.ReadTree(document.RootElement));
    }

    [Fact]
    public async Task EmptyReleaseResultsAreCachedAndReturnTheResolvedDefaultBranch()
    {
        List<string> endpoints = [];
        MaintenanceGh gh = new((arguments, _) =>
        {
            endpoints.Add(arguments[^1]);
            return Task.FromResult(new CommandResult(0, "[]", ""));
        });
        SkillSourceSnapshot branch = new("trunk", "head", DateTimeOffset.UnixEpoch);
        Assert.Same(branch, await gh.StableAsync(Repo, branch, CancellationToken.None));
        Assert.Same(branch, await gh.StableAsync(Repo, branch, CancellationToken.None));
        Assert.Equal($"repos/{Repo}/releases?per_page=100&page=1", Assert.Single(endpoints));
    }

    [Fact]
    public async Task ApiErrorsAreNotTreatedAsEmptyInventoriesOrCachedSuccesses()
    {
        int calls = 0;
        MaintenanceGh gh = new((_, _) =>
        {
            calls++;
            return Task.FromResult(new CommandResult(1, "", "HTTP 403"));
        });
        SkillSourceSnapshot branch = new("trunk", "head", DateTimeOffset.UnixEpoch);
        _ = await Assert.ThrowsAsync<IOException>(() => gh.StableAsync(Repo, branch, CancellationToken.None));
        _ = await Assert.ThrowsAsync<IOException>(() => gh.StableAsync(Repo, branch, CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DefaultBranchIsResolvedDynamicallyAndFrozenToCommitSha()
    {
        List<string> endpoints = [];
        MaintenanceGh gh = new((arguments, _) =>
        {
            string endpoint = arguments[^1];
            endpoints.Add(endpoint);
            string json = endpoint == $"repos/{Repo}"
                ? """{"default_branch":"release/next"}"""
                : """{"sha":"abc123","commit":{"committer":{"date":"2026-06-01T10:00:00Z"}}}""";
            return Task.FromResult(new CommandResult(0, json, ""));
        });
        var branch = await gh.DefaultBranchAsync(Repo, CancellationToken.None);
        Assert.Equal("release/next", branch.Ref);
        Assert.Equal("abc123", branch.CommitSha);
        Assert.Contains($"repos/{Repo}/commits/release%2Fnext", endpoints);
    }

    [Fact]
    public void TreeDiscoveryIncludesHiddenAndUnselectedFoldersButNotDirectoriesOrOtherFiles()
    {
        using var document = JsonDocument.Parse("""
            {"truncated":false,"tree":[
              {"path":".agents/skills/local/SKILL.md","sha":"a","type":"blob"},
              {"path":"new-plugin/skills/new/SKILL.md","sha":"b","type":"blob"},
              {"path":"SKILL.md","sha":"c","type":"blob"},
              {"path":"other/SKILL.md","sha":"d","type":"tree"},
              {"path":"other/README.md","sha":"e","type":"blob"}]}
            """);
        var files = MaintenanceGh.ReadTree(document.RootElement);
        Assert.Equal(3, files.Count);
        Assert.Contains(files, file => file.Path.StartsWith(".agents/", StringComparison.Ordinal));
        Assert.Contains(files, file => file.Path == "SKILL.md");
    }

    [Fact]
    public void FrontmatterUsesYamlNamesAndFoldedDescriptions()
    {
        var file = MaintenanceGh.ReadFrontmatter(File("skills/folder/SKILL.md"), """
            ---
            name: 'different-name'
            description: >-
              First line: with colon.
              Second line.
            ---
            Body
            """);
        Assert.Equal("different-name", file.Name);
        Assert.Equal("First line: with colon. Second line.", file.Description);
    }

    [Theory]
    [InlineData("no frontmatter")]
    [InlineData("---\nname: test\n")]
    [InlineData("---\nname: test\n---\n")]
    [InlineData("---\nname: [\n---\n")]
    public void InvalidFrontmatterIsReportedAsAnError(string content)
        => Assert.Throws<IOException>(() => MaintenanceGh.ReadFrontmatter(File("skills/test/SKILL.md"), content));

    [Fact]
    public void PreviewVersionIsAppendedToSkillArgumentNotRepository()
    {
        var entry = Entry("plugins/test/skills/run");
        Assert.Equal(entry.InstallArg, ManifestGhSkillTests.PreviewArgument(entry, ""));
        Assert.Equal("plugins/test/skills/run@abc123", ManifestGhSkillTests.PreviewArgument(entry, "abc123"));
        Assert.Equal("run@abc123", ManifestGhSkillTests.PreviewArgument(Entry("run@old"), "abc123"));
        Assert.Equal(Repo, entry.SourceRepo);
    }

    [Fact]
    public void ReportLinksToSourceInsteadOfRepeatingDescriptionsAndMetadata()
    {
        var time = DateTimeOffset.UnixEpoch;
        SkillSourceReview review = new(Repo, "baseline", time, time);
        SkillSourceSnapshot stable = new("v1", "stable-sha", time);
        SkillSourceSnapshot preview = new("trunk", "preview-sha", time);
        var skill = File("plugins/new/skills/test/SKILL.md") with { Description = "A | B\n<script>" };
        string text = SkillDiscoveryReport.Render([new(review, stable, preview, [new(SkillDiscoveryKind.NewCandidate, skill, "preview only")])], [], time);
        Assert.Contains($"https://github.com/{Repo}/blob/preview-sha/{skill.Path}", text, StringComparison.Ordinal);
        Assert.Contains("skill-discovery-sources.json", text, StringComparison.Ordinal);
        Assert.Contains("| plugins/new/skills |", text, StringComparison.Ordinal);
        Assert.DoesNotContain("A | B", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stable-sha", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryManifestSourceHasExactlyOneValidReviewBaseline()
    {
        var repos = StaticSkillManifest.All.Concat(StaticSkillManifest.Preview).Select(entry => entry.SourceRepo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(repos.Count, StaticSkillManifest.SourceReviews.Count);
        foreach (string source in repos)
        {
            var review = Assert.Single(StaticSkillManifest.SourceReviews, review => review.SourceRepo.Equals(source, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(40, review.CommitSha.Length);
            Assert.All(review.CommitSha, character => Assert.True(Uri.IsHexDigit(character)));
            Assert.True(review.CommittedAt <= review.ReviewedAt);
        }
    }

    [Fact]
    public void IdenticalCopiesAreHiddenFromReportButRetainedInClassification()
    {
        var included = IdentifiedFile("plugins/a/skills/canonical/SKILL.md", "same-name", "tree");
        var copy = IdentifiedFile("skills/other-folder/SKILL.md", "same-name", "tree");
        var items = SkillDiscovery.Compare(Repo, [included, copy], [included, copy], [included, copy],
            [Entry("plugins/a/skills/canonical")], [Entry("plugins/a/skills/canonical")]);

        Assert.Equal(SkillDiscoveryKind.IdenticalIncludedCopy, Item(items, copy.Path).Kind);
        Assert.DoesNotContain(items, SkillDiscovery.NeedsReview);
        string report = RenderReport(items);
        Assert.DoesNotContain(copy.Path, report, StringComparison.Ordinal);
        Assert.DoesNotContain(included.Path, report, StringComparison.Ordinal);
        Assert.DoesNotContain("omitted", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("different-tree")]
    [InlineData("")]
    public void SameSkillMarkdownDoesNotHideDifferentOrUnknownDirectoryContents(string copyTree)
    {
        var included = IdentifiedFile("plugins/a/skills/check/SKILL.md", "check", "tree");
        var copy = IdentifiedFile("skills/check/SKILL.md", "check", copyTree);
        Assert.Equal(included.BlobSha, copy.BlobSha);
        var items = SkillDiscovery.Compare(Repo, [included, copy], [included, copy], [included, copy],
            [Entry("plugins/a/skills/check")], [Entry("plugins/a/skills/check")]);
        var alternative = Item(items, copy.Path);
        Assert.Equal(SkillDiscoveryKind.AlternativeLocation, alternative.Kind);
        Assert.Contains(included.Path, alternative.Detail, StringComparison.Ordinal);
        Assert.False(SkillDiscovery.NeedsReview(alternative));
        string report = RenderReport(items);
        Assert.Contains("## Alternative locations", report, StringComparison.Ordinal);
        Assert.Contains($"| {Repo} | skills | plugins/a/skills |", report, StringComparison.Ordinal);
        Assert.DoesNotContain(copy.Path, report, StringComparison.Ordinal);
        Assert.DoesNotContain(included.Path, report, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferenceInEitherSnapshotKeepsAlternativeVisible()
    {
        var included = IdentifiedFile("plugins/a/skills/check/SKILL.md", "check", "tree");
        var copy = IdentifiedFile("skills/check/SKILL.md", "check", "tree");
        var changedCopy = copy with { DirectoryTreeSha = "changed" };
        var manifest = new[] { Entry("plugins/a/skills/check") };
        var items = SkillDiscovery.Compare(Repo, [included, copy], [included, changedCopy], [included, copy], manifest, manifest);
        Assert.Equal(SkillDiscoveryKind.AlternativeLocation, Item(items, copy.Path).Kind);
    }

    [Fact]
    public void DifferentVersionsAreComparedWithinTheirOwnSnapshots()
    {
        var included = IdentifiedFile("plugins/a/skills/check/SKILL.md", "check", "old-tree");
        var copy = IdentifiedFile("skills/check/SKILL.md", "check", "old-tree");
        var manifest = new[] { Entry("plugins/a/skills/check") };
        var items = SkillDiscovery.Compare(Repo, [included, copy], [included, copy],
            [included with { DirectoryTreeSha = "new-tree" }, copy with { DirectoryTreeSha = "new-tree" }], manifest, manifest);
        Assert.Equal(SkillDiscoveryKind.IdenticalIncludedCopy, Item(items, copy.Path).Kind);
    }

    [Fact]
    public void OtherRepoOrUnverifiedOrDifferentNamesRemainExcluded()
    {
        var included = IdentifiedFile("plugins/a/skills/check/SKILL.md", "check", "tree");
        var different = IdentifiedFile("skills/check/SKILL.md", "different", "tree");
        var unverified = IdentifiedFile("unverified/check/SKILL.md", "check", "tree") with { HasFrontmatter = false };
        var foreign = IdentifiedFile("foreign/skill/SKILL.md", "foreign", "tree");
        SkillTreeFile[] files = [included, different, unverified, foreign];
        var items = SkillDiscovery.Compare(Repo, files, files, files,
            [Entry("plugins/a/skills/check"), Entry("foreign/skill") with { SourceRepo = "other/repo" }], []);
        Assert.Equal(SkillDiscoveryKind.ExcludedAtBaseline, Item(items, different.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.ExcludedAtBaseline, Item(items, unverified.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.ExcludedAtBaseline, Item(items, foreign.Path).Kind);
    }

    [Fact]
    public void AlternativeLocationsDoNotHideMissingManifestPathsOrMoves()
    {
        var original = IdentifiedFile("plugins/a/skills/check/SKILL.md", "check", "tree");
        var copy = IdentifiedFile("skills/check/SKILL.md", "check", "tree");
        var moved = IdentifiedFile("new/skills/check/SKILL.md", "check", "tree");
        var manifest = new[] { Entry("plugins/a/skills/check") };
        var items = SkillDiscovery.Compare(Repo, [original, copy], [original, copy], [copy, moved], manifest, manifest);
        Assert.Equal(SkillDiscoveryKind.AlternativeLocation, Item(items, copy.Path).Kind);
        Assert.Equal(SkillDiscoveryKind.PossibleMove, Item(items, moved.Path).Kind);
        Assert.Equal("preview", Assert.Single(items, item => item.Kind == SkillDiscoveryKind.MissingUpstream).Availability);
    }

    [Fact]
    public void InventoryRetainsParentDirectoryTreeShaRatherThanSkillBlobSha()
    {
        using var document = JsonDocument.Parse("""
            {"sha":"root-tree","truncated":false,"tree":[
              {"path":"skills/one","sha":"directory-one","type":"tree"},
              {"path":"skills/two","sha":"directory-two","type":"tree"},
              {"path":"skills/one/SKILL.md","sha":"same-markdown","type":"blob"},
              {"path":"skills/two/SKILL.md","sha":"same-markdown","type":"blob"},
              {"path":"SKILL.md","sha":"root-markdown","type":"blob"}]}
            """);
        var files = MaintenanceGh.ReadTree(document.RootElement);
        Assert.Equal("directory-one", Assert.Single(files, file => file.Path == "skills/one/SKILL.md").DirectoryTreeSha);
        Assert.Equal("directory-two", Assert.Single(files, file => file.Path == "skills/two/SKILL.md").DirectoryTreeSha);
        Assert.Equal("root-tree", Assert.Single(files, file => file.Path == "SKILL.md").DirectoryTreeSha);
    }

    [Fact]
    public void InvalidHistoricalFrontmatterIsNeverUsedForDeduplication()
    {
        var file = IdentifiedFile("skills/check/SKILL.md", "check", "tree");
        var unverified = MaintenanceGh.ReadIdentity(file, "invalid", allowInvalidFrontmatter: true);
        Assert.False(unverified.HasFrontmatter);
        Assert.Contains("not deduplicated", unverified.Description, StringComparison.Ordinal);
        _ = Assert.Throws<IOException>(() => MaintenanceGh.ReadIdentity(file, "invalid", allowInvalidFrontmatter: false));
    }

    static SkillTreeFile IdentifiedFile(string path, string name, string treeSha)
        => MaintenanceGh.ReadFrontmatter(File(path) with { DirectoryTreeSha = treeSha }, $"---\nname: {name}\ndescription: test\n---\n");

    static string RenderReport(IReadOnlyList<SkillDiscoveryItem> items)
        => SkillDiscoveryReport.Render([new(new(Repo, "base", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            new("v1", "stable", DateTimeOffset.UnixEpoch), new("main", "preview", DateTimeOffset.UnixEpoch), items)], [], DateTimeOffset.UnixEpoch);

    static SkillTreeFile File(string path) => new(path, "blob", path.Split('/')[^2]);

    static SkillManifestEntry Entry(string argument) => new(Repo, argument, argument, "dotnet", []);

    static SkillDiscoveryItem Item(IReadOnlyList<SkillDiscoveryItem> items, string path)
        => Assert.Single(items, item => item.Skill.Path == path && item.Kind != SkillDiscoveryKind.MissingUpstream);
}
