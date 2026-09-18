using Agentic.PackageFixtures;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

[Collection(GitHubNetworkScope.Name)]
public sealed class PackageFixtureTests(ITestOutputHelper output)
{
    static readonly PackageTestRun TestRun = new();
    public static TheoryData<string, string, bool> Scenarios()
        => BuildScenarios(Environment.GetEnvironmentVariable("AGENTIC_E2E_BASELINE"));

    internal static TheoryData<string, string, bool> BuildScenarios(string? selectedBaseline)
    {
        TheoryData<string, string, bool> rows = [];
        var definitions = Directory.GetDirectories(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions"))
            .Order(StringComparer.Ordinal).Select(path => FixtureFiles.ReadJson<FixtureDefinition>(Path.Combine(path, "definition.json"))).ToArray();
        foreach (var definition in definitions)
            rows.Add(definition.Name, "fresh", false);
        if (selectedBaseline is not null)
        {
            FixtureFiles.Require(Path.GetFileName(selectedBaseline) == selectedBaseline, "Invalid baseline ID.");
            string directory = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baselines", selectedBaseline);
            var collection = FixtureFiles.ReadJson<BaselineCollection>(Path.Combine(directory, "collection.json"));
            foreach (string failed in collection.Failed.Keys.Order(StringComparer.Ordinal))
                rows.Add(failed, "baseline-preparation-failed", false);
            definitions = [.. collection.Completed.Select(name => FixtureFiles.ReadJson<FixtureCapture>(Path.Combine(directory, name, "metadata.json")).Definition)];
            FixtureFiles.Require(definitions.Length > 0, "Selected baseline has no completed fixtures.");
        }
        foreach (var definition in definitions)
        {
            if (definition.BaselinePreview)
            {
                foreach (string scenario in new[] { "preview-preview", "preview-stable", "candidate-preview-stable", "preview-declined" })
                    rows.Add(definition.Name, scenario, false);
                rows.Add(definition.Name, "preview-preview", true);
            }
            else
            {
                foreach (string scenario in new[] { "migration", "stable" })
                {
                    rows.Add(definition.Name, scenario, false);
                    rows.Add(definition.Name, scenario, true);
                }
            }
        }
        if (definitions.Any(definition => definition.Name == "broad-stack" && !definition.BaselinePreview))
        {
            rows.Add("broad-stack", "stable-current", false);
            rows.Add("broad-stack", "stable-declined", false);
        }
        return rows;
    }

    [SkippableTheory]
    [MemberData(nameof(Scenarios))]
    [Trait("Category", "PackageNetwork")]
    public async Task ExactPackagesInstallAndMigrate(string fixture, string scenario, bool contentTransition)
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1", "Opt in with AGENTIC_E2E_NETWORK=1 and exact package/build inputs.");
        string? selectedFixture = Environment.GetEnvironmentVariable("AGENTIC_E2E_FIXTURE");
        string? selectedScenario = Environment.GetEnvironmentVariable("AGENTIC_E2E_SCENARIO");
        FixtureFiles.Require(selectedFixture is null || Scenarios().Any(row => (string)row[0] == selectedFixture), "Unknown AGENTIC_E2E_FIXTURE.");
        FixtureFiles.Require(selectedScenario is null || Scenarios().Any(row => (string)row[1] == selectedScenario), "Unknown AGENTIC_E2E_SCENARIO.");
        Skip.If(selectedFixture is not null && selectedFixture != fixture, $"Fixture selection: {selectedFixture}");
        Skip.If(selectedScenario is not null && selectedScenario != scenario, $"Scenario selection: {selectedScenario}");
        if (scenario == "baseline-preparation-failed")
        {
            string baselineId = CandidateInputs.Required("AGENTIC_E2E_BASELINE");
            var collection = FixtureFiles.ReadJson<BaselineCollection>(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baselines", baselineId, "collection.json"));
            Assert.Fail($"No valid {fixture} snapshot in {baselineId}. Published baseline preparation failed: {collection.Failed[fixture]}");
        }
        bool interactive = fixture == "broad-stack" || scenario == "preview-declined";
        Skip.If(interactive && !RecordedTerminal.Supported, "PTY scenario requires Bash on macOS/Linux; noninteractive scenarios remain portable.");
        var budget = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(true);
        output.WriteLine($"Preflight authenticated GitHub core budget: {budget.Remaining}/{budget.Limit}, reset {DateTimeOffset.FromUnixTimeSeconds(budget.Reset):O}");
        var candidate = await CandidateInputs.LoadAsync().ConfigureAwait(true);
        output.WriteLine($"{candidate.Configuration}: origin/{candidate.Branch}@{candidate.Commit}\nCheck: {candidate.Check.Path} SHA256 {candidate.Check.Sha256}\nCompanion: {candidate.Companion.Path} SHA256 {candidate.Companion.Sha256}\nDna: {candidate.Dna.Path} SHA256 {candidate.Dna.Sha256}");
        using PackageScenario run = new(candidate, fixture, scenario, output.WriteLine, TestRun);
        try
        {
            await run.PrepareAsync().ConfigureAwait(true);
            if (contentTransition)
                Skip.If(!run.HasContentTransition, "UNCHANGED SOURCE: " + run.SourceDescription + "; reinstall/preservation is tested independently.");
            if (scenario == "stable-declined")
                Skip.If(!run.HasSkillUpdates, "No published skill update to decline: " + run.SourceDescription + "; stable no-op preservation is tested independently.");
            await run.ExecuteAsync(interactive).ConfigureAwait(true);
            await run.VerifyAsync().ConfigureAwait(true);
            if (contentTransition)
                Assert.True(run.ObservedContentTransition, "Expected a real content transition; changed tracking metadata alone is insufficient.");
        }
        finally
        {
            candidate.Check.Verify();
            candidate.Companion.Verify();
            candidate.Dna.Verify();
            run.VerifyBaselineUnchanged();
        }
    }

    [SkippableFact]
    [Trait("Category", "CandidatePackage")]
    public async Task ExactCompanionRoundTripAndCleanCacheRestore()
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1" && Environment.GetEnvironmentVariable("AGENTIC_E2E_CHECK_PACKAGE") is null,
            "Supply exact candidate package inputs to opt in.");
        _ = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(true);
        var candidate = await CandidateInputs.LoadAsync().ConfigureAwait(true);
        try
        {
            using FixtureWorkspace installed = new();
            await installed.InitializeAsync().ConfigureAwait(true);
            installed.AddPackage(candidate.Check);
            installed.AddPackage(candidate.Companion);
            await PackageScenario.WriteUnrelatedManifestAsync(installed, candidate.Check).ConfigureAwait(true);
            var unrelated = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(installed.Target, ".config/dotnet-tools.json")).ConfigureAwait(true))!["tools"]!["agentic.check"]!.DeepClone();
            _ = await installed.Process.SuccessAsync("dotnet", ["tool", "install", candidate.Companion.Id, "--local", "--version", candidate.Companion.Version], installed.Target).ConfigureAwait(true);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(unrelated, System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(installed.Target, ".config/dotnet-tools.json")).ConfigureAwait(true))!["tools"]!["agentic.check"]));
            await PackageScenario.CompanionRoundTripAsync(installed, candidate.Companion).ConfigureAwait(true);
            _ = await installed.Process.SuccessAsync("git", ["add", "."], installed.Target).ConfigureAwait(true);
            _ = await installed.Process.SuccessAsync("git", ["commit", "-m", "Installed fixture"], installed.Target).ConfigureAwait(true);
            using FixtureWorkspace clone = new();
            await clone.InitializeAsync().ConfigureAwait(true);
            clone.AddPackage(candidate.Check);
            clone.AddPackage(candidate.Companion);
            Directory.Delete(clone.Target, true);
            _ = await clone.Process.SuccessAsync("git", ["clone", "--no-hardlinks", installed.Target, clone.Target], clone.Root).ConfigureAwait(true);
            Assert.False(Directory.Exists(clone.Environment["NUGET_PACKAGES"]));
            _ = await clone.Process.SuccessAsync("dotnet", ["tool", "restore"], clone.Target).ConfigureAwait(true);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(unrelated, System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(clone.Target, ".config/dotnet-tools.json")).ConfigureAwait(true))!["tools"]!["agentic.check"]));
            await PackageScenario.CompanionRoundTripAsync(clone, candidate.Companion).ConfigureAwait(true);
        }
        finally
        {
            candidate.Check.Verify();
            candidate.Companion.Verify();
        }
    }
}
