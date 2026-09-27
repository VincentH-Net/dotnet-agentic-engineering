using System.Globalization;
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
            string name = Path.GetFileNameWithoutExtension(recordingPath);
            try
            {
                ProgressLog.Append($"{name}: terminal started");
                await auto.TypeAsync($"unset {string.Join(' ', RealProcess.GitStateVariables)}; {Quote(executable)} {string.Join(' ', arguments.Select(Quote))}; printf '\\n{sentinel}:%s__\\n' \"$?\"").ConfigureAwait(false);
                await auto.EnterAsync().ConfigureAwait(false);
                await interact(auto).ConfigureAwait(false);
                ProgressLog.Append($"{name}: keys sent, waiting for the CLI to exit");
                string completionPattern = Regex.Escape(sentinel) + @":([0-9]+)__";
                string screen = await WaitForExitAsync(auto, completionPattern, name, timeout.Token).ConfigureAwait(false);
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

    // A real apply keeps redrawing its progress, so a screen that stops changing means the CLI waits for
    // input that will never come. Failing after IdleLimit instead of the ceiling saves most of a long
    // timeout, and a heartbeat every HeartbeatInterval shows that a slow install is still moving.
    static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(90);
    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    static readonly TimeSpan ExitCeiling = TimeSpan.FromMinutes(15);

    static async Task<string> WaitForExitAsync(Hex1bTerminalAutomator auto, string completionPattern, string name, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var lastChange = started;
        var lastHeartbeat = started;
        string lastScreen = string.Empty;
        while (true)
        {
            string screen;
            using (var snapshot = auto.CreateSnapshot())
            {
                screen = snapshot.GetScreenText();
            }

            if (Regex.IsMatch(screen, completionPattern, RegexOptions.CultureInvariant))
            {
                ProgressLog.Append($"{name}: CLI exited after {Elapsed(started)}");
                return screen;
            }

            var now = DateTimeOffset.UtcNow;
            if (screen != lastScreen)
            {
                lastScreen = screen;
                lastChange = now;
            }
            else if (now - lastChange > IdleLimit)
            {
                ProgressLog.Append($"{name}: no screen change for {Elapsed(lastChange)}, giving up");
                throw new TimeoutException(string.Create(CultureInfo.InvariantCulture,
                    $"The packaged CLI produced no output for {IdleLimit.TotalSeconds:0} seconds and did not exit; it is probably waiting for input.\n{screen}"));
            }

            if (now - started > ExitCeiling)
            {
                ProgressLog.Append($"{name}: still running after {Elapsed(started)}, giving up");
                throw new TimeoutException(string.Create(CultureInfo.InvariantCulture,
                    $"The packaged CLI did not exit within {ExitCeiling.TotalMinutes:0} minutes.\n{screen}"));
            }

            if (now - lastHeartbeat >= HeartbeatInterval)
            {
                lastHeartbeat = now;
                ProgressLog.Append($"{name}: running for {Elapsed(started)}, screen last changed {Elapsed(lastChange)} ago");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    static string Elapsed(DateTimeOffset since)
        => (DateTimeOffset.UtcNow - since).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    internal static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
