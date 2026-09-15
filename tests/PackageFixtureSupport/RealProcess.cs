using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Agentic.PackageFixtures;

sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    internal void RequireSuccess(string operation) => FixtureFiles.Require(ExitCode == 0, $"{operation}: exit {ExitCode}\n{Output}\n{Error}");
}

sealed class RealProcess(IReadOnlyDictionary<string, string>? environment = null)
{
    internal static IReadOnlyList<string> GitStateVariables { get; } = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CONFIG_COUNT"];
    internal async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string directory, string? input = null, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo info = new(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        foreach (string name in GitStateVariables)
            _ = info.Environment.Remove(name);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                info.Environment[key] = value;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using Process process = new() { StartInfo = info };
        _ = process.Start();
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync((input ?? string.Empty).AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    internal async Task<string> SuccessAsync(string executable, IEnumerable<string> arguments, string directory, string? input = null)
    {
        var result = await RunAsync(executable, arguments, directory, input).ConfigureAwait(false);
        result.RequireSuccess(executable);
        return result.Output.TrimEnd();
    }
}

sealed class FixtureWorkspace : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("agentic-fixture-").FullName;
    internal string Target => Path.Combine(Root, "target");
    internal string Feed => Path.Combine(Root, "feed");
    internal string ToolDirectory => Path.Combine(Root, "tools");
    internal Dictionary<string, string> Environment { get; }
    internal RealProcess Process => new(Environment);

    internal FixtureWorkspace()
    {
        foreach (string name in new[] { "target", "feed", "tools", "home", "gh", "config", "cli", "cache" })
            _ = Directory.CreateDirectory(Path.Combine(Root, name));
        Environment = new(StringComparer.Ordinal)
        {
            ["HOME"] = Path.Combine(Root, "home"), ["USERPROFILE"] = Path.Combine(Root, "home"),
            ["XDG_CONFIG_HOME"] = Path.Combine(Root, "config"), ["XDG_CACHE_HOME"] = Path.Combine(Root, "cache"),
            ["GH_CONFIG_DIR"] = Path.Combine(Root, "gh"), ["GH_HOST"] = "github.com", ["GH_PROMPT_DISABLED"] = "1", ["GH_PAGER"] = "cat",
            // gh telemetry can write its device ID after the command exits, racing workspace cleanup.
            ["GH_TELEMETRY"] = "0",
            ["NUGET_PACKAGES"] = Path.Combine(Root, "packages"), ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "nuget-http"),
            ["DOTNET_CLI_HOME"] = Path.Combine(Root, "cli"), ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "gitconfig"),
            ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_AUTHOR_NAME"] = "Fixture",
            ["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid", ["GIT_COMMITTER_NAME"] = "Fixture", ["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid",
            ["AGENTIC_CHECK_CACHE_DIR"] = Path.Combine(Root, "source-cache"), ["AGENTIC_CHECK_CACHE_SECONDS"] = "0", ["TERM"] = "xterm-256color"
        };
    }

    internal async Task InitializeAsync(string? source = null)
    {
        if (source is not null)
            FixtureFiles.Copy(source, Target);
        _ = await Process.SuccessAsync("git", ["init", "--initial-branch=fixture"], Target).ConfigureAwait(false);
        await FixtureAuthentication.Shared.ApplyAsync(Environment).ConfigureAwait(false);
        SetFeed();
    }

    internal void SetFeed()
    {
        XDocument config = new(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "exact-candidates"), new XAttribute("value", Feed))),
            new XElement("fallbackPackageFolders", new XElement("clear"))));
        File.WriteAllText(Path.Combine(Root, "NuGet.Config"), config.ToString());
    }

    internal void AddPackage(PackageArtifact package)
    {
        package.Verify();
        File.Copy(package.Path, Path.Combine(Feed, Path.GetFileName(package.Path)), false);
    }

    internal async Task<string> InstallCheckAsync(PackageArtifact package)
    {
        AddPackage(package);
        _ = await Process.SuccessAsync("dotnet", ["tool", "install", package.Id, "--version", package.Version, "--tool-path", ToolDirectory,
            "--configfile", Path.Combine(Root, "NuGet.Config"), "--no-cache"], Target).ConfigureAwait(false);
        string installedPackage = Directory.GetFiles(ToolDirectory, "*.nupkg", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(path).Equals($"{package.Id}.{package.Version}.nupkg", StringComparison.OrdinalIgnoreCase));
        FixtureFiles.Require(FixtureFiles.Hash(installedPackage) == package.Sha256, "SDK installed different Agentic.Check bytes.");
        return Path.Combine(ToolDirectory, OperatingSystem.IsWindows() ? "agentic-check.exe" : "agentic-check");
    }

    public void Dispose() => Directory.Delete(Root, true);
}
