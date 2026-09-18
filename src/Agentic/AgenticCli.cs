using System.CommandLine;
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
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        input ??= Console.In;
        if (args is ["check", .. var checkArguments])
            return await ToolLauncher.CheckAsync(checkArguments, directory ?? Environment.CurrentDirectory, error, cancellationToken).ConfigureAwait(false);
        runningVersion ??= typeof(AgenticCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var running = ToolVersion.Parse(runningVersion);
        Option<string?> minimum = new("--minver", "-m") { Recursive = true, Description = "Required major.minor; same major, this minor or later. Defaults to the running tool's major.minor." };
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
        RootCommand root = new("Repo-local agentic tooling. Prompt-log Git operations are read-only.");
        root.Options.OfType<VersionOption>().Single().Validators.Clear();
        root.Options.Add(minimum);
        root.Subcommands.Add(new Command("check", "Run the latest stable Agentic.Check; all following arguments are forwarded."));
        Command prompt = new("prompt-log", "Wrap, display, and validate prompt logs.");
        root.Subcommands.Add(prompt);
        Option<string> inputFile = new("--input") { Required = true, Description = "Complete sanitized raw log file, or - for stdin. No per-entry encoding." };
        Option<string> blockFile = new("--prompt-log") { DefaultValueFactory = _ => "-", Description = "Output block file to replace atomically, or - for stdout (default). Never appends; empty input produces empty output." };
        Command wrap = new("wrap", "Wrap a complete raw prompt log in a commit-message block, escaping delimiter lines reversibly.");
        wrap.Options.Add(inputFile);
        wrap.Options.Add(blockFile);
        Option<string?> since = new("--since");
        Option<string?> until = new("--until");
        Command show = new("show", "Show decoded reachable history in chronological order.");
        show.Options.Add(since);
        show.Options.Add(until);
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
                        ? await history.ShowAsync(parse.GetValue(since), parse.GetValue(until), token).ConfigureAwait(false)
                        : await history.CheckAsync(parse.GetValue(commit)!, token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException or NotSupportedException)
                {
                    await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 1;
                }
            });
        }

        var parsed = root.Parse(args);
        InvocationConfiguration configuration = new() { Output = output, Error = error, EnableDefaultExceptionHandler = false };
        int code = await parsed.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        return parsed.Errors.Count > 0 ? 2 : code;
    }
}
