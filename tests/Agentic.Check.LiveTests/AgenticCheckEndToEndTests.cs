using System.Diagnostics;
using Hex1b;
using Hex1b.Automation;

namespace Agentic.Check.LiveTests;

public sealed class AgenticCheckEndToEndTests
{
    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DryRunDisplaysSummaryAndWouldActions()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync().ConfigureAwait(true);
        await using var terminal = CreateTerminal(workspace);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var runTask = terminal.RunAsync(cancellation.Token);
        var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

        try
        {
            await RunCommandAsync(auto, workspace, AgenticCheckCommand(workspace, $"--dry-run {Quote(workspace.RepoPath)}")).ConfigureAwait(true);

            using var snapshot = auto.CreateSnapshot();
            string screen = snapshot.GetScreenText();
            Assert.Contains("Recommended directives", screen, StringComparison.Ordinal);
            Assert.Contains("Recommended skills", screen, StringComparison.Ordinal);
            Assert.Contains("Would install directives into AGENTS.md:", screen, StringComparison.Ordinal);
            Assert.Contains("Would install skills into repo skills directories:", screen, StringComparison.Ordinal);
            Assert.Contains("__AGENTIC_DONE:0__", screen, StringComparison.Ordinal);
        }
        finally
        {
            await StopShellAsync(auto, runTask).ConfigureAwait(true);
        }
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveRecommendationListAcceptsKeyboardInput()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync().ConfigureAwait(true);
        await using var terminal = CreateTerminal(workspace);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var runTask = terminal.RunAsync(cancellation.Token);
        var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

        try
        {
            await auto.TypeAsync($"{AgenticCheckCommand(workspace, Quote(workspace.RepoPath))}; printf '\\n__AGENTIC_DONE:%s__\\n' \"$?\"").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
            await auto.WaitUntilTextAsync("select which to apply:", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            await auto.LeftAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[ ] dotnet-cli-run", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);

            await auto.WaitUntilTextAsync("__AGENTIC_DONE:0__", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
            using var snapshot = auto.CreateSnapshot();
            string screen = snapshot.GetScreenText();
            Assert.Contains("__AGENTIC_DONE:0__", screen, StringComparison.Ordinal);
            string agentsContent = File.Exists(Path.Combine(workspace.RepoPath, "AGENTS.md"))
                ? await File.ReadAllTextAsync(Path.Combine(workspace.RepoPath, "AGENTS.md")).ConfigureAwait(true)
                : string.Empty;
            Assert.DoesNotContain("dotnet-agentic-engineering:", agentsContent, StringComparison.Ordinal);
            string[] ghCalls = [.. (await File.ReadAllLinesAsync(workspace.GhLogPath).ConfigureAwait(true))
                .Where(line => !string.IsNullOrWhiteSpace(line))];
            Assert.DoesNotContain(ghCalls, line => line.Contains(" install ", StringComparison.Ordinal));
        }
        finally
        {
            await StopShellAsync(auto, runTask).ConfigureAwait(true);
        }
    }

    static string ToolAssemblyPath => typeof(AgenticCheckCli).Assembly.Location;

    static string AgenticCheckCommand(TestWorkspace workspace, string arguments)
    {
        string path = workspace.BinPath + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        return $"AGENTIC_CHECK_GH_LOG={Quote(workspace.GhLogPath)} PATH={Quote(path)} dotnet {Quote(ToolAssemblyPath)} {arguments}";
    }

    static Hex1bTerminal CreateTerminal(TestWorkspace workspace)
    {
        string path = workspace.BinPath + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        return Hex1bTerminal.CreateBuilder()
            .WithHeadless()
            .WithDimensions(220, 120)
            .WithPtyProcess(options =>
            {
                options.FileName = "/bin/bash";
                options.Arguments = ["--noprofile", "--norc", "-i"];
                options.WorkingDirectory = workspace.RootPath;
                options.Environment = new Dictionary<string, string>
                {
                    ["AGENTIC_CHECK_GH_LOG"] = workspace.GhLogPath,
                    ["GH_PAGER"] = "cat",
                    ["PATH"] = path,
                    ["TERM"] = "xterm-256color"
                };
            })
            .Build();
    }

    static async Task RunCommandAsync(Hex1bTerminalAutomator auto, TestWorkspace workspace, string command)
    {
        await auto.TypeAsync($"{command}; printf '\\n__AGENTIC_DONE:%s__\\n' \"$?\"").ConfigureAwait(true);
        await auto.EnterAsync().ConfigureAwait(true);
        await auto.WaitUntilTextAsync("__AGENTIC_DONE:0__", timeout: TimeSpan.FromSeconds(60)).ConfigureAwait(true);

        using var snapshot = auto.CreateSnapshot();
        string screen = snapshot.GetScreenText();
        Assert.Contains("__AGENTIC_DONE:0__", screen, StringComparison.Ordinal);
        Assert.Contains("skill update --dir", await File.ReadAllTextAsync(workspace.GhLogPath).ConfigureAwait(true), StringComparison.Ordinal);
    }

    static async Task StopShellAsync(Hex1bTerminalAutomator auto, Task runTask)
    {
        try
        {
            await auto.TypeAsync("exit").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
            await runTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    static string Quote(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    static bool IsUnsupportedPlatform()
        => OperatingSystem.IsWindows() || !File.Exists("/bin/bash");

    sealed class TestWorkspace : IDisposable
    {
        TestWorkspace(string rootPath)
        {
            RootPath = rootPath;
            RepoPath = Path.Combine(rootPath, "repo");
            BinPath = Path.Combine(rootPath, "bin");
            GhLogPath = Path.Combine(rootPath, "gh.log");
        }

        public string RootPath { get; }

        public string RepoPath { get; }

        public string BinPath { get; }

        public string GhLogPath { get; }

        public static async Task<TestWorkspace> CreateAsync()
        {
            var workspace = new TestWorkspace(Path.Combine(Path.GetTempPath(), $"agentic-check-e2e-{Guid.NewGuid():N}"));
            _ = Directory.CreateDirectory(workspace.RootPath);
            _ = Directory.CreateDirectory(workspace.RepoPath);
            _ = Directory.CreateDirectory(workspace.BinPath);
            await File.WriteAllTextAsync(workspace.GhLogPath, string.Empty).ConfigureAwait(true);
            await File.WriteAllTextAsync(
                Path.Combine(workspace.RepoPath, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """).ConfigureAwait(true);
            await RunProcessAsync("git", ["init"], workspace.RepoPath).ConfigureAwait(true);
            await WriteFakeGhAsync(workspace).ConfigureAwait(true);
            return workspace;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        static async Task WriteFakeGhAsync(TestWorkspace workspace)
        {
            string ghPath = Path.Combine(workspace.BinPath, "gh");
            await File.WriteAllTextAsync(
                ghPath,
                """
                #!/usr/bin/env bash
                set -euo pipefail

                if [[ -n "${AGENTIC_CHECK_GH_LOG:-}" ]]; then
                  printf '%s\n' "$*" >> "$AGENTIC_CHECK_GH_LOG"
                fi

                if [[ "${1:-}" == "--version" ]]; then
                  echo "gh version 2.93.0 (test)"
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "--help" ]]; then
                  echo "gh skill help"
                  exit 0
                fi

                if [[ "${1:-}" == "skills" && "${2:-}" == "--help" ]]; then
                  echo "gh skills help"
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "update" ]]; then
                  echo "No updates available."
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "install" ]]; then
                  target_dir=""
                  for ((index = 1; index <= $#; index++)); do
                    if [[ "${!index}" == "--dir" ]]; then
                      next=$((index + 1))
                      target_dir="${!next}"
                    fi
                  done

                  if [[ -z "$target_dir" ]]; then
                    echo "missing --dir" >&2
                    exit 2
                  fi

                  skill_name="$(basename "${4:-unknown}")"
                  mkdir -p "$target_dir/$skill_name"
                  printf '# Installed by fake gh\n' > "$target_dir/$skill_name/SKILL.md"
                  echo "Installed ${3:-unknown} ${4:-unknown}"
                  exit 0
                fi

                echo "Unexpected gh invocation: $*" >&2
                exit 2
                """).ConfigureAwait(true);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    ghPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
        }

        static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new()
            {
                StartInfo = startInfo
            };

            _ = process.Start();
            string standardOutput = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
            string standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            Assert.True(
                process.ExitCode == 0,
                $"{fileName} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{standardError}");
        }
    }
}
