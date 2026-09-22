namespace Agentic.Check.LiveTests;

public sealed class SkillDiscoveryReportTests
{
    [Fact]
    public void CombinesReposIntoOrderedActionTablesAndSeparatesExclusionsByRepo()
    {
        var scans = new[] { Scan("owner/one"), Scan("owner/two") };
        string report = SkillDiscoveryReport.Render(scans, [], DateTimeOffset.UnixEpoch);
        Assert.Equal(1, Count(report, "## New candidates\n"));
        Assert.Equal(1, Count(report, "## Deferred candidates\n"));
        Assert.Contains("| owner/one | skills | [deferred](https://github.com/owner/one/blob/preview-sha/skills/deferred/SKILL.md) | preview | Waiting for a release (v/t: doc/x.md). |", report, StringComparison.Ordinal);
        Assert.True(report.IndexOf("## New candidates", StringComparison.Ordinal) < report.IndexOf("## Deferred candidates", StringComparison.Ordinal));
        Assert.True(report.IndexOf("## Deferred candidates", StringComparison.Ordinal) < report.IndexOf("## Missing / moved skills", StringComparison.Ordinal));
        Assert.Equal(1, Count(report, "## Missing / moved skills\n"));
        Assert.Equal(1, Count(report, "## Alternative locations\n"));
        Assert.Equal(2, Count(report, "### Excluded: "));
        Assert.True(report.IndexOf("## New candidates", StringComparison.Ordinal) < report.IndexOf("## Missing / moved skills", StringComparison.Ordinal));
        Assert.True(report.IndexOf("## Missing / moved skills", StringComparison.Ordinal) < report.IndexOf("## Alternative locations", StringComparison.Ordinal));
        Assert.True(report.IndexOf("## Alternative locations", StringComparison.Ordinal) < report.IndexOf("## Excluded skills", StringComparison.Ordinal));
        Assert.Contains("[owner/one](#excluded-ownerone)", report, StringComparison.Ordinal);
        Assert.Contains("[owner/two](#excluded-ownertwo)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("already-included", report, StringComparison.Ordinal);
        Assert.DoesNotContain("identical-copy", report, StringComparison.Ordinal);
        Assert.DoesNotContain("lengthy description", report, StringComparison.Ordinal);
        Assert.Contains("| Possible move | preview | old/skills/moved/SKILL.md |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AlternativeTableDeduplicatesRepoAndParentFolderWithoutListingSkillNames()
    {
        string report = SkillDiscoveryReport.Render([Scan("owner/one"), Scan("owner/two")], [], DateTimeOffset.UnixEpoch);
        Assert.Equal(1, Count(report, "| owner/one | copies | included, other |"));
        Assert.Equal(1, Count(report, "| owner/two | copies | included, other |"));
        Assert.DoesNotContain("copy-one", report, StringComparison.Ordinal);
        Assert.DoesNotContain("copy-two", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanErrorsAppearFirstOnlyWhenPresentAndEscapeMarkdown()
    {
        string report = SkillDiscoveryReport.Render([Scan("owner/ok")], [new("owner/error", "HTTP 403 | <error>\nretry")], DateTimeOffset.UnixEpoch);
        Assert.True(report.IndexOf("## Scan errors", StringComparison.Ordinal) < report.IndexOf("## New candidates", StringComparison.Ordinal));
        Assert.Contains("HTTP 403 &#124; &lt;error&gt; retry", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## Scan errors", SkillDiscoveryReport.Render([], [], DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    [Fact]
    public void StableOnlyLinksUseStableSnapshotAndRootSkillsHaveRootParent()
    {
        var scan = Scan("owner/one") with { Items = [Item(SkillDiscoveryKind.NewCandidate, "root-skill", "SKILL.md", "stable only")] };
        string report = SkillDiscoveryReport.Render([scan], [], DateTimeOffset.UnixEpoch);
        Assert.Contains("| (root) | [root-skill](https://github.com/owner/one/blob/stable-sha/SKILL.md)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("### Excluded:", report, StringComparison.Ordinal);
    }

    static SkillRepositoryScan Scan(string repo)
        => new(new(repo, "baseline-sha", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            new("v1", "stable-sha", DateTimeOffset.UnixEpoch), new("main", "preview-sha", DateTimeOffset.UnixEpoch),
            [
                Item(SkillDiscoveryKind.NewCandidate, "new", "skills/new/SKILL.md"),
                Item(SkillDiscoveryKind.Deferred, "deferred", "skills/deferred/SKILL.md") with { Detail = "Waiting for a release (v/t: doc/x.md)." },
                Item(SkillDiscoveryKind.MissingUpstream, "missing", "skills/missing/SKILL.md"),
                Item(SkillDiscoveryKind.PossibleMove, "moved", "skills/moved/SKILL.md") with { RelatedPaths = ["old/skills/moved/SKILL.md"] },
                Item(SkillDiscoveryKind.AlternativeLocation, "copy-one", "copies/one/SKILL.md") with { RelatedPaths = ["included/one/SKILL.md"] },
                Item(SkillDiscoveryKind.AlternativeLocation, "copy-two", "copies/two/SKILL.md") with { RelatedPaths = ["included/two/SKILL.md", "other/one/SKILL.md"] },
                Item(SkillDiscoveryKind.ExcludedAtBaseline, "excluded", "skills/excluded/SKILL.md"),
                Item(SkillDiscoveryKind.Included, "already-included", "skills/already-included/SKILL.md"),
                Item(SkillDiscoveryKind.IdenticalIncludedCopy, "identical-copy", "skills/identical-copy/SKILL.md")
            ]);

    static SkillDiscoveryItem Item(SkillDiscoveryKind kind, string name, string path, string source = "preview")
        => new(kind, new(path, "blob", name, "lengthy description"), source);

    static int Count(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;
}
