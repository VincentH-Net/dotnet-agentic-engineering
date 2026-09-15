using System.Text.Json;
using Agentic.PackageFixtures;
using Hex1b.Automation;

namespace Agentic.Check.LiveTests;

sealed class PackageScenario(CandidateBuild candidate, string fixtureName, string scenario, Action<string> log, PackageTestRun testRun) : IDisposable
{
    const string UserText = "\nFixture-owned instructions: preserve this exact text.\n";
    readonly FixtureWorkspace workspace = new();
    readonly Dictionary<string, SourceSnapshot> sources = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> skillTextBefore = new(StringComparer.Ordinal);
    readonly string runId = $"{fixtureName}-{scenario}-{Guid.NewGuid():N}";
    FixtureDefinition definition = null!;
    FixtureCapture? baseline;
    PublishedToolDefinition? baselineCompanion;
    string? snapshot;
    string executable = string.Empty;
    SourceOracle oracle = null!;
    JsonElement dryReport;
    JsonElement report;
    string agentsBefore = string.Empty;
    string manifestBefore = string.Empty;
    SortedDictionary<string, string> before = new(StringComparer.Ordinal);
    IReadOnlyList<SkillManifestEntry> expectedSkills = [];
    bool preview = scenario is "fresh" or "migration" or "preview-preview" or "preview-declined";
    internal bool HasContentTransition { get; private set; }
    internal bool ObservedContentTransition { get; private set; }
    internal bool HasSkillUpdates => dryReport.GetProperty("outdatedSkills").GetInt32() > 0;
    internal string SourceDescription => $"{fixtureName}/{scenario}: " + string.Join(", ", sources.Values.Select(source => $"{source.Repository}@{source.Reference} ({source.Commit})"));

    internal async Task PrepareAsync()
    {
        if (scenario == "fresh")
        {
            string definitionDirectory = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions", fixtureName);
            definition = FixtureFiles.ReadJson<FixtureDefinition>(Path.Combine(definitionDirectory, "definition.json"));
            FixtureFiles.MaterializeTrigger(definitionDirectory, workspace.Target);
        }
        else
        {
            string id = CandidateInputs.Required("AGENTIC_E2E_BASELINE");
            FixtureFiles.Require(Path.GetFileName(id) == id, "Invalid baseline ID.");
            string baselineDirectory = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baselines", id);
            var collection = FixtureFiles.ReadJson<BaselineCollection>(Path.Combine(baselineDirectory, "collection.json"));
            FixtureFiles.Require(collection.Completed.Contains(fixtureName, StringComparer.Ordinal), $"Baseline {id} has no completed {fixtureName}.");
            baseline = FixtureFiles.ReadJson<FixtureCapture>(Path.Combine(baselineDirectory, fixtureName, "metadata.json"));
            baselineCompanion = collection.Definition.Companion;
            definition = baseline.Definition;
            snapshot = Path.Combine(baselineDirectory, fixtureName, "snapshot.zip");
            VerifyBaselineUnchanged();
            Directory.Delete(workspace.Target, true);
            FixtureFiles.ExtractSnapshot(snapshot, workspace.Target);
            FixtureFiles.Require(FixtureFiles.EqualInventory(FixtureFiles.Inventory(workspace.Target), baseline.Files), "Snapshot extracted inventory differs from capture.");
        }
        await workspace.InitializeAsync().ConfigureAwait(false);
        workspace.Environment["AGENTIC_CHECK_CACHE_SECONDS"] = "3600";
        workspace.AddPackage(candidate.Companion);
        executable = await workspace.InstallCheckAsync(candidate.Check).ConfigureAwait(false);
        if (baseline?.Companion is { } previous)
        {
            // Future baselines must retain actual published bytes; rewritten candidate versions are not history.
            FixtureFiles.Require(baselineCompanion is not null, "Historical companion metadata requires its original published package URL.");
            var original = await PackageArtifact.DownloadAsync(baselineCompanion!).ConfigureAwait(false);
            FixtureFiles.Require(original.Id == previous.Id && original.Version == previous.Version && original.Sha256 == previous.Sha256, "Historical companion bytes differ from the captured baseline.");
            workspace.AddPackage(original);
            _ = await workspace.Process.SuccessAsync("dotnet", ["tool", "restore"], workspace.Target).ConfigureAwait(false);
            await CompanionRoundTripAsync(workspace, original).ConfigureAwait(false);
        }
        if (scenario != "fresh")
        {
            await WriteUnrelatedManifestAsync(workspace, candidate.Check).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(workspace.Target, "user-owned.txt"), "untouched source\n").ConfigureAwait(false);
            await File.AppendAllTextAsync(Path.Combine(workspace.Target, "AGENTS.md"), UserText).ConfigureAwait(false);
        }
        oracle = new(workspace.Process, workspace.Root);
        if (scenario == "candidate-preview-stable")
        {
            preview = true;
            await ResolveSourcesAsync().ConfigureAwait(false);
            await InvokeAsync(false, false, "candidate-preview-setup").ConfigureAwait(false);
            await VerifyDeliveredAsync().ConfigureAwait(false);
            sources.Clear();
            preview = false;
        }
        await ResolveSourcesAsync().ConfigureAwait(false);
        if (scenario == "stable-current")
        {
            await InvokeAsync(false, false, "stable-update-setup").ConfigureAwait(false);
            await VerifyDeliveredAsync().ConfigureAwait(false);
        }
        before = FixtureFiles.Inventory(workspace.Target);
        foreach (string file in before.Keys.Where(file => file.EndsWith("/SKILL.md", StringComparison.Ordinal)))
            skillTextBefore[file] = await File.ReadAllTextAsync(Path.Combine(workspace.Target, file)).ConfigureAwait(false);
        agentsBefore = File.Exists(Path.Combine(workspace.Target, "AGENTS.md"))
            ? await File.ReadAllTextAsync(Path.Combine(workspace.Target, "AGENTS.md")).ConfigureAwait(false) : string.Empty;
        manifestBefore = File.Exists(Path.Combine(workspace.Target, ".config/dotnet-tools.json"))
            ? await File.ReadAllTextAsync(Path.Combine(workspace.Target, ".config/dotnet-tools.json")).ConfigureAwait(false) : string.Empty;
        await InvokeAsync(true, false, "dry-run").ConfigureAwait(false);
        dryReport = report;
        FixtureFiles.Require(FixtureFiles.EqualInventory(before, FixtureFiles.Inventory(workspace.Target)), "Dry run changed target files.");
        HasContentTransition = DetermineContentDifference();
        log(SourceDescription);
        FixtureFiles.WriteJson(Path.Combine(FixtureFiles.Reports, runId + "-sources.json"), sources.Values.Select(source => new { source.Repository, source.Reference, source.Commit }));
    }

    async Task ResolveSourcesAsync()
    {
        var stack = StackDetector.Detect(workspace.Target);
        var manifest = preview ? StaticSkillManifest.Preview : StaticSkillManifest.All;
        var planned = SkillPlanner.Plan(manifest, stack);
        // Close declared dependencies against the same candidate manifest, excluding the companion action.
        var selected = planned.ToDictionary(skill => skill.Key, StringComparer.Ordinal);
        Queue<SkillDependency> dependencies = new(planned.SelectMany(skill => skill.Dependencies));
        while (dependencies.TryDequeue(out var dependency))
        {
            if (dependency == CompanionDependency.Identity || selected.ContainsKey(dependency.Key))
                continue;
            var skill = manifest.Single(skill => skill.Key == dependency.Key);
            selected.Add(skill.Key, skill);
            foreach (var nested in skill.Dependencies)
                dependencies.Enqueue(nested);
        }
        expectedSkills = [.. selected.Values];
        foreach (string repository in expectedSkills.Select(skill => skill.SourceRepo).Append(SourceOracle.OwnRepository).Distinct(StringComparer.Ordinal))
            sources[repository] = await oracle.SelectedAsync(repository, preview, preview ? candidate.Commit : null).ConfigureAwait(false);
    }

    internal Task ExecuteAsync(bool interactive) => InvokeAsync(false, interactive, "apply");

    async Task InvokeAsync(bool dryRun, bool interactive, string phase)
    {
        testRun.UseCache(workspace, candidate.Check.Sha256, preview, log);
        string reportPath = Path.Combine(FixtureFiles.Reports, runId + "-" + phase + ".json");
        _ = Directory.CreateDirectory(FixtureFiles.Reports);
        string[] arguments = [workspace.Target, "--agents", definition.Agents, "--report", reportPath, "--verbose",
            .. preview ? new[] { "--preview", "--preview-source-ref", candidate.Commit } : [],
            .. dryRun ? new[] { "--dry-run", "--yes" } : interactive ? [] : ["--yes"]];
        if (interactive)
        {
            string recording = Path.Combine(FixtureFiles.Reports, "recordings", runId + "-" + phase + ".cast");
            log("asciinema play " + RecordedTerminal.Quote(recording));
            _ = await RecordedTerminal.RunAsync(workspace, executable, arguments, recording, InteractAsync).ConfigureAwait(false);
        }
        else
        {
            var result = await workspace.Process.RunAsync(executable, arguments, workspace.Target).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(FixtureFiles.Reports, runId + "-" + phase + ".txt"), result.Output + result.Error).ConfigureAwait(false);
            result.RequireSuccess($"{fixtureName}/{scenario}/{phase}");
        }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(false));
        report = document.RootElement.Clone();
        BaselinePreparation.VerifyReport(report, definition);
        foreach (var update in report.GetProperty("skillUpdateDryRuns").EnumerateArray().Concat(report.GetProperty("skillUpdates").EnumerateArray()))
            FixtureFiles.Require(update.GetProperty("success").GetBoolean(), $"Real gh update failed: {update}");
    }

    async Task InteractAsync(Hex1bTerminalAutomator auto)
    {
        // Current stable runs may have no recommendation selector at all.
        bool hasActions = dryReport.GetProperty("directiveSummary").GetProperty("missingCount").GetInt32() > 0
            || dryReport.GetProperty("directiveSummary").GetProperty("outdatedCount").GetInt32() > 0
            || dryReport.GetProperty("missingSkills").GetArrayLength() > 0 || preview
            || dryReport.GetProperty("actions").EnumerateArray().Any(action => action.GetString()!.StartsWith("Would install ", StringComparison.Ordinal))
            || (dryReport.TryGetProperty("companion", out var companion) && companion.ValueKind == JsonValueKind.Object);
        if (hasActions)
        {
            await auto.WaitUntilTextAsync("select which to apply:").ConfigureAwait(false);
            if (scenario is "preview-declined" or "stable-declined")
            {
                await auto.LeftAsync().ConfigureAwait(false);
                await auto.WaitUntilTextAsync("[ ]").ConfigureAwait(false);
            }
            else if (scenario == "fresh" && fixtureName == "broad-stack")
            {
                await auto.TypeAsync("InnoWvate.Agentic").ConfigureAwait(false);
                await auto.WaitUntilTextAsync("Filter: InnoWvate.Agentic").ConfigureAwait(false);
                await auto.SpaceAsync().ConfigureAwait(false);
                await auto.WaitUntilTextAsync("[ ] InnoWvate.Agentic").ConfigureAwait(false);
                await auto.EscapeAsync().ConfigureAwait(false);
                await auto.WaitUntilTextAsync("[ ] foundation-prompt-log").ConfigureAwait(false);
                await auto.TypeAsync("foundation-prompt-log").ConfigureAwait(false);
                await auto.WaitUntilTextAsync("Filter: foundation-prompt-log").ConfigureAwait(false);
                await auto.SpaceAsync().ConfigureAwait(false);
                await auto.EscapeAsync().ConfigureAwait(false);
                await auto.WaitUntilTextAsync("[x] InnoWvate.Agentic").ConfigureAwait(false);
                await auto.RightAsync().ConfigureAwait(false);
            }
            await auto.EnterAsync().ConfigureAwait(false);
        }
        if (!preview && HasSkillUpdates)
        {
            await auto.WaitUntilTextAsync("Update these skill(s)?").ConfigureAwait(false);
            await auto.TypeAsync(scenario == "stable-declined" ? "n" : "y").ConfigureAwait(false);
            await auto.EnterAsync().ConfigureAwait(false);
        }
    }

    internal async Task VerifyAsync()
    {
        var after = FixtureFiles.Inventory(workspace.Target);
        foreach (var (path, hash) in before.Where(file => file.Key is not "AGENTS.md" and not "CLAUDE.md" and not ".config/dotnet-tools.json"
            && !file.Key.StartsWith(".agents/skills/", StringComparison.Ordinal) && !file.Key.StartsWith(".claude/skills/", StringComparison.Ordinal)))
        {
            FixtureFiles.Require(after.GetValueOrDefault(path) == hash, $"Unrelated file changed: {path}");
        }

        string agents = await File.ReadAllTextAsync(Path.Combine(workspace.Target, "AGENTS.md")).ConfigureAwait(false);
        if (scenario != "fresh")
        {
            FixtureFiles.Require(agents.Contains(UserText.Trim(), StringComparison.Ordinal), "Unrelated instructions changed.");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.Target, ".config/dotnet-tools.json")).ConfigureAwait(false));
            var previousManifest = System.Text.Json.Nodes.JsonNode.Parse(manifestBefore)!;
            var currentManifest = System.Text.Json.Nodes.JsonNode.Parse(manifest.RootElement.GetRawText())!;
            foreach (var entry in previousManifest["tools"]!.AsObject().Where(entry => !entry.Key.Equals("InnoWvate.Agentic", StringComparison.OrdinalIgnoreCase)))
                FixtureFiles.Require(System.Text.Json.Nodes.JsonNode.DeepEquals(entry.Value, currentManifest["tools"]![entry.Key]), $"Unrelated manifest entry changed: {entry.Key}");
            foreach (var property in previousManifest.AsObject().Where(property => property.Key != "tools"))
                FixtureFiles.Require(System.Text.Json.Nodes.JsonNode.DeepEquals(property.Value, currentManifest[property.Key]), $"Manifest property changed: {property.Key}");
        }
        if (scenario is "preview-declined" or "stable-declined")
        {
            foreach (var (path, hash) in before.Where(file => file.Key.StartsWith(".agents/skills/", StringComparison.Ordinal) || file.Key.StartsWith(".claude/skills/", StringComparison.Ordinal)))
                FixtureFiles.Require(after.GetValueOrDefault(path) == hash, $"Deselected skill changed: {path}");
            FixtureFiles.Require(agents == agentsBefore, "Deselected directive changed.");
            FixtureFiles.Require(report.GetProperty("installResults").GetArrayLength() == 0, "Deselected skills were installed.");
        }
        else
        {
            await VerifyDeliveredAsync().ConfigureAwait(false);
            ObservedContentTransition = ContentChanged(before, after) || agentsBefore != agents;
            foreach (var (path, oldText) in skillTextBefore)
            {
                var (oldYaml, oldBody) = SourceOracle.ParseSkill(oldText);
                var (newYaml, newBody) = SourceOracle.ParseSkill(await File.ReadAllTextAsync(Path.Combine(workspace.Target, path)).ConfigureAwait(false));
                SourceOracle.RemoveTracking(oldYaml);
                SourceOracle.RemoveTracking(newYaml);
                ObservedContentTransition |= oldBody != newBody || !oldYaml.Equals(newYaml);
            }
            ObservedContentTransition |= after.Keys.Any(path => path.StartsWith(".agents/skills/", StringComparison.Ordinal) && !before.ContainsKey(path));
            if (scenario == "stable-current")
            {
                FixtureFiles.Require(report.GetProperty("outdatedSkills").GetInt32() == 0, "Already-current check still reports updates.");
                FixtureFiles.Require(report.GetProperty("installResults").GetArrayLength() == 0, "Already-current stable check unnecessarily reinstalled skills.");
                FixtureFiles.Require(!ObservedContentTransition, "Already-current check modified content.");
            }
            if (scenario is "preview-stable" or "candidate-preview-stable")
            {
                await InvokeAsync(false, false, "subsequent-stable").ConfigureAwait(false);
                FixtureFiles.Require(report.GetProperty("outdatedSkills").GetInt32() == 0, "Subsequent stable update is not current.");
                FixtureFiles.Require(report.GetProperty("installResults").GetArrayLength() == 0, "Subsequent stable check unexpectedly reinstalled skills.");
            }
        }
        foreach (var source in sources.Values)
            await oracle.EnsureUnmovedAsync(source).ConfigureAwait(false);
    }

    async Task VerifyDeliveredAsync()
    {
        var allowed = expectedSkills.Select(skill => skill.LocalFolder).ToHashSet(StringComparer.Ordinal);
        var origins = await oracle.VerifySkillsAsync(workspace.Target, sources, allowed).ConfigureAwait(false);
        if (preview)
        {
            var installed = report.GetProperty("installResults").EnumerateArray().Select(item => item.GetProperty("sourceRepo").GetString() + "\n" + item.GetProperty("installArg").GetString()).Order(StringComparer.Ordinal);
            FixtureFiles.Require(installed.SequenceEqual(expectedSkills.Select(skill => skill.Key).Order(StringComparer.Ordinal)), "Preview did not execute each intended real gh installation/reinstallation.");
            foreach (var installation in report.GetProperty("installResults").EnumerateArray())
                FixtureFiles.Require(installation.GetProperty("standardOutput").GetString()!.Contains("Installed ", StringComparison.Ordinal), "gh did not report an actual install/reinstall.");
            foreach (var skill in report.GetProperty("recommendedSkills").EnumerateArray().Where(skill => skill.GetProperty("sourceRepo").GetString() == SourceOracle.OwnRepository))
                FixtureFiles.Require(skill.GetProperty("sourceRef").GetString() == candidate.Commit, "Candidate skill source SHA differs from package/source provenance.");
        }
        foreach (string agent in definition.Agents.Contains("claude-code", StringComparison.Ordinal) ? new[] { ".agents/skills", ".claude/skills" } : [".agents/skills"])
        {
            var expected = expectedSkills.Select(skill => agent + "/" + skill.LocalFolder).Order(StringComparer.Ordinal);
            var actual = origins.Where(origin => origin.LocalPath.StartsWith(agent + "/", StringComparison.Ordinal)).Select(origin => origin.LocalPath).Order(StringComparer.Ordinal);
            FixtureFiles.Require(expected.SequenceEqual(actual), $"Installed manifest/dependency inventory mismatch in {agent}");
            var retained = baseline?.Sources.Where(origin => origin.LocalPath.StartsWith(agent + "/", StringComparison.Ordinal)).Select(origin => origin.LocalPath) ?? [];
            var allExpected = expected.Concat(retained).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            string skillsDirectory = Path.Combine(workspace.Target, agent);
            string[] directories = Directory.Exists(skillsDirectory) ? Directory.GetDirectories(skillsDirectory) : [];
            var allActual = directories.Select(path => agent + "/" + Path.GetFileName(path)).Order(StringComparer.Ordinal);
            FixtureFiles.Require(allExpected.SequenceEqual(allActual), $"Unexpected skill directories in {agent}");
        }
        foreach (var origin in origins)
        {
            var source = sources[origin.Repository];
            if (preview)
                FixtureFiles.Require(origin.Pin == source.Commit, $"Preview did not pin {origin.LocalPath} to {source.Commit}");
            else
                FixtureFiles.Require(origin.Pin is null, $"Stable switch left a pin on {origin.LocalPath}: {origin.Pin}. Production compatibility defect; do not manually unpin.");
        }
        string agents = await File.ReadAllTextAsync(Path.Combine(workspace.Target, "AGENTS.md")).ConfigureAwait(false);
        foreach (var (name, block) in ExpectedDirectives())
        {
            FixtureFiles.Require(agents.Contains(block, StringComparison.Ordinal), $"Directive {name} differs from {sources[SourceOracle.OwnRepository].Commit}");
            FixtureFiles.Require(agents.Split($"<!-- dotnet-agentic-engineering:{name}:start -->", StringSplitOptions.None).Length == 2, $"Duplicate directive {name}");
        }
        if (definition.Agents.Contains("claude-code", StringComparison.Ordinal))
            FixtureFiles.Require((await File.ReadAllTextAsync(Path.Combine(workspace.Target, "CLAUDE.md")).ConfigureAwait(false)).Contains("@AGENTS.md", StringComparison.Ordinal), "Missing Claude instruction import.");
        // Content outside the applicable manifest is preserved, including preview-only skills in stable mode.
        foreach (var (path, hash) in before.Where(file => (file.Key.StartsWith(".agents/skills/", StringComparison.Ordinal) || file.Key.StartsWith(".claude/skills/", StringComparison.Ordinal)) && !allowed.Contains(file.Key.Split('/')[2])))
            FixtureFiles.Require(FixtureFiles.Hash(Path.Combine(workspace.Target, path)) == hash, $"Unselected skill unexpectedly changed: {path}");
        bool requiresCompanion = ExpectedDirectives().Any(directive => directive.Name == "foundation-prompt-log" && directive.Block.Contains("dotnet agentic", StringComparison.Ordinal))
            || expectedSkills.Any(skill => skill.Dependencies.Contains(CompanionDependency.Identity));
        if (requiresCompanion)
        {
            if (report.TryGetProperty("companion", out var companionReport) && companionReport.ValueKind == JsonValueKind.Object)
            {
                FixtureFiles.Require(companionReport.GetProperty("success").GetBoolean(), "Required companion failed.");
                string sourceProject = await File.ReadAllTextAsync(Path.Combine(sources[SourceOracle.OwnRepository].Directory, CompanionSourceVersionReader.ProjectPath)).ConfigureAwait(false);
                var requirement = CompanionSourceVersionReader.Parse(sourceProject);
                FixtureFiles.Require(companionReport.GetProperty("requiredMinimum").GetString() == requirement.Minimum, "Prerequisite requirement differs from the selected immutable source.");
                string[] actions = [.. report.GetProperty("actions").EnumerateArray().Select(action => action.GetString()!)];
                int prepared = Array.FindIndex(actions, action => action.Contains("InnoWvate.Agentic: installed", StringComparison.Ordinal));
                int directive = Array.FindIndex(actions, action => action.Contains("directive foundation-prompt-log", StringComparison.Ordinal));
                if (directive >= 0)
                    FixtureFiles.Require(prepared >= 0 && prepared < directive, "Dependent directive arrived before the companion prerequisite.");
            }
            if (baseline?.Companion is null && scenario is "migration" or "preview-preview")
                FixtureFiles.Require(report.GetProperty("companion").GetProperty("action").GetString() == "install", "Pre-companion migration must be first installation.");
            await CompanionRoundTripAsync(workspace, candidate.Companion).ConfigureAwait(false);
        }
        FixtureFiles.WriteJson(Path.Combine(FixtureFiles.Reports, runId + "-installed-sources.json"), origins);
    }

    IEnumerable<(string Name, string Block)> ExpectedDirectives()
        => DirectiveOracle.Expected(sources[SourceOracle.OwnRepository].Directory, definition.Technologies);

    bool DetermineContentDifference()
    {
        if (ExpectedDirectives().Any(directive => !agentsBefore.Contains(directive.Block, StringComparison.Ordinal)))
            return true;
        foreach (var skill in expectedSkills)
        {
            var origin = baseline?.Sources.FirstOrDefault(origin => origin.LocalPath == ".agents/skills/" + skill.LocalFolder);
            if (origin is null)
                return true;
            string source = Path.Combine(sources[skill.SourceRepo].Directory, origin.SourcePath);
            if (!Directory.Exists(source) || !FixtureFiles.EqualInventory(FixtureFiles.Inventory(source), origin.SourceFiles))
                return true;
        }
        return false;
    }

    static bool ContentChanged(SortedDictionary<string, string> oldFiles, SortedDictionary<string, string> newFiles)
        => oldFiles.Where(file => file.Key.StartsWith(".agents/skills/", StringComparison.Ordinal))
            .Any(file => newFiles.GetValueOrDefault(file.Key) != file.Value && !file.Key.EndsWith("/SKILL.md", StringComparison.Ordinal));

    internal static async Task WriteUnrelatedManifestAsync(FixtureWorkspace workspace, PackageArtifact check)
    {
        string path = Path.Combine(workspace.Target, ".config/dotnet-tools.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var manifest = File.Exists(path) ? System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false))!
            : System.Text.Json.Nodes.JsonNode.Parse("""{"version":1,"isRoot":false,"tools":{}}""")!;
        await File.WriteAllTextAsync(path, manifest.ToJsonString(FixtureFiles.JsonOptions)).ConfigureAwait(false);
        string action = manifest["tools"]!["agentic.check"] is null ? "install" : "update";
        _ = await workspace.Process.SuccessAsync("dotnet", ["tool", action, check.Id, "--local", "--tool-manifest", path, "--version", check.Version, "--allow-downgrade"], workspace.Target).ConfigureAwait(false);
    }

    internal static Task CompanionRoundTripAsync(FixtureWorkspace workspace, PackageArtifact companion)
        => CompanionExercise.RunAsync(workspace, companion);

    internal void VerifyBaselineUnchanged()
    {
        if (snapshot is not null)
            FixtureFiles.Require(FixtureFiles.Hash(snapshot) == baseline!.SnapshotSha256, $"Immutable baseline changed: {snapshot}");
    }

    public void Dispose()
    {
        oracle?.Dispose();
        workspace.Dispose();
    }

}
