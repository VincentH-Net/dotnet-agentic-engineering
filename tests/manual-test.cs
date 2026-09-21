// Prepare an isolated shell for manually testing the local Agentic.Check, InnoWvate.Agentic and
// InnoWvate.Dna builds together with this branch's directives and skills, before anything is published.
// Run from any directory: dotnet run --file /path/to/tests/manual-test.cs [options]
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

string? baselineId = null;
string fixture = "broad-stack";
string? target = null;
bool fresh = false;
bool release = true;
bool openTerminal = true;
bool withGh = true;
bool list = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help" or "-h":
            PrintUsage();
            return 0;
        case "--list":
            list = true;
            break;
        case "--baseline" when i + 1 < args.Length:
            baselineId = args[++i];
            break;
        case "--fixture" when i + 1 < args.Length:
            fixture = args[++i];
            break;
        case "--target" when i + 1 < args.Length:
            target = Path.GetFullPath(args[++i]);
            break;
        case "--fresh":
            fresh = true;
            break;
        case "--debug":
            release = false;
            break;
        case "--no-terminal":
            openTerminal = false;
            break;
        case "--no-gh":
            withGh = false;
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete option: {args[i]}. Use --help for usage.");
            return 2;
    }
}

string checkout = Checkout();
string baselinesRoot = Path.Combine(checkout, "tests", "fixtures", "baselines");
string definitionsRoot = Path.Combine(checkout, "tests", "fixtures", "definitions");
string[] baselineIds = [.. Directory.GetDirectories(baselinesRoot).Select(directory => Path.GetFileName(directory)!).Order(StringComparer.Ordinal)];
if (list)
{
    foreach (string id in baselineIds)
    {
        Say($"Baseline {id} (--baseline {id} --fixture <name>):");
        foreach (string name in CompletedFixtures(id))
            Say($"  {name}{(name == fixture ? "  (default)" : string.Empty)}");
    }
    Say("Fresh trigger content (--fresh --fixture <name>):");
    foreach (string name in Directory.GetDirectories(definitionsRoot).Select(directory => Path.GetFileName(directory)!).Order(StringComparer.Ordinal))
        Say($"  {name}");
    return 0;
}

try
{
    string sha = Git("rev-parse", "HEAD");
    string branch = Git("symbolic-ref", "--quiet", "--short", "HEAD");
    var (remoteCode, remoteOutput) = Capture("git", ["ls-remote", "--exit-code", "origin", "refs/heads/" + branch], checkout);
    string pushed = remoteCode == 0 ? remoteOutput.Split('\t')[0].Trim() : "(none)";
    if (pushed != sha)
    {
        throw Failure($"SOURCE NOT READY: origin/{branch} is at {pushed}, HEAD is {sha}.\n"
            + "Agentic.Check reads directives and skills from GitHub, so push HEAD first and rerun.");
    }
    string content = Git("status", "--porcelain", "--untracked-files=all", "--", "directives", "plugins");
    if (content.Length > 0)
        throw Failure("Directives and skills must be committed and pushed to be tested:\n" + content);
    string sources = Git("status", "--porcelain", "--", "src");

    string configuration = release ? "Release" : "Debug";
    string runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    string run = Path.Combine(checkout, "tests", "TestResults", "manual", runId);
    string feed = Path.Combine(run, "feed");
    string cliHome = Path.Combine(run, "cli-home");
    _ = Directory.CreateDirectory(feed);
    _ = Directory.CreateDirectory(cliHome);
    Say($"Manual test run {runId}: {branch}@{sha[..12]} ({configuration})");
    if (sources.Length > 0)
        Say("Note: src has uncommitted changes; the packed tools include them, the pushed content does not.");

    Say("\nPacking the three tools into an isolated feed...");
    string[] projects = ["src/Agentic.Check/Agentic.Check.csproj", "src/Agentic/Agentic.csproj", "src/Dna/Dna.csproj"];
    foreach (string project in projects)
    {
        if (Run("dotnet", ["pack", project, "-c", configuration, "-o", feed, "-p:RepositoryCommit=" + sha], checkout) != 0)
            throw Failure($"Packing {project} failed.");
    }
    string[] packageIds = ["Agentic.Check", "InnoWvate.Agentic", "InnoWvate.Dna"];
    var packages = packageIds.Select(id =>
    {
        string path = Directory.GetFiles(feed, id + ".*.nupkg").Single();
        return (id, version: Path.GetFileNameWithoutExtension(path)[(id.Length + 1)..], path, sha256: Sha256(path));
    }).ToArray();

    bool existing = target is not null;
    string? source = null;
    if (existing)
    {
        if (!Directory.Exists(target))
            throw Failure($"--target does not exist: {target}");
    }
    else
    {
        target = Path.Combine(Path.GetTempPath(), "agentic-manual", runId, "repo");
        _ = Directory.CreateDirectory(target);
        if (fresh)
        {
            string definition = Path.Combine(definitionsRoot, fixture);
            if (!File.Exists(Path.Combine(definition, "trigger.json")))
                throw Failure($"No fresh definition {fixture}. Use --list.");
            using var trigger = JsonDocument.Parse(File.ReadAllText(Path.Combine(definition, "trigger.json")));
            foreach (var property in trigger.RootElement.EnumerateObject())
            {
                string relative = property.Name;
                string text = property.Value.GetString() ?? string.Empty;
                string file = Path.GetFullPath(Path.Combine(target, relative));
                if (!file.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw Failure($"Trigger path escapes the target: {relative}");
                _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            source = $"fresh trigger content {fixture}";
        }
        else
        {
            baselineId ??= baselineIds.LastOrDefault() ?? throw Failure("No baseline collections found.");
            if (!CompletedFixtures(baselineId).Contains(fixture, StringComparer.Ordinal))
                throw Failure($"Baseline {baselineId} has no completed fixture {fixture}. Use --list.");
            ZipFile.ExtractToDirectory(Path.Combine(baselinesRoot, baselineId, fixture, "snapshot.zip"), target);
            source = $"baseline {baselineId}/{fixture}";
        }
        Say($"\nTest repository from {source}: {target}");
        string[][] gitCommands =
        [
            ["init", "-q"],
            ["add", "-A"],
            ["-c", "user.name=Manual test", "-c", "user.email=manual-test@example.invalid", "commit", "-q", "-m", $"Test repository from {source}"]
        ];
        foreach (string[] git in gitCommands)
        {
            var (code, output) = Capture("git", git, target);
            if (code != 0)
                throw Failure($"git {git[0]} failed in {target}: {output}");
        }
    }

    string nugetConfig = Path.Combine(target!, "NuGet.Config");
    if (existing)
        Say($"\nRefreshing {nugetConfig} to the new feed; the repository's files and history are untouched.");
    new XDocument(new XElement("configuration",
        new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local-builds"), new XAttribute("value", feed))),
        new XElement("fallbackPackageFolders", new XElement("clear")))).Save(nugetConfig);

    string toolsDirectory = Path.Combine(cliHome, ".dotnet", "tools");
    Dictionary<string, string?> environment = new(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_HOME"] = cliHome,
        ["NUGET_PACKAGES"] = Path.Combine(run, "packages"),
        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(run, "http-cache"),
        ["AGENTIC_CHECK_CACHE_DIR"] = Path.Combine(run, "source-cache"),
        ["AGENTIC_CHECK_PREVIEW_SOURCE_REF"] = sha,
        ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0",
        ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    };

    Say("\nInstalling the local dna into the isolated CLI home...");
    if (Run("dotnet", ["tool", "install", "--global", "InnoWvate.Dna", "--configfile", nugetConfig], target!, environment) != 0)
        throw Failure("Installing InnoWvate.Dna from the local feed failed.");
    if (File.Exists(Path.Combine(target!, ".config", "dotnet-tools.json"))
        && Run("dotnet", ["tool", "restore", "--configfile", nugetConfig], target!, environment) != 0)
    {
        Say("Note: dotnet tool restore failed; dna check will offer to repair the local tool.");
    }
    Say("\nCaching the local Agentic.Check for dna check...");
    var (cacheCode, cacheOutput) = Capture("dotnet", ["tool", "exec", "Agentic.Check", "--configfile", nugetConfig, "--", "--version"], target!, environment, "y\n");
    if (cacheCode != 0)
        Say($"Note: caching failed; the first dna check downloads it from the local feed instead.\n{cacheOutput}");

    string activate = Path.Combine(run, "activate.sh");
    string dotnetDirectory = Path.GetDirectoryName(Environment.ProcessPath!)!;
    string path = withGh
        ? $"{Quote(toolsDirectory)}:$PATH"
        : $"{Quote(toolsDirectory)}:{Quote(dotnetDirectory)}:/usr/bin:/bin:/usr/sbin:/sbin";
    string summary = $"Manual test shell {runId}: local Agentic.Check {packages[0].version}, InnoWvate.Agentic {packages[1].version}, InnoWvate.Dna {packages[2].version} ({configuration}).";
    string pinned = $"Directives and skills come from {branch}@{sha[..12]} through AGENTIC_CHECK_PREVIEW_SOURCE_REF.";
    File.WriteAllText(activate, string.Join('\n',
    [
        "# Source this file in a terminal to test the local builds. Only that terminal and its child processes change.",
        .. environment.Select(setting => $"export {setting.Key}={Quote(setting.Value!)}"),
        "unset AGENTIC_CHECK_CACHE_SECONDS",
        $"export PATH={path}",
        $"cd {Quote(target!)} || return",
        $"printf '%s\\n' {Quote(summary)} {Quote(pinned)} {Quote(withGh ? "Try: dna check   dna prompt-log   dnx agentic.check -- -h" : "gh is not on PATH in this shell, to test the missing prerequisite message.")}",
        string.Empty
    ]));

    JsonObject provenance = new()
    {
        ["branch"] = branch,
        ["commit"] = sha,
        ["configuration"] = configuration,
        ["target"] = target,
        ["source"] = source ?? "existing target",
        ["feed"] = feed,
        ["activate"] = activate,
        ["packages"] = new JsonArray([.. packages.Select(package => (JsonNode)new JsonObject
        {
            ["id"] = package.id,
            ["version"] = package.version,
            ["path"] = package.path,
            ["sha256"] = package.sha256
        })])
    };
    File.WriteAllText(Path.Combine(run, "manual-build.json"), provenance.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(run, "README.md"), string.Join('\n',
    [
        $"# Manual test {runId}",
        string.Empty,
        summary,
        pinned,
        string.Empty,
        $"Test repository: `{target}`",
        string.Empty,
        "Activate in any terminal (only that terminal changes):",
        string.Empty,
        "```sh",
        $"source {Quote(activate)}",
        "```",
        string.Empty,
        "Then `dna check`, `dna prompt-log`, `dnx agentic.check -- -h` and agent-started `dotnet agentic` commands all",
        "use the local builds from `feed/` and the pinned content. Your normal global tools, caches, GitHub login and",
        "shell profiles are unchanged. `NuGet.Config` in the test repository lists only the local feed; add nuget.org",
        "there if you need to restore other packages in that repository.",
        string.Empty,
        "Reuse this repository with a newer build: `dotnet run --file tests/manual-test.cs --target " + Quote(target!) + "`.",
        string.Empty
    ]));

    Say($"\n{summary}\n{pinned}\nRun folder: {run}");
    if (openTerminal && OperatingSystem.IsMacOS())
    {
        Say("Opening a Terminal window with the environment active...");
        if (Run("osascript", ["-e", "tell application \"Terminal\"", "-e", "activate", "-e", $"do script \"source {Quote(activate)}\"", "-e", "end tell"], run) != 0)
            Say($"Could not open Terminal. Activate manually:\n  source {Quote(activate)}");
    }
    else
    {
        Say($"Activate in a terminal:\n  source {Quote(activate)}");
    }
    return 0;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static void PrintUsage()
    => Say("""
        Usage: dotnet run --file tests/manual-test.cs [options]

        Packs Agentic.Check, InnoWvate.Agentic and InnoWvate.Dna from the working tree into an isolated feed,
        prepares a test repository, installs the local dna into an isolated .NET CLI home, and opens a
        Terminal window (macOS) whose environment uses only those local builds. Directives and skills are read
        from the pushed HEAD commit, so HEAD must be pushed and directives/plugins must be clean.

        Options:
          --list               List baseline collections, their fixtures, and fresh definitions, then exit.
          --baseline <id>      Baseline collection to unpack (default: the newest collection).
          --fixture <name>     Fixture to unpack or materialize (default: broad-stack).
          --fresh              Use the fixture's trigger content only, for first-install flows.
          --target <path>      Reuse an existing test repository; only its NuGet.Config is rewritten.
          --debug              Pack Debug instead of Release.
          --no-terminal        Do not open a Terminal window; print the activation command instead.
          --no-gh              Leave gh off PATH in the shell, to test the missing prerequisite message.
          -h, --help           Show this help.

        Each run writes tests/TestResults/manual/<run-id>/ with feed/, activate.sh, README.md and manual-build.json.
        Nothing outside that folder, the test repository and the opened terminal is changed.
        """);

string Git(params string[] arguments)
{
    var (code, output) = Capture("git", arguments, checkout);
    return code == 0 ? output.Trim() : throw Failure($"git {arguments[0]} failed: {output.Trim()}");
}

// Console text of this repository-only script is not localized (the documented CA1303 exclusion);
// the parameter name keeps the literal-string analyzer from treating it as localizable UI text.
static void Say(string line) => Console.WriteLine(line);

IReadOnlyList<string> CompletedFixtures(string id)
{
    string collection = Path.Combine(baselinesRoot, id, "collection.json");
    if (!File.Exists(collection))
        throw Failure($"No baseline collection {id}. Use --list.");
    using var document = JsonDocument.Parse(File.ReadAllText(collection));
    return [.. document.RootElement.GetProperty("completed").EnumerateArray().Select(name => name.GetString()!).Order(StringComparer.Ordinal)];
}

static int Run(string fileName, string[] arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment = null)
{
    Console.WriteLine("> " + fileName + " " + string.Join(' ', arguments));
    var start = StartInfo(fileName, arguments, workingDirectory, environment);
    try
    {
        using var process = Process.Start(start) ?? throw Failure($"Could not start {fileName}.");
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Win32Exception exception)
    {
        Console.Error.WriteLine($"Cannot start {fileName}: {exception.Message}");
        return 127;
    }
}

static (int Code, string Output) Capture(string fileName, string[] arguments, string workingDirectory,
    IReadOnlyDictionary<string, string?>? environment = null, string? input = null)
{
    var start = StartInfo(fileName, arguments, workingDirectory, environment);
    start.RedirectStandardOutput = true;
    start.RedirectStandardError = true;
    start.RedirectStandardInput = input is not null;
    try
    {
        using var process = Process.Start(start) ?? throw Failure($"Could not start {fileName}.");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, (output.Result + error.Result).Trim());
    }
    catch (Win32Exception exception)
    {
        return (127, exception.Message);
    }
}

static ProcessStartInfo StartInfo(string fileName, string[] arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment)
{
    ProcessStartInfo start = new(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false };
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);
    foreach (var (name, value) in environment ?? new Dictionary<string, string?>(StringComparer.Ordinal))
        start.Environment[name] = value;
    return start;
}

static string Sha256(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}

static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

static InvalidOperationException Failure(string message) => new(message);

static string Checkout([CallerFilePath] string path = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
