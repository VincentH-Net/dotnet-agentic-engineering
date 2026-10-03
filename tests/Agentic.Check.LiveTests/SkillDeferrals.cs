using System.Reflection;

namespace Agentic.Check.LiveTests;

// An upstream skill whose inclusion waits. Discovery reports it as deferred, without failing the
// maintenance test, until its trigger is reached; it then counts as a new candidate again, whatever the
// review baseline says, until included or excluded. A deferral waits for one of two things:
// - something outside the skill's repo, typically the release of the control it documents: TriggerPath
//   exists in TriggerRepo's latest release (or default branch when it has no releases);
// - a later version of this tool, for a skill planned for that version: Agentic.Check reaches
//   UntilCheckVersion, so that version cannot ship without the skill being included or excluded.
sealed record SkillDeferral(string SourceRepo, string SkillName, string TriggerRepo, string TriggerPath, string WaitingFor)
{
    // The major.minor of Agentic.Check that ends a version-scoped deferral; null for a release trigger.
    internal string? UntilCheckVersion { get; init; }

    internal string Trigger => UntilCheckVersion is null ? $"{TriggerRepo}: {TriggerPath}" : $"Agentic.Check {UntilCheckVersion}";

    internal static SkillDeferral UntilCheck(string sourceRepo, string skillName, string version, string waitingFor)
        => new(sourceRepo, skillName, string.Empty, string.Empty, waitingFor) { UntilCheckVersion = version };

    // What ended the deferral, given what its trigger reported.
    internal string Released(string release)
        => UntilCheckVersion is null ? $"now released in {TriggerRepo} {release}" : $"Agentic.Check is now {release}";

    // A version-scoped deferral holds below its major.minor and is over from there on: any later minor or
    // major ends it too, and so does a prerelease of that minor, whatever the patch.
    internal string? DueIn(ToolVersion check)
    {
        var until = ToolVersion.ParseMinimum(UntilCheckVersion ?? throw new InvalidOperationException($"{SkillName} is not deferred until a version."));
        return (check.Major, check.Minor).CompareTo((until.Major, until.Minor)) >= 0 ? check.ToString() : null;
    }
}

static class SkillDeferrals
{
    internal static IReadOnlyList<SkillDeferral> All { get; } =
    [
        // A valuable addition for Windows Forms repositories, but offering it only there needs a WinForms
        // install gate: new detection, which belongs in a minor release rather than a patch.
        SkillDeferral.UntilCheck("dotnet/skills", "winforms-expert", "2.5", "the next minor release, which adds the WinForms install gate it needs")
    ];

    // The Agentic.Check build under test, without build metadata.
    internal static ToolVersion CheckVersion { get; } = ToolVersion.Parse(
        (typeof(SkillManifestEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("Agentic.Check carries no informational version.")).Split('+')[0]);

    internal static IReadOnlyList<SkillDeferral> For(string repo)
        => [.. All.Where(deferral => deferral.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase))];
}
