using System.CommandLine;
using Spectre.Console;

namespace Agentic.Check;

static class AgenticCheckCli
{
    static readonly HashSet<string> KnownOptions = new(StringComparer.Ordinal)
    {
        "--agents",
        "--skills-dir",
        "--dry-run",
        "--preview",
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

        Argument<DirectoryInfo> targetDirectoryArgument = new("target-dir")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => new DirectoryInfo(Environment.CurrentDirectory),
            Description = "Target directory."
        };

        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Report recommended actions without applying them."
        };
        Option<bool> previewOption = new("--preview")
        {
            Description = "Install preview directives and skills from source repo default branches."
        };
        Option<bool> yesOption = new("--yes")
        {
            Description = "Apply all recommended actions."
        };
        Option<FileInfo?> reportOption = new("--report")
        {
            Description = "Write a JSON report to this path."
        };
        Option<string?> skillsDirectoryOption = new("--skills-dir")
        {
            Description = "Target-contained relative skills directory below the target directory, for example .agents/skills or .claude/skills. Absolute paths and paths that escape the target directory are not allowed. Overrides directories implied by --agents."
        };
        Option<string?> agentsOption = new("--agents")
        {
            Description = $"""
            Comma-separated agent values to support. [default: {defaultAgents}]

            Supported agent values (identical to what 'gh skill' supports):
            {AgentSkillRegistry.AgentHelpLines}
            """
        };
        agentsOption.Validators.Add(result =>
        {
            var validation = AgentSkillRegistry.ValidateAgentsValue(result.GetValueOrDefault<string?>());
            if (!validation.Success)
            {
                result.AddError(validation.Error ?? "Invalid --agents value.");
            }
        });
        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Include detailed command and scan information."
        };

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

        Folder Specializing
        
        agentic-check supports specializing folders in your repo, e.g. to have common
        directives and skills in the repo root, but additional and different ones in
        backend and frontend subfolders:
        1. Start agentic-check in the repo root and select the common set of directives
           and skills to install there
        2. Start agentic-check in the backend subfolder and select the additional
           specialized set of directives and skills for that subfolder - agentic-check
           will automatically deselect any directives and skills that are already
           installed above or below the target folder. For agentic-check, above
           terminates at the repo root or else the drive root.
        3. Start agentic-check in the frontend subfolder and select the specialized
           set of directives and skills for that subfolder
        4. Start your agent in a (sub)folder of choice to use that specialized set of
           instructions. Multiple harnesses support this, including Codex CLI (composes 
           above) and Claude Code CLI (composes above, as well as below when working on
           files below it's working dir).
        """);
        rootCommand.Arguments.Add(targetDirectoryArgument);
        rootCommand.Options.Add(agentsOption);
        rootCommand.Options.Add(skillsDirectoryOption);
        rootCommand.Options.Add(dryRunOption);
        rootCommand.Options.Add(previewOption);
        rootCommand.Options.Add(yesOption);
        rootCommand.Options.Add(reportOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.SetAction(
            async (parseResult, cancellationToken) =>
            {
                SpectreReporter reporter = new(AnsiConsole.Console);
                try
                {
                    reporter.Header();

                    var targetDirectory = parseResult.GetValue(targetDirectoryArgument)
                        ?? new DirectoryInfo(Environment.CurrentDirectory);
                    bool dryRun = parseResult.GetValue(dryRunOption);
                    bool preview = parseResult.GetValue(previewOption);
                    bool yes = parseResult.GetValue(yesOption);
                    var report = parseResult.GetValue(reportOption);
                    string? skillsDirectory = parseResult.GetValue(skillsDirectoryOption);
                    string? agents = parseResult.GetValue(agentsOption);
                    bool verbose = parseResult.GetValue(verboseOption);

                    if (skillsDirectory is not null && !string.IsNullOrWhiteSpace(agents))
                    {
                        AnsiConsole.MarkupLine("[red]Specify no more than one of --skills-dir and --agents.[/]");
                        return 1;
                    }

                    string? effectiveAgents = string.IsNullOrWhiteSpace(agents) && skillsDirectory is null
                        ? defaultAgents
                        : agents;

                    var options = new AgenticCheckOptions(
                        targetDirectory.FullName,
                        dryRun,
                        yes,
                        report?.FullName,
                        skillsDirectory,
                        effectiveAgents,
                        verbose,
                        preview);

                    var workflow = new CheckWorkflow(
                        new ProcessCommandRunner(),
                        new SpectreUserPrompts(AnsiConsole.Console),
                        reporter);

                    var result = await workflow.RunAsync(options, CancellationToken.None).ConfigureAwait(false);
                    return result.ExitCode;
                }
                finally
                {
                    AnsiConsole.WriteLine();
                }
            });

        return await rootCommand.Parse(args).InvokeAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
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
