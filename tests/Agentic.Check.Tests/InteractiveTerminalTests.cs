namespace Agentic.Check.Tests;

public sealed class InteractiveTerminalTests
{
    static readonly KeyValuePair<string, string?>[] Environment =
    [
        new("SHLVL", "3"),
        new("AGENTIC_CHECK_PREVIEW_SOURCE_REF", "abc"),
        new("NUGET_PACKAGES", "/tmp/it's here"),
        new("BAD-NAME", "skipped in sh"),
        new("EMPTY", null)
    ];

    [Fact]
    public async Task HandOffHappensAfterArgumentValidationAndOnlyForInteractiveRuns()
    {
        using TempDirectory temp = new();
        _ = Directory.CreateDirectory(temp.Path);
        int handOffs = 0;
        Task<int> HandOff(CancellationToken _)
        {
            handOffs++;
            return Task.FromResult(InteractiveTerminal.HandedOffExitCode);
        }
        ToolRunner runner = new();
        RecordingReporter reporter = new();
        FakePrompts prompts = new() { Interactive = false };
        CheckWorkflow workflow = new(runner, prompts, reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(), interactiveHandOff: HandOff);

        var invalid = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false, PreviewSourceRef: "feature/x"), CancellationToken.None);
        Assert.Equal(2, invalid.ExitCode);
        Assert.Equal(0, handOffs);
        Assert.Contains("--preview-source-ref", Assert.Single(reporter.Errors), StringComparison.Ordinal);

        var handedOff = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(InteractiveTerminal.HandedOffExitCode, handedOff.ExitCode);
        Assert.Equal(1, handOffs);
        Assert.Empty(runner.Calls);

        var dryRun = await workflow.RunAsync(new(temp.Path, true, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Equal(1, handOffs);
        Assert.NotEmpty(runner.Calls);
    }

    [Fact]
    public void ShellScriptChangesDirectoryExportsEnvironmentAndRunsTheCommand()
    {
        string script = InteractiveTerminal.ShellScript("/repo/it's", ["/usr/local/share/dotnet/dotnet", "/store/agentic.check.dll", "--report", "r's.json"], Environment);
        string[] lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("#!/bin/sh", lines[0]);
        Assert.Equal("cd '/repo/it'\\''s' || exit 1", lines[1]);
        Assert.Equal("export AGENTIC_CHECK_PREVIEW_SOURCE_REF='abc'", lines[2]);
        Assert.Equal("export NUGET_PACKAGES='/tmp/it'\\''s here'", lines[3]);
        Assert.Equal("'/usr/local/share/dotnet/dotnet' '/store/agentic.check.dll' '--report' 'r'\\''s.json'", lines[4]);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void PowerShellScriptQuotesValuesAndKeepsOddVariableNames()
    {
        string script = InteractiveTerminal.PowerShellScript(@"C:\repo", [@"C:\Program Files\dotnet\dotnet.exe", @"C:\store\agentic.check.dll"], Environment);
        string[] lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(@"Set-Location -LiteralPath 'C:\repo'", lines[0]);
        Assert.Equal("${env:AGENTIC_CHECK_PREVIEW_SOURCE_REF} = 'abc'", lines[1]);
        Assert.Equal("${env:BAD-NAME} = 'skipped in sh'", lines[2]);
        Assert.Equal("${env:NUGET_PACKAGES} = '/tmp/it''s here'", lines[3]);
        Assert.Equal(@"& 'C:\Program Files\dotnet\dotnet.exe' 'C:\store\agentic.check.dll'", lines[4]);
        Assert.DoesNotContain("SHLVL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RelaunchUsesTheToolDllUnderTheMuxerAndTheExecutableUnderAnAppHost()
    {
        Assert.Equal(["/dotnet/dotnet", "/store/agentic.check.dll", "--preview"],
            InteractiveTerminal.RelaunchCommand("/dotnet/dotnet", ["/store/agentic.check.dll"], ["--preview"]));
        Assert.Equal(["/tools/agentic-check", "--preview"],
            InteractiveTerminal.RelaunchCommand("/tools/agentic-check", ["/tools/agentic-check"], ["--preview"]));
    }

    [Fact]
    public void DesktopIsNeverAssumedUnderCiOrWithoutADisplay()
    {
        Assert.False(InteractiveTerminal.DesktopAvailable(HostPlatform.MacOS, name => name == "CI" ? "true" : null));
        Assert.True(InteractiveTerminal.DesktopAvailable(HostPlatform.MacOS, _ => null));
        Assert.False(InteractiveTerminal.DesktopAvailable(HostPlatform.Windows, _ => null, userInteractive: false));
        Assert.True(InteractiveTerminal.DesktopAvailable(HostPlatform.Windows, _ => null));
        Assert.False(InteractiveTerminal.DesktopAvailable(HostPlatform.Linux, _ => null));
        Assert.True(InteractiveTerminal.DesktopAvailable(HostPlatform.Linux, name => name == "WAYLAND_DISPLAY" ? "wayland-0" : null));
    }

    [Fact]
    public void LaunchCommandsSourceTheScriptPerPlatform()
    {
        var (macFile, macArguments) = InteractiveTerminal.LaunchCommand(HostPlatform.MacOS, "/tmp/a \"b\".sh", null);
        Assert.Equal("osascript", macFile);
        Assert.Equal("do script \"source '/tmp/a \\\"b\\\".sh'\"", macArguments[5]);
        var (windowsFile, windowsArguments) = InteractiveTerminal.LaunchCommand(HostPlatform.Windows, @"C:\t\a.ps1", null);
        Assert.Equal("cmd.exe", windowsFile);
        Assert.Equal(["/c", "start", "Agentic.Check", "powershell", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", @"C:\t\a.ps1"], windowsArguments);

        using TempDirectory temp = new();
        string bin = temp.CreateDirectory("bin");
        File.WriteAllText(Path.Combine(bin, "xterm"), string.Empty);
        var (linuxFile, linuxArguments) = InteractiveTerminal.LaunchCommand(HostPlatform.Linux, "/tmp/a.sh", bin);
        Assert.Equal(Path.Combine(bin, "xterm"), linuxFile);
        Assert.Equal(["-e", "sh", "-c", ". '/tmp/a.sh'; exec \"${SHELL:-sh}\""], linuxArguments);
        var missing = Assert.Throws<InvalidOperationException>(() => InteractiveTerminal.LaunchCommand(HostPlatform.Linux, "/tmp/a.sh", temp.CreateDirectory("empty")));
        Assert.Contains("no terminal emulator found", missing.Message, StringComparison.Ordinal);
    }
}
