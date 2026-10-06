using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class ManifestHistoryTests(ITestOutputHelper output)
{
    // A manifest never forgets a skill: what the previous release offered is offered now or retired, so a
    // check can remove it from the repositories that have it.
    [SkillMaintenanceFact]
    [Trait("Category", "SkillMaintenance")]
    public async Task EverySkillThePreviousReleaseOfferedIsOfferedOrRetired()
    {
        var (version, skills) = await PublishedManifest.PreviousReleaseAsync().ConfigureAwait(true);
        output.WriteLine($"Previous release: Agentic.Check {version}, {skills.Count} manifest entries.");
        HashSet<SkillIdentity> known = [.. StaticSkillManifest.All.Concat(StaticSkillManifest.Preview).Select(StaticSkillManifest.Identity), .. StaticSkillManifest.Retired];
        string[] forgotten = [.. skills.Select(skill => new SkillIdentity(skill.SourceRepo, skill.LocalFolder)).Distinct()
            .Where(identity => !known.Contains(identity)).Select(identity => $"{identity.SourceRepo} {identity.Name}").Order(StringComparer.Ordinal)];

        Assert.True(forgotten.Length == 0,
            $"Agentic.Check {version} offered skills that neither set offers now; add them to StaticSkillManifest.Retired:{Environment.NewLine}{string.Join(Environment.NewLine, forgotten)}");
    }
}
