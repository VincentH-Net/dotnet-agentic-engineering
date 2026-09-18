// Run from any directory: dotnet run --file /path/to/tests/run-full-suite.cs
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

if (args is ["--help"])
{
    PrintUsage();
    return 0;
}
if (args.Length != 0)
{
    Console.Error.WriteLine("Unexpected arguments. Use --help for usage.");
    return 2;
}

string checkout = Checkout();
string baseline = Environment.GetEnvironmentVariable("AGENTIC_E2E_BASELINE") ?? "agentic-check-2.2.0-2026-09-14-capture02";
string runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
string reports = Path.Combine(checkout, "tests", "TestResults", "full-suite", runId);
string candidates = Path.Combine(reports, "candidates");
string summary = Path.Combine(reports, "summary.txt");
_ = Directory.CreateDirectory(reports);
File.WriteAllText(summary, $"Full suite (C#)\nBaseline: {baseline}\n");

string[] projects = ["Agentic.Tests", "Agentic.Check.Tests", "Agentic.LiveTests", "Agentic.Check.LiveTests"];
string[] packageKinds = ["check", "companion", "dna"];
List<string> failedCommands = [];
Dictionary<string, string?> settings = new(StringComparer.Ordinal)
{
    ["AGENTIC_E2E_SOURCE_CHECKOUT"] = checkout,
    ["AGENTIC_E2E_BASELINE"] = baseline,
    ["AGENTIC_E2E_NETWORK"] = "1",
    ["AGENTIC_CHECK_SKILL_MAINTENANCE"] = "1",
    ["AGENTIC_E2E_FIXTURE"] = null,
    ["AGENTIC_E2E_SCENARIO"] = null,
    ["AGENTIC_E2E_REPORTS"] = Path.Combine(reports, "pack"),
    ["AGENTIC_E2E_CACHE"] = Path.Combine(reports, "cache"),
    ["AGENTIC_CHECK_MAINTENANCE_REPORT_DIR"] = Path.Combine(reports, "maintenance"),
    ["AGENTIC_CHECK_MAINTENANCE_CACHE_SECONDS"] = "1800"
};

int result = 1;
try
{
    if (!File.Exists(Path.Combine(checkout, "tests", "fixtures", "baselines", baseline, "collection.json")))
    {
        Console.Error.WriteLine($"Missing persisted baseline collection: {baseline}");
        return result;
    }

    // Bootstrap the existing helper, which validates origin/branch/clean source before packing.
    if (Run("build", "tests/Agentic.FixturePreparation/Agentic.FixturePreparation.csproj", "-c", "Release") != 0
        || Run("tests/Agentic.FixturePreparation/bin/Release/net10.0/Agentic.FixturePreparation.dll",
            "pack-candidates", candidates, "Release") != 0)
    {
        return result;
    }

    foreach (var (id, variable) in new[] { ("Agentic.Check", "AGENTIC_E2E_CHECK_PACKAGE"), ("InnoWvate.Agentic", "AGENTIC_E2E_COMPANION_PACKAGE"), ("InnoWvate.Dna", "AGENTIC_E2E_DNA_PACKAGE") })
    {
        string[] packages = Directory.GetFiles(candidates, id + ".*.nupkg");
        if (packages.Length != 1)
        {
            Console.Error.WriteLine($"Expected exactly one {id} candidate in {candidates}.");
            return result;
        }
        settings[variable] = packages[0];
    }
    settings["AGENTIC_E2E_BUILD_MANIFEST"] = Path.Combine(candidates, "candidate-build.json");
    settings["AGENTIC_E2E_REPORTS"] = Path.Combine(reports, "network");

    foreach (string project in projects)
    {
        if (Run("build", $"tests/{project}/{project}.csproj", "-c", "Release") != 0)
            return result;
    }

    result = 0;
    foreach (string project in projects)
    {
        // A failing test project must not prevent the remaining projects from running.
        if (Run("test", $"tests/{project}/{project}.csproj", "-c", "Release", "--no-build", "--no-restore",
            "--results-directory", Path.Combine(reports, project), "--logger", "trx;LogFileName=results.trx",
            "--logger", "console;verbosity=normal") != 0)
        {
            result = 1;
        }
    }
    return result;
}
catch
{
    result = 1;
    throw;
}
finally
{
    string outcome = Summarize(result);
    File.AppendAllText(summary, outcome);
    Console.WriteLine(outcome);
}

int Run(params string[] arguments)
{
    string command = "dotnet " + string.Join(' ', arguments.Select(argument => '"' + argument + '"'));
    Console.WriteLine("\n> " + command);
    ProcessStartInfo start = new("dotnet") { WorkingDirectory = checkout, UseShellExecute = false };
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);
    foreach (var (name, value) in settings)
        start.Environment[name] = value;
    int code;
    try
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        process.WaitForExit();
        code = process.ExitCode;
    }
    catch (Win32Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        code = 127;
    }
    File.AppendAllText(summary, $"{code}: {command}\n");
    if (code != 0)
        failedCommands.Add($"Exit {code}: {command}");
    return code;
}

string Summarize(int exitCode)
{
    StringBuilder text = new();
    List<string> failures = [];
    Dictionary<string, int> skips = new(StringComparer.Ordinal);
    int passed = 0, failed = 0, skipped = 0, other = 0, reportsRead = 0;
    bool incomplete = false;
    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    _ = text.AppendLine("\nFull-suite overview");
    _ = text.AppendLine(CultureInfo.InvariantCulture, $"Baseline: {baseline}\n");
    _ = text.AppendLine(CultureInfo.InvariantCulture, $"{"Project",-28} {"Passed",7} {"Failed",7} {"Skipped",8} {"Other",7}");
    foreach (string project in projects)
    {
        ReadReport(Path.Combine(reports, project, "results.trx"), content =>
        {
            var document = XDocument.Parse(content);
            var results = document.Root?.Element(ns + "Results")?.Elements(ns + "UnitTestResult").ToArray() ?? [];
            int expected = (int?)document.Root?.Element(ns + "ResultSummary")?.Element(ns + "Counters")?.Attribute("total") ?? -1;
            if (results.Length == 0)
                throw new InvalidDataException("No test results were recorded.");
            int p = results.Count(test => (string?)test.Attribute("outcome") == "Passed");
            int f = results.Count(test => (string?)test.Attribute("outcome") == "Failed");
            int s = results.Count(test => (string?)test.Attribute("outcome") == "NotExecuted");
            int o = results.Length - p - f - s;
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"{project,-28} {p,7} {f,7} {s,8} {o,7}");
            passed += p;
            failed += f;
            skipped += s;
            other += o;
            reportsRead++;
            if (expected != results.Length)
            {
                incomplete = true;
                _ = text.AppendLine(CultureInfo.InvariantCulture, $"  Incomplete counts: {results.Length} result rows; declared total {expected}.");
            }
            foreach (var test in results)
            {
                string outcome = (string?)test.Attribute("outcome") ?? "Unknown";
                string message = (string?)test.Element(ns + "Output")?.Element(ns + "ErrorInfo")?.Element(ns + "Message") ?? "No reason recorded.";
                if (outcome == "NotExecuted")
                {
                    string reason = SkipReason(message);
                    skips[reason] = skips.GetValueOrDefault(reason) + 1;
                }
                else if (outcome != "Passed")
                {
                    failures.Add($"{outcome}: {(string?)test.Attribute("testName")}\n  {FirstLine(message)}");
                }
            }
            foreach (var info in document.Descendants(ns + "RunInfo").Where(info => (string?)info.Attribute("outcome") == "Error"))
                failures.Add($"{project} runner error: {FirstLine(info.Element(ns + "Text")?.Value ?? "No details recorded.")}");
        });
    }
    _ = text.AppendLine(CultureInfo.InvariantCulture, $"{"TOTAL (reported)",-28} {passed,7} {failed,7} {skipped,8} {other,7}");
    _ = text.AppendLine(CultureInfo.InvariantCulture, $"Reports read: {reportsRead}/{projects.Length}. Missing reports are not counted as zero tests.");

    _ = text.AppendLine("\nFailed commands:");
    _ = text.AppendLine(failedCommands.Count == 0 ? "  None recorded." : string.Join('\n', failedCommands));
    _ = text.AppendLine("\nFailed tests / runner errors:");
    _ = text.AppendLine(failures.Count == 0 ? "  None in the available reports." : string.Join('\n', failures));
    _ = text.AppendLine("\nSkipped tests by reason:");
    if (skips.Count == 0)
        _ = text.AppendLine("  None in the available reports.");
    foreach (var (reason, count) in skips.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  {count}: {reason}");

    _ = text.AppendLine("\nCandidate source and packages:");
    ReadReport(Path.Combine(candidates, "candidate-build.json"), content =>
    {
        using var document = JsonDocument.Parse(content);
        var build = document.RootElement;
        _ = text.AppendLine(CultureInfo.InvariantCulture, $"  origin/{build.GetProperty("branch").GetString()} @ {build.GetProperty("commit").GetString()} ({build.GetProperty("configuration").GetString()})");
        foreach (string name in packageKinds)
        {
            var package = build.GetProperty(name);
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"  {package.GetProperty("id").GetString()} {package.GetProperty("version").GetString()}: {package.GetProperty("path").GetString()}");
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"    SHA256 {package.GetProperty("sha256").GetString()}");
        }
    });

    _ = text.AppendLine("\nGitHub budgets (separate scopes; shared-budget deltas must not be summed as exclusive usage):");
    foreach (string phase in new[] { "pack", "network" })
    {
        string directory = Path.Combine(reports, phase, "github-runs");
        string[] runs = Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];
        if (runs.Length == 0)
        {
            incomplete = true;
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"  {phase}: measurements unavailable; no GitHub run report.");
        }
        foreach (string run in runs.Order(StringComparer.Ordinal))
        {
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"  {phase}/{Path.GetFileName(run)}:");
            // Reuse the measured summary, including resets, unavailable deltas, and attribution caveats.
            ReadReport(Path.Combine(run, "budgets.txt"), content => _ = text.AppendLine(content.TrimEnd()));
        }
    }

    string status = exitCode != 0 ? "FAILED" : incomplete ? "COMMANDS SUCCEEDED; OVERVIEW INCOMPLETE" : "PASSED";
    _ = text.AppendLine(CultureInfo.InvariantCulture, $"\nResult: {status}\nExit code: {exitCode}\nArtifacts: {reports}\nSummary: {summary}");
    return text.ToString();

    void ReadReport(string path, Action<string> append)
    {
        try
        {
            append(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or XmlException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            incomplete = true;
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"  Unavailable: {Path.GetRelativePath(reports, path)} — {FirstLine(exception.Message)}");
        }
    }
}

static string FirstLine(string value) => value.Split('\n', 2)[0].Trim();

static string SkipReason(string message)
    => message.StartsWith("UNCHANGED SOURCE:", StringComparison.Ordinal) ? "Unchanged source; reinstall/preservation covered by separate tests."
        : message.StartsWith("No published skill update to decline:", StringComparison.Ordinal) ? "No published skill update available to decline."
        : FirstLine(message);

static string Checkout([CallerFilePath] string source = "") => Path.GetDirectoryName(Path.GetDirectoryName(source))!;

// CA1303 permits exclusion for code that will not be localized:
// https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1303
[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
    Justification = "This repository-only single-file test runner is not localized; a separate resource file would defeat its standalone format.")]
static void PrintUsage()
    => Console.WriteLine("Usage: dotnet run --file tests/run-full-suite.cs\nRuns all four test projects with network and maintenance checks enabled.\nAGENTIC_E2E_BASELINE optionally selects a persisted baseline collection.");
