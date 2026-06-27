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

        Option<bool> dryRunOption = new("--dry-run", "Report recommended actions without applying them.");
        Option<bool> yesOption = new("--yes", "Apply all recommended actions.");
        Option<FileInfo?> reportOption = new("--report", "Write a JSON report to this path.");
        Option<DirectoryInfo?> skillsDirectoryOption = new("--skills-dir", "Repo-local skills directory. Overrides --agents.");
        Option<string?> agentsOption = new(
            "--agents", $"""
            Comma-separated agent values to install; defaults to {AgentSkillRegistry.DefaultAgents}.

            Valid agent values: {AgentSkillRegistry.AgentIds}.

            (agent values are identical to what 'gh skill' supports)
            """);
        Option<bool> verboseOption = new("--verbose", "Include detailed command and scan information.");

        RootCommand rootCommand = new("""
        Optimizes your repo for agentic engineering with .NET - based technologies.

        - Detects which .NET based technologies and features you use
        - Recommends an optimal set of agentic directives and skills for those
        - You select which to apply
        - Directives are installed / updated in AGENTS.md, directly from the
          dotnet-agentic-engineering GitHub repo
        - Skills are installed / updated directly from source GitHub skill repo's with 
          'gh skill'

        The skills available for composition are carefully selected and tested from 
        best-in-class GitHub repo's. The composition minimizes context usage and avoids
        contradictions and ambiguities, reducing agent mistakes.

        Currently supports foundational agentic habits, .NET, ASP.NET, Microsoft Orleans
        and Uno Platform

        Uno Platform skills are selected depending on detected:
        - MVVM or MVUX update pattern
        - Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
        - Fluent / Material / Cupertino design system

        """
        ) {
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
                try
                {
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
                }
                finally
                {
                    AnsiConsole.WriteLine();
                }
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
