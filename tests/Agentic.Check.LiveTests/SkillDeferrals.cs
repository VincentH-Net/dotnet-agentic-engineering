namespace Agentic.Check.LiveTests;

// An upstream skill whose inclusion waits for something outside its repo, typically the release of the
// control it documents. Discovery reports it as deferred, without failing the maintenance test, until
// TriggerPath exists in TriggerRepo's latest release (or default branch when it has no releases). It
// then counts as a new candidate again, whatever the review baseline says, until included or excluded.
sealed record SkillDeferral(string SourceRepo, string SkillName, string TriggerRepo, string TriggerPath, string WaitingFor)
{
    internal string Trigger => $"{TriggerRepo}: {TriggerPath}";
}

static class SkillDeferrals
{
    // FlexPanel (unoplatform/uno.toolkit.ui#1639) is now a reference inside the uno-toolkit hub, so nothing waits.
    internal static IReadOnlyList<SkillDeferral> All { get; } = [];

    internal static IReadOnlyList<SkillDeferral> For(string repo)
        => [.. All.Where(deferral => deferral.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase))];
}
