using System.CommandLine;
using System.CommandLine.Help;
using System.Reflection;

namespace Agentic;

static class AgenticCli
{
    internal static async Task<int> InvokeAsync(
        string[] args,
        TextReader? input = null,
        TextWriter? output = null,
        TextWriter? error = null,
        IGitCommandRunner? git = null,
        string? directory = null,
        string? runningVersion = null,
        Action<CompatibilityContext>? observeContext = null,
        Func<string, string?>? readEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        input ??= Console.In;
        if (args is ["check", .. var checkArguments])
            return await ToolLauncher.CheckAsync(checkArguments, directory ?? Environment.CurrentDirectory, error, cancellationToken).ConfigureAwait(false);
        runningVersion ??= typeof(AgenticCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var running = ToolVersion.Parse(runningVersion);
        Option<string?> minimum = new("--minver", "-m")
        {
            Recursive = true,
            Description = "The major.minor a directive or skill was written for. Keeps instructions and this tool versioned together: fails on another major or an older minor. Defaults to the running version."
        };
        minimum.Validators.Add(result =>
        {
            try
            {
                if (result.GetValueOrDefault<string?>() is { } value)
                {
                    _ = ToolVersion.ParseMinimum(value);
                }
            }
            catch (FormatException exception)
            {
                result.AddError(exception.Message);
            }
        });
        // Help shows the command the user actually typed: dna when the shorthand launched us, otherwise dotnet agentic.
        string launcher = string.IsNullOrEmpty((readEnvironment ?? Environment.GetEnvironmentVariable)(DnaLauncherContract.VersionVariable)) ? "dotnet agentic" : "dna";
        RootCommand root = new("Folder-local agentic tool for directives, skills and humans. dna is the shorthand for dotnet agentic. Prompt-log Git operations are read-only.");
        root.Options.OfType<VersionOption>().Single().Validators.Clear();
        root.Options.Add(minimum);
        root.Subcommands.Add(new Command("check", "Run the latest stable Agentic.Check; all following arguments are forwarded."));
        Command prompt = new("prompt-log", "Wrap, display, and validate prompt logs. Defaults to show (latest 20 logs).");
        root.Subcommands.Add(prompt);
        Option<string> inputFile = new("--input") { Required = true, Description = "Complete sanitized raw log file, or - for stdin. No per-entry encoding." };
        Option<string> blockFile = new("--prompt-log") { DefaultValueFactory = _ => "-", Description = "Output block file to replace atomically, or - for stdout (default). Never appends; empty input produces empty output." };
        Command wrap = new("wrap", "Wrap a complete raw prompt log in a commit-message block, escaping delimiter lines reversibly.");
        wrap.Options.Add(inputFile);
        wrap.Options.Add(blockFile);
        Option<string?> since = new("--since") { Description = "Show logs newer than this date." };
        Option<string?> until = new("--until") { Description = "Show logs older than this date." };
        Option<int?> limit = new("--limit")
        {
            Description = "Maximum number of recent prompt logs to show (default: 20).",
            CustomParser = result =>
            {
                if (result.Tokens.Count == 1 && int.TryParse(result.Tokens[0].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value) && value > 0)
                {
                    return value;
                }
                result.AddError("--limit must be a positive integer.");
                return null;
            }
        };
        Option<bool> all = new("--all") { Description = "Show all matching prompt logs reachable from HEAD." };
        Command show = new("show", "Show the latest 20 prompt logs in chronological order.");
        foreach (var command in new[] { prompt, show })
        {
            command.Options.Add(since);
            command.Options.Add(until);
            command.Options.Add(limit);
            command.Options.Add(all);
            command.Validators.Add(result =>
            {
                if (result.GetResult(all) is { Implicit: false } && result.GetResult(limit) is { Implicit: false })
                    result.AddError("--limit and --all cannot be used together.");
            });
        }
        Option<string> commit = new("--commit") { DefaultValueFactory = _ => "HEAD" };
        Command check = new("check", "Validate structure of a commit's prompt log.");
        check.Options.Add(commit);
        GitHistory history = new(git ?? new GitCommandRunner(), directory ?? Environment.CurrentDirectory, output, error);
        foreach (var command in new[] { wrap, show, check })
        {
            prompt.Subcommands.Add(command);
            command.SetAction(async (parse, token) =>
            {
                try
                {
                    string? requested = parse.GetValue(minimum);
                    CompatibilityContext context = new(running, requested is null ? new(running.Major, running.Minor, 0, string.Empty) : ToolVersion.ParseMinimum(requested), requested is not null);
                    if (!running.Satisfies(context.Required))
                    {
                        await error.WriteLineAsync($"InnoWvate.Agentic {runningVersion} is incompatible with --minver {context.Required.Minimum}.\nRequired: major {context.Required.Major}, minor {context.Required.Minor} or later.\nStop this operation and ask the user to run agentic-check interactively\nin the intended target directory, then retry.").ConfigureAwait(false);
                        return 1;
                    }

                    observeContext?.Invoke(context);
                    if (command == wrap)
                    {
                        await PromptLogWrapper.WrapAsync(parse.GetValue(inputFile)!, parse.GetValue(blockFile)!, input, token, stdout: output).ConfigureAwait(false);
                        return 0;
                    }

                    return command == show
                        ? await history.ShowAsync(parse.GetValue(since), parse.GetValue(until), parse.GetValue(all) ? null : parse.GetValue(limit) ?? 20, token).ConfigureAwait(false)
                        : await history.CheckAsync(parse.GetValue(commit)!, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException or NotSupportedException)
                {
                    await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 1;
                }
            });
        }

        prompt.Action = show.Action;
        var parsed = root.Parse(args);
        // System.CommandLine 2.0.9 prints the executable name in Usage lines and offers no way to rename it,
        // so help and parse-error output is buffered and those lines are rewritten to the launcher command.
        StringWriter? help = parsed.Action is HelpAction || parsed.Errors.Count > 0 ? new() : null;
        InvocationConfiguration configuration = new() { Output = help ?? output, Error = error, EnableDefaultExceptionHandler = false };
        int code = await parsed.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (help is not null)
            await output.WriteAsync(WithLauncherUsage(help.ToString(), launcher)).ConfigureAwait(false);
        return parsed.Errors.Count > 0 ? 2 : code;
    }

    internal static string WithLauncherUsage(string helpText, string launcher)
    {
        string usage = "  " + RootCommand.ExecutableName;
        string[] lines = helpText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line == usage || line.StartsWith(usage + " ", StringComparison.Ordinal))
                lines[i] = "  " + launcher + lines[i][usage.Length..];
        }
        return string.Join('\n', lines);
    }
}
