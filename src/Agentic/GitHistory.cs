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
    internal async Task<int> ShowAsync(string? since, string? until, CancellationToken cancellationToken)
    {
        List<string> args = ["log", "--reverse", "--format=%H"];
        if (since is not null)
        {
            args.Add("--since=" + since);
        }

        if (until is not null)
        {
            args.Add("--until=" + until);
        }

        args.Add("--");
        string hashes = await runner.RunAsync(args, directory, cancellationToken).ConfigureAwait(false);
        int result = 0;
        foreach (string hash in hashes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= await ReadAsync(hash, show: true, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    internal async Task<int> CheckAsync(string revision, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-') || revision.Any(char.IsControl))
        {
            throw new FormatException("Invalid commit revision.");
        }

        string hash = await runner.RunAsync(["rev-parse", "--verify", "--end-of-options", revision + "^{commit}"], directory, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(hash.Trim(), show: false, cancellationToken).ConfigureAwait(false);
    }

    async Task<int> ReadAsync(string hash, bool show, CancellationToken cancellationToken)
    {
        if (hash.Length is not (40 or 64) || !hash.All(char.IsAsciiHexDigit))
        {
            throw new FormatException("Git returned an invalid object identifier.");
        }

        string message = await runner.RunAsync(["show", "-s", "--format=%cI%n%B", hash, "--"], directory, cancellationToken).ConfigureAwait(false);
        int newline = message.IndexOf('\n', StringComparison.Ordinal);
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
