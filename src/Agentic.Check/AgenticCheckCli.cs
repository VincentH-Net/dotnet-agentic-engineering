using System.CommandLine;
using System.CommandLine.Parsing;
using Spectre.Console;

namespace Agentic.Check;

static class AgenticCheckCli
{
    static readonly HashSet<string> KnownOptions = new(StringComparer.Ordinal)
    {
        "--agents",
        "--skills-dir",
        "--dry-run",
        "--yes",
        "--report",
        "--verbose",
        "--version",
        "-?",
        "-h",
        "--help"
    };

    static readonly HashSet<string> OptionsWithValues = new(StringComparer.Ordinal)
    {
        "--agents",
        "--skills-dir",
        "--report"
    };

    internal static async Task<int> InvokeAsync(string[] args)
    {
        string? unknownOption = FindUnknownOption(args);
        if (unknownOption is not null)
        {
            await Console.Error.WriteLineAsync($"Unknown option: {unknownOption}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync().ConfigureAwait(false);
            return 1;
        }

        string defaultAgents = AgentSkillRegistry.DefaultAgents;
        if (IsHelpRequested(args) || !IsOptionSpecified(args, "--agents"))
        {
            defaultAgents = await AgentCliDetector
                .DetectDefaultAgentsAsync(new ProcessCommandRunner(), Environment.CurrentDirectory, CancellationToken.None)
                .ConfigureAwait(false);
        }

        Argument<DirectoryInfo> targetDirectoryArgument = new(
            "target-dir",
            () => new DirectoryInfo(Environment.CurrentDirectory),
            "Target directory.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        Option<bool> dryRunOption = new("--dry-run", "Report recommended actions without applying them.");
        Option<bool> yesOption = new("--yes", "Apply all recommended actions.");
        Option<FileInfo?> reportOption = new("--report", "Write a JSON report to this path.");
        Option<DirectoryInfo?> skillsDirectoryOption = new("--skills-dir", "Repo-local skills directory. Overrides directories implied by --agents.");
        Option<string?> agentsOption = new(
            "--agents", $"""
            Comma-separated agent values to support. [default: {defaultAgents}]

            Supported agent values (identical to what 'gh skill' supports):
            {AgentSkillRegistry.AgentHelpLines}
            """);
        agentsOption.AddValidator(result =>
        {
            var validation = AgentSkillRegistry.ValidateAgentsValue(result.GetValueOrDefault<string?>());
            if (!validation.Success)
            {
                result.ErrorMessage = validation.Error;
            }
        });
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
        rootCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(skillsDirectoryOption) is not null
                && !string.IsNullOrWhiteSpace(result.GetValueForOption(agentsOption)))
            {
                result.ErrorMessage = "Specify no more than one of --skills-dir and --agents.";
            }
        });

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

                    string? effectiveAgents = string.IsNullOrWhiteSpace(agents) && skillsDirectory is null
                        ? defaultAgents
                        : agents;

                    var options = new AgenticCheckOptions(
                        targetDirectory.FullName,
                        dryRun,
                        yes,
                        report?.FullName,
                        skillsDirectory?.FullName,
                        effectiveAgents,
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

    internal static bool IsHelpRequested(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.Equals("--", StringComparison.Ordinal))
            {
                return false;
            }

            if (arg is "--help" or "-h" or "-?")
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsOptionSpecified(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg.Equals("--", StringComparison.Ordinal))
            {
                return false;
            }

            if (arg.Equals(optionName, StringComparison.Ordinal)
                || arg.StartsWith(optionName + "=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? FindUnknownOption(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (arg.Equals("--", StringComparison.Ordinal))
            {
                return null;
            }

            if (arg.Equals("-", StringComparison.Ordinal))
            {
                continue;
            }

            string optionName = arg;
            int valueSeparatorIndex = arg.IndexOf('=', StringComparison.Ordinal);
            if (valueSeparatorIndex > 0)
            {
                optionName = arg[..valueSeparatorIndex];
            }

            if (!optionName.StartsWith('-'))
            {
                continue;
            }

            if (!KnownOptions.Contains(optionName))
            {
                return optionName;
            }

            if (valueSeparatorIndex < 0 && OptionsWithValues.Contains(optionName))
            {
                index++;
            }
        }

        return null;
    }
}
