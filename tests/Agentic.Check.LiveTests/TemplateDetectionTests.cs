using System.Text;
using Agentic.PackageFixtures;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

sealed class TemplateMaintenanceFactAttribute : FactAttribute
{
    public TemplateMaintenanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AGENTIC_CHECK_SKILL_MAINTENANCE") != "1")
        {
            Skip = "Opt in with AGENTIC_CHECK_SKILL_MAINTENANCE=1; instantiates the installed dotnet new templates.";
        }
    }
}

// What the detector must report for a project as `dotnet new` creates it. The expectations follow
// the skills the gates serve, so a template change that alters detection fails here first.
sealed record TemplateExpectation(string Template, string[] Arguments, string[] Technologies, Dictionary<string, string[]> Gates)
{
    internal string Name => string.Join(' ', Arguments.Where(argument => !argument.EndsWith("-restore", StringComparison.Ordinal)).Prepend(Template));
}

public sealed class TemplateDetectionTests(ITestOutputHelper output)
{
    static readonly TemplateExpectation[] Templates =
    [
        new("console", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("classlib", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("web", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet, TechnologyNames.AspNetCore], []),
        new("webapi", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet, TechnologyNames.AspNetCore], []),
        new("worker", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("blazorwasm", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("mstest", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("maui", ["--no-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet], []),
        new("unoapp", ["-preset", "blank", "--skip-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet, TechnologyNames.Uno],
            new() { ["presentation"] = [], ["markup"] = ["xaml"], ["theme"] = ["fluent"] }),
        new("unoapp", ["-preset", "recommended", "--skip-restore"], [TechnologyNames.Foundation, TechnologyNames.Dotnet, TechnologyNames.Uno],
            new() { ["presentation"] = ["mvux"], ["markup"] = ["xaml"], ["theme"] = ["simple"] })
    ];

    [TemplateMaintenanceFact]
    [Trait("Category", "SkillMaintenance")]
    public async Task InstalledTemplatesDetectAsExpected()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
        // The user's template packages live under the real CLI home, so only quiet the SDK.
        RealProcess process = new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        });
        string root = Directory.CreateTempSubdirectory("agentic-templates-").FullName;
        List<string> failures = [];
        List<string> skipped = [];
        StringBuilder report = new("# Template detection\n\n| Template | Technologies | Gates | Result |\n| --- | --- | --- | --- |\n");
        try
        {
            foreach (var expectation in Templates)
            {
                string target = Path.Combine(root, expectation.Name.Replace(' ', '_'));
                var result = await process.RunAsync("dotnet", ["new", expectation.Template, "-o", target, .. expectation.Arguments], root, cancellationToken: cancellation.Token).ConfigureAwait(true);
                if (!Directory.Exists(target) || !Directory.EnumerateFiles(target, "*.csproj", SearchOption.AllDirectories).Any())
                {
                    string outcome = result.Output.Contains("No templates or subcommands found", StringComparison.Ordinal) ? "skipped: template not installed" : $"failed: {result.Error}{result.Output}".Trim();
                    (outcome.StartsWith("skipped", StringComparison.Ordinal) ? skipped : failures).Add($"{expectation.Name}: {outcome}");
                    _ = report.Append('|').Append(expectation.Name).Append(" | | | ").Append(outcome.Split('\n')[0]).Append(" |\n");
                    continue;
                }

                var detected = StackDetector.Detect(target);
                string[] technologies = [.. detected.Technologies.Order(StringComparer.Ordinal)];
                var gates = detected.InstallGates.SelectMany(gate => gate.Values).GroupBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
                string gateText = string.Join("; ", gates.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={string.Join(',', pair.Value)}"));
                bool matches = technologies.SequenceEqual(expectation.Technologies.Order(StringComparer.Ordinal))
                    && gates.Keys.Order(StringComparer.Ordinal).SequenceEqual(expectation.Gates.Keys.Order(StringComparer.Ordinal))
                    && expectation.Gates.All(pair => gates[pair.Key].SequenceEqual(pair.Value.Order(StringComparer.Ordinal)));
                if (!matches)
                {
                    failures.Add($"{expectation.Name}: detected {string.Join(',', technologies)} [{gateText}]; expected {string.Join(',', expectation.Technologies)} [{string.Join("; ", expectation.Gates.Select(pair => $"{pair.Key}={string.Join(',', pair.Value)}"))}]");
                }

                _ = report.Append('|').Append(expectation.Name).Append(" | ").Append(string.Join(", ", technologies)).Append(" | ").Append(gateText).Append(" | ").Append(matches ? "as expected" : "MISMATCH").Append(" |\n");
            }
        }
        finally
        {
            Directory.Delete(root, true);
            string path = await MaintenanceReport.WriteAsync("template-detection.md", report.ToString()).ConfigureAwait(true);
            output.WriteLine($"Report: {path}");
        }

        foreach (string entry in skipped)
            output.WriteLine(entry);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.DoesNotContain(skipped, entry => entry.StartsWith("console:", StringComparison.Ordinal));
    }
}
