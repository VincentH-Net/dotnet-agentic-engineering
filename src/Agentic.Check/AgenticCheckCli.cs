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

        Option<bool> dryRunOption = new("--dry-run", "Report intended directive and skill actions without changing files or running installs.");
        Option<bool> yesOption = new("--yes", "Approve fixes and select all recommended directives and missing skills.");
        Option<FileInfo?> reportOption = new("--report", "Write a JSON report to this path.");
        Option<DirectoryInfo?> skillsDirectoryOption = new("--skills-dir", "Repo-local skills directory. Overrides --agents. Example: for Claude Code, use '.claude/skills'.");
        Option<string?> agentsOption = new(
            "--agents",
            $"Comma-separated agent values to install. Defaults to {AgentSkillRegistry.DefaultAgents}. Use {AgentSkillRegistry.StandardAgentId} for {AgentSkillRegistry.StandardProjectDirectory}; standard-path agents: {AgentSkillRegistry.StandardAgentNames}. Other valid agent values: {AgentSkillRegistry.AgentIds}.");
        Option<bool> verboseOption = new("--verbose", "Include detailed command and scan information.");

        RootCommand rootCommand = new("Check and install recommended agentic engineering directives and skills.")
        {
            targetDirectoryArgument,
            agentsOption,
            skillsDirectoryOption,
            dryRunOption,
            yesOption,
            reportOption,
            verboseOption
        };

        rootCommand.SetHandler(
            async (targetDirectory, dryRun, yes, report, skillsDirectory, agents, verbose) =>
            {
                SpectreReporter reporter = new(AnsiConsole.Console);
                reporter.Header();

                if (skillsDirectory is not null && !string.IsNullOrWhiteSpace(agents))
                {
                    AnsiConsole.MarkupLine("[red]Specify no more than one of --skills-dir and --agents.[/]");
                    Environment.ExitCode = 2;
                    return;
                }

                var options = new AgenticCheckOptions(
                    targetDirectory.FullName,
                    dryRun,
                    yes,
                    report?.FullName,
                    skillsDirectory?.FullName,
                    agents,
                    verbose);

                var workflow = new CheckWorkflow(
                    new ProcessCommandRunner(),
                    new SpectreUserPrompts(AnsiConsole.Console),
                    reporter);

                var result = await workflow.RunAsync(options, CancellationToken.None).ConfigureAwait(false);
                Environment.ExitCode = result.ExitCode;
            },
            targetDirectoryArgument,
            dryRunOption,
            yesOption,
            reportOption,
            skillsDirectoryOption,
            agentsOption,
            verboseOption);

        int parseExitCode = await rootCommand.InvokeAsync(args).ConfigureAwait(false);
        return parseExitCode == 0 ? Environment.ExitCode : parseExitCode;
    }
}
