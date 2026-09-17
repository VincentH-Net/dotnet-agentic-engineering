using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Agentic.Check;
using Hex1b;
using Hex1b.Automation;
using Xunit.Abstractions;

namespace Agentic.LiveTests;

public sealed class LocalPackageTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NestedManifestPreservesParentToolsAndUnrelatedTargetEntries()
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        workspace.AddPackage("2.3.0");
        workspace.AddPackage("1.0.0", "Fixture.Other", "other");
        string parentManifest = CompanionInstaller.ManifestPath(workspace.Target);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(parentManifest)!);
        await File.WriteAllTextAsync(parentManifest, "{\"version\":1,\"isRoot\":true,\"tools\":{}}").ConfigureAwait(true);
        await workspace.RunAsync("dotnet", ["tool", "install", "Fixture.Other", "--tool-manifest", parentManifest, "--version", "1.0.0"]).ConfigureAwait(true);
        string parent = await File.ReadAllTextAsync(parentManifest).ConfigureAwait(true);
        string child = Directory.CreateDirectory(Path.Combine(workspace.Target, "child")).FullName;
        var installed = await new CompanionInstaller(workspace.Runner).EnsureAsync(child, ToolVersion.ParseMinimum("2.3"), false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(installed.Success, installed.Error);
        Assert.Equal(parent, await File.ReadAllTextAsync(parentManifest).ConfigureAwait(true));
        var other = await workspace.Runner.RunAsync("dotnet", ["other", "--help"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(other.Success, other.StandardError);
        var parentCompanion = await new CompanionInstaller(workspace.Runner).EnsureAsync(workspace.Target, ToolVersion.ParseMinimum("2.3"), false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(parentCompanion.Success, parentCompanion.Error);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(parentManifest).ConfigureAwait(true));
        Assert.True(manifest.RootElement.GetProperty("isRoot").GetBoolean());
        Assert.Equal("1.0.0", manifest.RootElement.GetProperty("tools").GetProperty("fixture.other").GetProperty("version").GetString());
    }

    [Fact]
    public async Task RealSdkResolvesChannelsUpdatesDowngradesAndRestoresExactManifest()
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        int minor = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 900000);
        string stable = string.Create(CultureInfo.InvariantCulture, $"2.{minor}.0");
        string preview = string.Create(CultureInfo.InvariantCulture, $"2.{minor + 1}.0-preview.1");
        string future = string.Create(CultureInfo.InvariantCulture, $"2.{minor + 2}.0");
        string wrongMajor = string.Create(CultureInfo.InvariantCulture, $"3.{minor}.0");
        workspace.AddPackage(stable);
        workspace.AddPackage(preview);
        CompanionInstaller installer = new(workspace.Runner);
        var required = ToolVersion.ParseMinimum("2.3");
        var first = await installer.EnsureAsync(workspace.Target, required, false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(first.Success, first.Error);
        Assert.Equal(stable, first.ResolvedVersion);
        using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(first.ManifestPath).ConfigureAwait(true)))
        {
            Assert.False(manifest.RootElement.GetProperty("isRoot").GetBoolean());
        }

        var pre = await installer.EnsureAsync(workspace.Target, required, true, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(pre.Success, pre.Error);
        Assert.Equal(preview, pre.ResolvedVersion);
        var stableAgain = await installer.EnsureAsync(workspace.Target, required, false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(stableAgain.Success, stableAgain.Error);
        Assert.Equal(stable, stableAgain.ResolvedVersion);
        workspace.AddPackage(future);
        var updated = await installer.EnsureAsync(workspace.Target, required, false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(updated.Success, updated.Error);
        Assert.Equal(future, updated.ResolvedVersion);
        File.Delete(Path.Combine(workspace.Feed, $"InnoWvate.Agentic.{future}.nupkg"));
        var downgrade = await installer.EnsureAsync(workspace.Target, required, false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(downgrade.Success, downgrade.Error);
        Assert.Equal(stable, downgrade.ResolvedVersion);
        workspace.AddPackage(wrongMajor);
        await workspace.RunAsync("dotnet", ["tool", "update", CompanionDependency.PackageId, "--tool-manifest", first.ManifestPath, "--version", wrongMajor, "--allow-downgrade"]).ConfigureAwait(true);
        var repaired = await installer.EnsureAsync(workspace.Target, required, false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(repaired.Success, repaired.Error);
        Assert.Equal(stable, repaired.ResolvedVersion);
        Directory.Delete(workspace.Packages, recursive: true);
        Directory.Delete(workspace.CliHome, recursive: true);
        var restored = await installer.EnsureAsync(workspace.Target, required, false, true, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(restored.Success, restored.Error);
        Assert.Equal(stable, restored.ResolvedVersion);
        Assert.False(restored.Changed);
        string child = Directory.CreateDirectory(Path.Combine(workspace.Target, "child")).FullName;
        var help = await workspace.Runner.RunAsync("dotnet", ["agentic", "prompt-log", "show", "--help", "-m", "2.3"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(help.Success, help.StandardError);
        Assert.Contains("--minver", help.StandardOutput, StringComparison.Ordinal);
        var tooHigh = await installer.EnsureAsync(workspace.Target, ToolVersion.ParseMinimum(string.Create(CultureInfo.InvariantCulture, $"2.{minor + 10}")), false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.False(tooHigh.Success);
        Assert.Equal(stable, tooHigh.ResolvedVersion);
        output.WriteLine($"SDK resolution verified with isolated fixture versions {stable}, {preview}, {future}, {wrongMajor}.");
    }

    static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    [Fact]
    public async Task PackagedCompanionAcceptsRawStdioWithoutEntryFiles()
    {
        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        workspace.AddPackage("2.3.0");
        var installed = await new CompanionInstaller(workspace.Runner).EnsureAsync(workspace.Target, ToolVersion.ParseMinimum("2.3"), false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(installed.Success, installed.Error);
        string child = Directory.CreateDirectory(Path.Combine(workspace.Target, "child")).FullName;
        string[] before = Directory.GetFiles(workspace.Target, "*", SearchOption.AllDirectories);
        string large = new('x', 150000);
        string body = "[red]literal[/] \"quotes\" 漢字 😀\n\nQ: preserve $(touch unexpected)?\nA: yes\n\n" + large + "\nprompt-log:\nprompt-log-end:\n\\prompt-log-end:\n\"prompt-log-end:\"\n\n";
        string expected = "prompt-log:\nprompt-log-format: raw-v1\n[red]literal[/] \"quotes\" 漢字 😀\n\nQ: preserve $(touch unexpected)?\nA: yes\n\n" + large + "\n\\prompt-log:\n\\prompt-log-end:\n\\\\prompt-log-end:\n\"prompt-log-end:\"\n\n\nprompt-log-end:\n";
        var wrapped = await workspace.Runner.RunWithInputAsync("dotnet", ["agentic", "prompt-log", "wrap", "--input", "-", "-m", "2.3"], child, body).ConfigureAwait(true);
        Assert.True(wrapped.Success, wrapped.StandardError);
        Assert.Empty(wrapped.StandardError);
        Assert.Equal(expected, wrapped.StandardOutput);
        Assert.Equal(before.Order(StringComparer.Ordinal), Directory.GetFiles(workspace.Target, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(child, "unexpected")));

        // Fixture commits are agent-controlled; production wrap remains independent of Git.
        await workspace.RunAsync("git", ["init"]).ConfigureAwait(true);
        var committed = await workspace.Runner.RunWithInputAsync("git", ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "--cleanup=verbatim", "--file", "-"], child, "Fixture subject\n\nFixture body\n\n" + wrapped.StandardOutput + "\nReviewed-by: Fixture\n").ConfigureAwait(true);
        Assert.True(committed.Success, committed.StandardError);
        var identity = await workspace.Runner.RunAsync("git", ["show", "-s", "--format=%H %cI"], child, CancellationToken.None).ConfigureAwait(true);
        var shown = await workspace.Runner.RunAsync("dotnet", ["agentic", "prompt-log", "show", "-m", "2.3"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(shown.Success, shown.StandardError);
        Assert.Equal(identity.StandardOutput.TrimEnd() + ": valid prompt log" + Environment.NewLine + body + Environment.NewLine + Environment.NewLine, shown.StandardOutput);
        var check = await workspace.Runner.RunAsync("dotnet", ["agentic", "prompt-log", "check", "-m", "2.3"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(check.Success, check.StandardError);

        var cleaned = await workspace.Runner.RunWithInputAsync("git", ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.cleanup=default", "commit", "--allow-empty", "--file", "-"], child, "Normal cleanup\n\n" + wrapped.StandardOutput).ConfigureAwait(true);
        Assert.True(cleaned.Success, cleaned.StandardError);
        var cleanedIdentity = await workspace.Runner.RunAsync("git", ["show", "-s", "--format=%H %cI"], child, CancellationToken.None).ConfigureAwait(true);
        var cleanedHistory = await workspace.Runner.RunAsync("dotnet", ["agentic", "prompt-log", "show", "-m", "2.3"], child, CancellationToken.None).ConfigureAwait(true);
        Assert.True(cleanedHistory.Success, cleanedHistory.StandardError);
        // Git collapses the final run of blank lines; show returns that stored text.
        Assert.EndsWith(cleanedIdentity.StandardOutput.TrimEnd() + ": valid prompt log" + Environment.NewLine + body[..^1] + Environment.NewLine + Environment.NewLine, cleanedHistory.StandardOutput, StringComparison.Ordinal);

        // Explicit stdout, file output, and empty replacement are exercised through real processes.
        var empty = await workspace.Runner.RunWithInputAsync("dotnet", ["agentic", "prompt-log", "wrap", "--input", "-", "--prompt-log", "-", "-m", "2.3"], child, string.Empty).ConfigureAwait(true);
        Assert.True(empty.Success, empty.StandardError);
        Assert.Empty(empty.StandardOutput);
        string destination = Path.Combine(child, "block.txt");
        var file = await workspace.Runner.RunWithInputAsync("dotnet", ["agentic", "prompt-log", "wrap", "--input", "-", "--prompt-log", destination, "-m", "2.3"], child, body).ConfigureAwait(true);
        Assert.True(file.Success, file.StandardError);
        Assert.Empty(file.StandardOutput);
        Assert.Equal(expected, await File.ReadAllTextAsync(destination).ConfigureAwait(true));
        var cleared = await workspace.Runner.RunWithInputAsync("dotnet", ["agentic", "prompt-log", "wrap", "--input", "-", "--prompt-log", destination, "-m", "2.3"], child, string.Empty).ConfigureAwait(true);
        Assert.True(cleared.Success, cleared.StandardError);
        Assert.Empty(await File.ReadAllBytesAsync(destination).ConfigureAwait(true));
    }

    [Fact]
    public async Task PackagedCompanionRunsInRecordedTerminalFromLocalManifestAndChildFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using PackageWorkspace workspace = new();
        await workspace.PrepareAsync().ConfigureAwait(true);
        workspace.AddPackage("2.3.0");
        var installed = await new CompanionInstaller(workspace.Runner).EnsureAsync(workspace.Target, ToolVersion.ParseMinimum("2.3"), false, false, false, CancellationToken.None).ConfigureAwait(true);
        Assert.True(installed.Success, installed.Error);
        await workspace.RunAsync("git", ["init"]).ConfigureAwait(true);
        const string raw = "[red]literal[/] quotes \" \\ 漢字\n\nQ: Keep raw text?\nA: Yes.\n\nprompt-log-end:\n\\prompt-log-end:\nlast\n";
        await File.WriteAllTextAsync(Path.Combine(workspace.Target, "log.txt"), raw.Replace("\n", "\r\n", StringComparison.Ordinal)).ConfigureAwait(true);
        string recording = Path.Combine(workspace.Checkout, "tests/Agentic.LiveTests/TestResults/recordings", $"companion-{Guid.NewGuid():N}.cast");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(recording)!);
        output.WriteLine($"Hex1b recording: {recording}");
        await using (var terminal = Hex1bTerminal.CreateBuilder().WithHeadless().WithDimensions(180, 70)
            .WithPtyProcess(options =>
            {
                options.FileName = "/bin/bash";
                options.Arguments = ["--noprofile", "--norc", "-i"];
                options.WorkingDirectory = workspace.Target;
                options.Environment = workspace.Environment;
            })
            .WithAsciinemaRecording(recording, new AsciinemaRecorderOptions { Title = "Packaged dotnet agentic", Command = "dotnet agentic", IdleTimeLimit = 1 }).Build())
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(90));
            var run = terminal.RunAsync(cancellation.Token);
            Hex1bTerminalAutomator auto = new(terminal, defaultTimeout: TimeSpan.FromSeconds(20));
            try
            {
                string exports = string.Join(" ", workspace.Environment.Select(pair => pair.Key + "=" + Quote(pair.Value)));
                _ = await CommandAsync("export " + exports, 0).ConfigureAwait(true);
                string help = await CommandAsync("dotnet agentic --help", 0).ConfigureAwait(true);
                Assert.Contains("--minver", help, StringComparison.Ordinal);
                _ = await CommandAsync("dotnet agentic prompt-log wrap --input log.txt --prompt-log block.txt -m 2.3", 0).ConfigureAwait(true);
                string wrapped = await CommandAsync("dotnet agentic prompt-log wrap --input - --minver 2.3 < log.txt", 0).ConfigureAwait(true);
                Assert.Contains("prompt-log-format: raw-v1", wrapped, StringComparison.Ordinal);
                Assert.Contains("\\prompt-log-end:", wrapped, StringComparison.Ordinal);
                string block = await File.ReadAllTextAsync(Path.Combine(workspace.Target, "block.txt")).ConfigureAwait(true);
                Assert.Equal("prompt-log:\nprompt-log-format: raw-v1\n[red]literal[/] quotes \" \\ 漢字\n\nQ: Keep raw text?\nA: Yes.\n\n\\prompt-log-end:\n\\\\prompt-log-end:\nlast\n\nprompt-log-end:\n", block);
                await File.WriteAllTextAsync(Path.Combine(workspace.Target, "message.txt"), "Fixture commit\n\n" + block + "\nReviewed-by: Fixture\n").ConfigureAwait(true);
                _ = await CommandAsync("git -c user.name=Fixture -c user.email=fixture@example.invalid -c commit.cleanup=default commit --allow-empty -F message.txt", 0).ConfigureAwait(true);
                string shown = await CommandAsync("dotnet agentic prompt-log show -m 2.3", 0).ConfigureAwait(true);
                Assert.Contains("[red]literal[/]", shown, StringComparison.Ordinal);
                Assert.Contains("A: Yes.", shown, StringComparison.Ordinal);
                Assert.Contains("valid prompt log", shown, StringComparison.Ordinal);
                _ = await CommandAsync("dotnet agentic prompt-log check -m 2.3", 0).ConfigureAwait(true);
                await File.WriteAllTextAsync(Path.Combine(workspace.Target, "old-message.txt"), "Historical JSON\n\nprompt-log:\n\"old first line\"\n\"\"\n\"old last line\"\n\nprompt-log-end:\n").ConfigureAwait(true);
                _ = await CommandAsync("git -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty --cleanup=verbatim -F old-message.txt", 0).ConfigureAwait(true);
                string historical = await CommandAsync("dotnet agentic prompt-log show -m 2.3", 0).ConfigureAwait(true);
                Assert.Contains("legacy prompt log (JSON)", historical, StringComparison.Ordinal);
                Assert.Contains("old last line", historical, StringComparison.Ordinal);
                _ = await CommandAsync("mkdir child && cd child && dotnet agentic --minver 2.3 prompt-log check", 0).ConfigureAwait(true);
                _ = await CommandAsync("dotnet agentic prompt-log wrap --unknown", 2).ConfigureAwait(true);
                string incompatible = await CommandAsync("dotnet agentic prompt-log wrap --input ../log.txt --prompt-log forbidden.txt -m 3.0", 1).ConfigureAwait(true);
                Assert.Contains("incompatible", incompatible, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(workspace.Target, "child/forbidden.txt")));
                _ = await CommandAsync("dotnet agentic prompt-log check --commit absent -m 2.3", 1).ConfigureAwait(true);
            }
            finally
            {
                await auto.TypeAsync("exit").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
                _ = await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }

            async Task<string> CommandAsync(string command, int code)
            {
                string sentinel = "DONE_" + Guid.NewGuid().ToString("N");
                await auto.TypeAsync($"{command}; printf '\\n{sentinel}:%s\\n' \"$?\"").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync($"{sentinel}:{code}").ConfigureAwait(true);
                using var snapshot = auto.CreateSnapshot();
                return snapshot.GetScreenText();
            }
        }

        Assert.True(new FileInfo(recording).Length > 100);
    }
}

sealed class PackageWorkspace : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("agentic-packages-").FullName;
    string basePackage = string.Empty;

    internal string Checkout { get; } = FindCheckout();
    internal string Target => Path.Combine(root, "target");
    internal string Feed => Path.Combine(root, "feed");
    internal string Packages => Path.Combine(root, "packages");
    internal string CliHome => Path.Combine(root, "cli");
    internal IsolatedRunner Runner => new(Environment);

    internal Dictionary<string, string> Environment => new(StringComparer.Ordinal)
    {
        ["NUGET_PACKAGES"] = Packages,
        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache"),
        ["DOTNET_CLI_HOME"] = CliHome,
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "empty-gitconfig"),
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["TERM"] = "xterm-256color"
    };

    internal async Task PrepareAsync()
    {
        _ = Directory.CreateDirectory(Target);
        _ = Directory.CreateDirectory(Feed);
        string baseDirectory = Path.Combine(root, "base");
        var packed = await new ProcessCommandRunner().RunAsync("dotnet", ["pack", "src/Agentic/Agentic.csproj", "-c", "Release", "-o", baseDirectory], Checkout, CancellationToken.None).ConfigureAwait(false);
        Assert.True(packed.Success, packed.StandardError + packed.StandardOutput);
        basePackage = Directory.GetFiles(baseDirectory, "*.nupkg").Single();

        XDocument config = new(new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", Feed)))));
        await File.WriteAllTextAsync(Path.Combine(Target, "nuget.config"), config.ToString()).ConfigureAwait(false);
    }

    internal void AddPackage(string version, string packageId = CompanionDependency.PackageId, string command = "agentic")
    {
        // Fixture metadata varies; the payload is the real packaged 2.3 companion. Runtime
        // compatibility is independently exercised by CLI invocations, not inferred from NuGet.
        string destination = Path.Combine(Feed, $"{packageId}.{version}.nupkg");
        File.Copy(basePackage, destination);
        using var archive = ZipFile.Open(destination, ZipArchiveMode.Update);
        var nuspec = archive.Entries.Single(entry => entry.Name.EndsWith(".nuspec", StringComparison.Ordinal));
        string name = nuspec.FullName;
        XDocument document;
        using (var stream = nuspec.Open())
        {
            document = XDocument.Load(stream);
        }

        document.Descendants().Single(element => element.Name.LocalName == "version").Value = version;
        document.Descendants().Single(element => element.Name.LocalName == "id").Value = packageId;
        nuspec.Delete();
        using (var replacement = archive.CreateEntry(name).Open())
        {
            document.Save(replacement);
        }

        if (command != "agentic")
        {
            var settings = archive.Entries.Single(entry => entry.Name == "DotnetToolSettings.xml");
            string settingsName = settings.FullName;
            XDocument tool;
            using (var stream = settings.Open())
            {
                tool = XDocument.Load(stream);
            }

            tool.Descendants("Command").Single().SetAttributeValue("Name", command);
            settings.Delete();
            using var replacement = archive.CreateEntry(settingsName).Open();
            tool.Save(replacement);
        }
    }

    internal async Task RunAsync(string command, IReadOnlyList<string> arguments)
    {
        var result = await Runner.RunAsync(command, arguments, Target, CancellationToken.None).ConfigureAwait(false);
        Assert.True(result.Success, result.StandardError + result.StandardOutput);
    }

    static string FindCheckout()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Missing checkout root.");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}

sealed class IsolatedRunner(IReadOnlyDictionary<string, string> environment) : ICommandRunner
{
    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment = null)
        => RunCoreAsync(fileName, arguments, workingDirectory, null, cancellationToken, environment);

    internal Task<CommandResult> RunWithInputAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, string input)
        => RunCoreAsync(fileName, arguments, workingDirectory, input, CancellationToken.None);

    async Task<CommandResult> RunCoreAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, string? input, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        ProcessStartInfo info = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false
        };
        if (input is not null)
        {
            info.StandardInputEncoding = new UTF8Encoding(false);
        }
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                info.Environment[key] = value;
        }

        using Process process = new() { StartInfo = info };
        _ = process.Start();
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdin = input is null ? Task.CompletedTask : WriteInputAsync();
            await Task.WhenAll(stdout, stderr, stdin, process.WaitForExitAsync(cancellationToken)).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        async Task WriteInputAsync()
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }
    }
}
