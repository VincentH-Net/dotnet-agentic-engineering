using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Agentic.Check;

enum HostPlatform
{
    MacOS,
    Windows,
    Linux
}

// Agentic.Check is interactive unless --yes or --dry-run says otherwise. Started without an interactive
// terminal, typically by a coding agent, it re-launches itself in a new terminal window so the user can
// make the selections there, and exits with HandedOffExitCode.
static partial class InteractiveTerminal
{
    internal const int HandedOffExitCode = 3;
    internal const string NeedsTerminalMessage = "Agentic.Check needs an interactive terminal to select actions. Run it in a terminal, or use --yes or --dry-run.";
    internal const string HandedOffMessage = "Opened Agentic.Check in a new terminal window; ask the user to finish there, then continue.";

    // Per-window shell state that must not be carried into the new window.
    static readonly HashSet<string> ExcludedVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "_", "SHLVL", "PWD", "OLDPWD", "TERM", "TERM_SESSION_ID", "TERM_PROGRAM", "TERM_PROGRAM_VERSION",
        "COLUMNS", "LINES", "PS1", "PROMPT", "SHELL", "TTY", "WINDOWID", "ITERM_SESSION_ID"
    };

    static readonly (string Command, Func<string, string[]> Arguments)[] LinuxTerminals =
    [
        ("x-terminal-emulator", run => ["-e", run]),
        ("gnome-terminal", run => ["--", "sh", "-c", run]),
        ("konsole", run => ["-e", "sh", "-c", run]),
        ("xfce4-terminal", run => ["-e", run]),
        ("xterm", run => ["-e", "sh", "-c", run])
    ];

    internal static HostPlatform Current => OperatingSystem.IsWindows() ? HostPlatform.Windows : OperatingSystem.IsMacOS() ? HostPlatform.MacOS : HostPlatform.Linux;

    internal static bool DesktopAvailable(HostPlatform platform, Func<string, string?> environment, bool userInteractive = true)
        => string.IsNullOrEmpty(environment("CI")) && platform switch
        {
            HostPlatform.MacOS => true,
            HostPlatform.Windows => userInteractive,
            _ => !string.IsNullOrEmpty(environment("DISPLAY")) || !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"))
        };

    // Under the dotnet muxer the first command-line argument is the tool DLL; under an apphost it is the executable itself.
    internal static IReadOnlyList<string> RelaunchCommand(string processPath, IReadOnlyList<string> commandLineArguments, IReadOnlyList<string> args)
    {
        string entry = commandLineArguments[0];
        bool appHost = !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFullPath(entry), Path.GetFullPath(processPath), StringComparison.OrdinalIgnoreCase);
        return appHost ? [processPath, .. args] : [processPath, entry, .. args];
    }

    internal static string ShellScript(string workingDirectory, IReadOnlyList<string> command, IEnumerable<KeyValuePair<string, string?>> environment)
    {
        StringBuilder script = new("#!/bin/sh\n");
        _ = script.Append("cd ").Append(ShellQuote(workingDirectory)).Append(" || exit 1\n");
        foreach (var (name, value) in Exported(environment).Where(setting => ShellNameRegex().IsMatch(setting.Key)))
            _ = script.Append("export ").Append(name).Append('=').Append(ShellQuote(value)).Append('\n');
        return script.Append(string.Join(' ', command.Select(ShellQuote))).Append('\n').ToString();
    }

    internal static string PowerShellScript(string workingDirectory, IReadOnlyList<string> command, IEnumerable<KeyValuePair<string, string?>> environment)
    {
        StringBuilder script = new();
        _ = script.Append("Set-Location -LiteralPath ").Append(PowerShellQuote(workingDirectory)).Append('\n');
        foreach (var (name, value) in Exported(environment))
            _ = script.Append("${env:").Append(name).Append("} = ").Append(PowerShellQuote(value)).Append('\n');
        return script.Append("& ").Append(string.Join(' ', command.Select(PowerShellQuote))).Append('\n').ToString();
    }

    internal static (string FileName, IReadOnlyList<string> Arguments) LaunchCommand(HostPlatform platform, string scriptPath, string? searchPath)
    {
        switch (platform)
        {
            case HostPlatform.MacOS:
                string source = $"source {ShellQuote(scriptPath)}".Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
                return ("osascript", ["-e", "tell application \"Terminal\"", "-e", "activate", "-e", $"do script \"{source}\"", "-e", "end tell"]);
            case HostPlatform.Windows:
                return ("cmd.exe", ["/c", "start", "Agentic.Check", "powershell", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", scriptPath]);
            case HostPlatform.Linux:
                string run = $". {ShellQuote(scriptPath)}; exec \"${{SHELL:-sh}}\"";
                foreach (var (command, arguments) in LinuxTerminals)
                {
                    string? found = FindOnPath(command, searchPath);
                    if (found is not null)
                        return (found, arguments(run));
                }
                throw new InvalidOperationException("no terminal emulator found on PATH (tried " + string.Join(", ", LinuxTerminals.Select(terminal => terminal.Command)) + ")");
            default:
                throw new ArgumentOutOfRangeException(nameof(platform));
        }
    }

    internal static async Task<int> HandOffAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var platform = Current;
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string?>((string)entry.Key, entry.Value as string)).ToList();
        if (!DesktopAvailable(platform, Environment.GetEnvironmentVariable, Environment.UserInteractive))
        {
            await error.WriteLineAsync(NeedsTerminalMessage + " (no desktop session detected)").ConfigureAwait(false);
            return 2;
        }

        string script = Path.Combine(Path.GetTempPath(), $"agentic-check-{Guid.NewGuid():N}.{(platform == HostPlatform.Windows ? "ps1" : "sh")}");
        try
        {
            var command = RelaunchCommand(Environment.ProcessPath!, Environment.GetCommandLineArgs(), args);
            await File.WriteAllTextAsync(script, platform == HostPlatform.Windows
                ? PowerShellScript(Environment.CurrentDirectory, command, environment)
                : ShellScript(Environment.CurrentDirectory, command, environment), cancellationToken).ConfigureAwait(false);
            var (fileName, arguments) = LaunchCommand(platform, script, Environment.GetEnvironmentVariable("PATH"));
            ProcessStartInfo start = new(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {fileName}");
            // Terminal launchers return quickly; one that blocks until its window closes is still a success.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    string details = (await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
                    throw new InvalidOperationException($"{fileName} exited with {process.ExitCode}: {details}");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Still running after the grace period: the window is open.
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            await error.WriteLineAsync($"{NeedsTerminalMessage} (could not open a terminal window: {exception.Message})").ConfigureAwait(false);
            return 2;
        }

        await output.WriteLineAsync(HandedOffMessage).ConfigureAwait(false);
        return HandedOffExitCode;
    }

    static IEnumerable<KeyValuePair<string, string>> Exported(IEnumerable<KeyValuePair<string, string?>> environment)
        => environment.Where(setting => setting.Value is not null && !ExcludedVariables.Contains(setting.Key))
            .Select(setting => new KeyValuePair<string, string>(setting.Key, setting.Value!))
            .OrderBy(setting => setting.Key, StringComparer.Ordinal);

    static string? FindOnPath(string command, string? searchPath)
        => (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(File.Exists);

    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal static string PowerShellQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ShellNameRegex();
}
