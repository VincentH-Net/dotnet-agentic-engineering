namespace Agentic;

// Shared by the dna launcher and Agentic.Check; no other logic lives here.
static class DnaLauncherContract
{
    // dna sets this to its own package version before starting Agentic.Check, which then
    // verifies the launcher and never replaces the files of the dna that is still running.
    internal const string VersionVariable = "INNOWVATE_DNA_VERSION";
}
