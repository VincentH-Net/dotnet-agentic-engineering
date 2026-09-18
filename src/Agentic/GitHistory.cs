using System.Diagnostics;

namespace Agentic;

interface IGitCommandRunner
{
    Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken);
}

sealed class GitCommandRunner(string executable = "git") : IGitCommandRunner
{
    public async Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken)
    {
        ProcessStartInfo info = new(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = info };
        _ = process.Start();
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
            string output = await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);
            return process.ExitCode == 0 ? output : throw new IOException(error.TrimEnd());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}

sealed class GitHistory(IGitCommandRunner runner, string directory, TextWriter output, TextWriter error)
{
    internal async Task<int> ShowAsync(string? since, string? until, int? limit, CancellationToken cancellationToken)
    {
        // Filter before limiting so ordinary commits never consume the prompt-log budget.
        // Match either delimiter, including CR line endings, to retain malformed-log errors.
        List<string> args = ["log", "-z", "--format=%H%n%cI%n%B", "--no-notes", "--no-show-signature",
            "--extended-regexp", "--grep=(^|\r)prompt-log(-end)?:(\r|$)"];
        if (limit is not null)
            args.Add("--max-count=" + Math.Min((long)limit + 1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (since is not null)
        {
            args.Add("--since=" + since);
        }

        if (until is not null)
        {
            args.Add("--until=" + until);
        }

        args.Add("--");
        string history = await runner.RunAsync(args, directory, cancellationToken).ConfigureAwait(false);
        string[] records = history.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        int count = Math.Min(limit ?? records.Length, records.Length);
        int result = 0;
        for (int index = count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string record = records[index];
            int newline = record.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0)
                throw new FormatException("Git returned an invalid history record.");
            string hash = record[..newline];
            ValidateHash(hash);
            result |= await DisplayAsync(hash, record[(newline + 1)..], show: true).ConfigureAwait(false);
        }

        if (records.Length > count)
            await error.WriteLineAsync($"Showing the latest {count} prompt logs; older entries omitted. Use --limit N or --all to show more.").ConfigureAwait(false);

        return result;
    }

    internal async Task<int> CheckAsync(string revision, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-') || revision.Any(char.IsControl))
        {
            throw new FormatException("Invalid commit revision.");
        }

        string resolved = await runner.RunAsync(["rev-parse", "--verify", "--end-of-options", revision + "^{commit}"], directory, cancellationToken).ConfigureAwait(false);
        string hash = resolved.Trim();
        ValidateHash(hash);
        string message = await runner.RunAsync(["show", "-s", "--format=%cI%n%B", hash, "--"], directory, cancellationToken).ConfigureAwait(false);
        return await DisplayAsync(hash, message, show: false).ConfigureAwait(false);
    }

    static void ValidateHash(string hash)
    {
        if (hash.Length is not (40 or 64) || !hash.All(char.IsAsciiHexDigit))
        {
            throw new FormatException("Git returned an invalid object identifier.");
        }
    }

    async Task<int> DisplayAsync(string hash, string message, bool show)
    {
        int newline = message.IndexOf('\n', StringComparison.Ordinal);
        if (newline < 0)
            throw new FormatException("Git returned an invalid history record.");
        try
        {
            var log = PromptLogReader.Parse(message[(newline + 1)..]);
            if (log is null)
            {
                if (!show)
                {
                    await output.WriteLineAsync($"{hash}: no prompt log.").ConfigureAwait(false);
                }

                return 0;
            }

            await output.WriteLineAsync($"{hash} {message[..newline]}: {log.Description}").ConfigureAwait(false);
            if (show)
            {
                await output.WriteLineAsync(log.Text).ConfigureAwait(false);
                await output.WriteLineAsync().ConfigureAwait(false);
            }

            return 0;
        }
        catch (FormatException exception)
        {
            await error.WriteLineAsync($"{hash}: malformed prompt log: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
