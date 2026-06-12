using System.CommandLine;
using Spectre.Console;

namespace Agentic.Check;

static class AgenticCheckCli
{
    internal static async Task<int> InvokeAsync(string[] args)
    {
        Argument<DirectoryInfo> targetDirectoryArgument = new(
            "target-dir",
            () => new DirectoryInfo(Environment.CurrentDirectory),
            "Target directory. Defaults to the current working directory.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        Option<bool> dryRunOption = new("--dry-run", "Report intended actions without changing files or running installs.");
        Option<bool> yesOption = new("--yes", "Approve fixes and select all recommended missing skills.");
        Option<FileInfo?> reportOption = new("--report", "Write a JSON report to this path.");
        Option<DirectoryInfo?> skillsDirectoryOption = new("--skills-dir", "Repo-local skills directory. Defaults to <repo-root>/.agents/skills.");
        Option<bool> verboseOption = new("--verbose", "Include detailed command and scan information.");

        RootCommand rootCommand = new("Check and install recommended agentic engineering skills.")
        {
            targetDirectoryArgument,
            dryRunOption,
            yesOption,
            reportOption,
            skillsDirectoryOption,
            verboseOption
        };

        rootCommand.SetHandler(
            async (targetDirectory, dryRun, yes, report, skillsDirectory, verbose) =>
            {
                var options = new AgenticCheckOptions(
                    targetDirectory.FullName,
                    dryRun,
                    yes,
                    report?.FullName,
                    skillsDirectory?.FullName,
                    verbose);

                var workflow = new CheckWorkflow(
                    new ProcessCommandRunner(),
                    new SpectreUserPrompts(AnsiConsole.Console),
                    new SpectreReporter(AnsiConsole.Console));

                var result = await workflow.RunAsync(options, CancellationToken.None).ConfigureAwait(false);
                Environment.ExitCode = result.ExitCode;
            },
            targetDirectoryArgument,
            dryRunOption,
            yesOption,
            reportOption,
            skillsDirectoryOption,
            verboseOption);

        int parseExitCode = await rootCommand.InvokeAsync(args).ConfigureAwait(false);
        return parseExitCode == 0 ? Environment.ExitCode : parseExitCode;
    }
}
