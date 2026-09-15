using System.Text.RegularExpressions;
using Hex1b;
using Hex1b.Automation;

namespace Agentic.PackageFixtures;

static class RecordedTerminal
{
    internal static bool Supported => !OperatingSystem.IsWindows() && File.Exists("/bin/bash");

    internal static async Task<string> RunAsync(FixtureWorkspace workspace, string executable, IReadOnlyList<string> arguments,
        string recordingPath, Func<Hex1bTerminalAutomator, Task> interact)
    {
        FixtureFiles.Require(Supported, "PTY automation requires Bash on macOS/Linux.");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(recordingPath)!);
        // Hex1b 0.165's Unix native PTY uses the inherited native environment, ignoring
        // managed overrides. Apply them before any command using a private Bash startup file.
        string startup = Path.Combine(workspace.Root, "terminal-environment-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Recorded terminal requires Unix Bash.");
        using (FileStream stream = new(startup, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        }))
        using (StreamWriter writer = new(stream))
        {
            await writer.WriteLineAsync("set +x +v; unset HISTFILE").ConfigureAwait(false);
            foreach (var (name, value) in workspace.Environment)
            {
                FixtureFiles.Require(name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'), "Invalid fixture environment key.");
                await writer.WriteLineAsync("export " + name + "=" + Quote(value)).ConfigureAwait(false);
            }
            await writer.WriteLineAsync("cd -- " + Quote(workspace.Target) + " || exit 1").ConfigureAwait(false);
            await writer.WriteLineAsync("rm -- " + Quote(startup)).ConfigureAwait(false);
        }
        var terminal = Hex1bTerminal.CreateBuilder().WithHeadless().WithDimensions(260, 220)
            .WithPtyProcess(options =>
            {
                options.FileName = "/bin/bash";
                options.Arguments = ["--noprofile", "--rcfile", startup, "-i"];
                options.WorkingDirectory = workspace.Target;
                options.Environment = workspace.Environment;
            })
            .WithAsciinemaRecording(recordingPath, new AsciinemaRecorderOptions { Title = Path.GetFileNameWithoutExtension(recordingPath), Command = "agentic-check", IdleTimeLimit = 1 }).Build();
        await using (terminal.ConfigureAwait(false))
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(20));
            var run = terminal.RunAsync(timeout.Token);
            var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromMinutes(5));
            string sentinel = "__PACKAGE_DONE_" + Guid.NewGuid().ToString("N");
            try
            {
                await auto.TypeAsync($"unset {string.Join(' ', RealProcess.GitStateVariables)}; {Quote(executable)} {string.Join(' ', arguments.Select(Quote))}; printf '\\n{sentinel}:%s__\\n' \"$?\"").ConfigureAwait(false);
                await auto.EnterAsync().ConfigureAwait(false);
                await interact(auto).ConfigureAwait(false);
                string completionPattern = Regex.Escape(sentinel) + @":([0-9]+)__";
                await auto.WaitUntilAsync(snapshot => Regex.IsMatch(snapshot.GetScreenText(), completionPattern, RegexOptions.CultureInvariant),
                    timeout: TimeSpan.FromMinutes(15), description: "packaged CLI to exit").ConfigureAwait(false);
                using var snapshot = auto.CreateSnapshot();
                string screen = snapshot.GetScreenText();
                string exitCode = Regex.Match(screen, completionPattern, RegexOptions.CultureInvariant).Groups[1].Value;
                FixtureFiles.Require(exitCode == "0", $"Recorded packaged CLI exited {exitCode}:\n{screen}");
            }
            finally
            {
                try
                {
                    await auto.TypeAsync("exit").ConfigureAwait(false);
                    await auto.EnterAsync().ConfigureAwait(false);
                    _ = await run.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
                {
                    // Preserve the original assertion/automation failure; disposal stops the PTY process tree.
                }
                finally
                {
                    await timeout.CancelAsync().ConfigureAwait(false);
                }
            }
        }
        FixtureFiles.Require(new FileInfo(recordingPath).Length > 0, "Missing terminal recording.");
        return await File.ReadAllTextAsync(recordingPath).ConfigureAwait(false);
    }

    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
