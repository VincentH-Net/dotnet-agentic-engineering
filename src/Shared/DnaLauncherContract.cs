namespace Agentic;

// Shared by the dna launcher and Agentic.Check; no other logic lives here.
static class DnaLauncherContract
{
    // dna sets this to its own package version before starting Agentic.Check, which then
    // verifies the launcher and never replaces the files of the dna that is still running.
    internal const string VersionVariable = "INNOWVATE_DNA_VERSION";

    // The tool's root help and the launcher's fallback help share these lines.
    internal const string Description = "Repo-local agentic tool for directives, skills and humans. dna is the shorthand for dotnet agentic. Prompt-log Git operations are read-only.";
    internal const string CheckDescription = "Run the latest stable Agentic.Check; all following arguments are forwarded.";
    internal const string DocsUrl = "https://github.com/VincentH-Net/dotnet-agentic-engineering";

    // Help and the banner name the command the user typed. dna forwards every argument, while dnx
    // parses -h, --version and -y/--yes itself, so the standalone form needs the separator before
    // the tool's own arguments.
    internal const string CheckLauncher = "dna check";
    internal const string StandaloneCheckLauncher = "dnx agentic.check --";

    internal static string CurrentCheckLauncher
        => IsLaunchedByDna() ? CheckLauncher : StandaloneCheckLauncher;

    internal static bool IsLaunchedByDna(Func<string, string?>? readEnvironment = null)
        => !string.IsNullOrEmpty((readEnvironment ?? Environment.GetEnvironmentVariable)(VersionVariable));
}
