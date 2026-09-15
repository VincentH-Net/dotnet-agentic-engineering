using System.Runtime.InteropServices;
using System.Text.Json;

namespace Agentic.PackageFixtures;

static class BaselinePreparation
{
    internal static async Task<int> PrepareAsync(string definitionPath, string baselineId, bool resumeIncomplete = false)
    {
        var definition = FixtureFiles.ReadJson<BaselineDefinition>(definitionPath);
        FixtureFiles.Require(baselineId.StartsWith($"agentic-check-{definition.InstallerVersion}-{DateTimeOffset.UtcNow:yyyy-MM-dd}", StringComparison.Ordinal)
            && Path.GetFileName(baselineId) == baselineId, "Baseline ID must contain installer version and today's UTC preparation date.");
        string destination = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/baselines", baselineId);
        BaselineCollection? previous = null;
        if (Directory.Exists(destination))
        {
            FixtureFiles.Require(resumeIncomplete, $"Baseline is immutable; use a new ID or explicitly resume an incomplete capture: {destination}");
            previous = FixtureFiles.ReadJson<BaselineCollection>(Path.Combine(destination, "collection.json"));
            int fixtureCount = Directory.GetDirectories(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions")).Length;
            FixtureFiles.Require(previous.Completed.Length < fixtureCount && previous.Definition == definition, "Cannot resume a completed or different baseline.");
            foreach (string completedFixture in previous.Completed)
            {
                var metadata = FixtureFiles.ReadJson<FixtureCapture>(Path.Combine(destination, completedFixture, "metadata.json"));
                FixtureFiles.Require(FixtureFiles.Hash(Path.Combine(destination, completedFixture, "snapshot.zip")) == metadata.SnapshotSha256, "Completed snapshot changed.");
            }
        }
        FixtureFiles.Require(definition.CompanionExpected == (definition.Companion is not null), "Companion expectation requires its actual published package definition, or explicit absence.");
        var budget = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(false);
        Console.WriteLine($"Authenticated GitHub core budget: {budget.Remaining}/{budget.Limit}, reset {DateTimeOffset.FromUnixTimeSeconds(budget.Reset):O}");
        await WaitForSourceBudgetAsync().ConfigureAwait(false);
        var package = await PackageArtifact.DownloadAsync(definition).ConfigureAwait(false);
        var companion = definition.Companion is null ? null : await PackageArtifact.DownloadAsync(definition.Companion).ConfigureAwait(false);
        var retrieved = previous?.RetrievedAtUtc ?? package.RetrievedAtUtc ?? DateTimeOffset.UtcNow;
        if (previous is not null)
            FixtureFiles.Require(previous.Installer.Sha256 == package.Sha256, "Resume installer bytes differ.");
        _ = Directory.CreateDirectory(destination);
        using FixtureWorkspace tools = new();
        await tools.InitializeAsync().ConfigureAwait(false);
        string sdk = await tools.Process.SuccessAsync("dotnet", ["--version"], tools.Root).ConfigureAwait(false);
        string gh = await tools.Process.SuccessAsync("gh", ["--version"], tools.Root).ConfigureAwait(false);
        string help = await tools.Process.SuccessAsync("gh", ["skill", "install", "--help"], tools.Root).ConfigureAwait(false);
        FixtureFiles.Require(help.Contains("--pin", StringComparison.Ordinal) && help.Contains("--force", StringComparison.Ordinal), "gh skill install capabilities missing.");
        _ = await tools.Process.SuccessAsync("gh", ["skill", "update", "--help"], tools.Root).ConfigureAwait(false);
        string executable = await tools.InstallCheckAsync(package).ConfigureAwait(false);
        string version = await tools.Process.SuccessAsync(executable, ["--version"], tools.Root).ConfigureAwait(false);
        FixtureFiles.Require(version.StartsWith(definition.InstallerVersion, StringComparison.Ordinal), $"Wrong executable: {version}");
        List<string> completed = previous is null ? [] : [.. previous.Completed];
        Dictionary<string, string> failed = previous is null ? new(StringComparer.Ordinal) : new(previous.Failed, StringComparer.Ordinal);
        foreach (string fixturePath in Directory.GetDirectories(Path.Combine(FixtureFiles.Checkout, "tests/fixtures/definitions")).Order(StringComparer.Ordinal))
        {
            var fixture = FixtureFiles.ReadJson<FixtureDefinition>(Path.Combine(fixturePath, "definition.json"));
            if (completed.Contains(fixture.Name, StringComparer.Ordinal))
                continue;
            Console.WriteLine($"Preparing {fixture.Name} with published {package.Id} {package.Version}...");
            try
            {
                await CaptureAsync(fixture, fixturePath, definition, destination, executable, companion).ConfigureAwait(false);
                completed.Add(fixture.Name);
                _ = failed.Remove(fixture.Name);
                Console.WriteLine($"Completed {fixture.Name}.");
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or HttpRequestException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or JsonException or YamlDotNet.Core.YamlException)
            {
                failed[fixture.Name] = exception.Message;
                FixtureFiles.WriteJson(Path.Combine(FixtureFiles.Reports, "preparation", baselineId, $"{fixture.Name}-failure-{Guid.NewGuid():N}.json"), new { exception.Message, exception.StackTrace });
                Console.WriteLine($"FAILED {fixture.Name}: {exception.Message}");
            }
            FixtureFiles.WriteJson(Path.Combine(destination, "collection.json"), new BaselineCollection(baselineId, definition, package with { Path = Path.GetFileName(package.Path) }, retrieved, sdk, gh,
                RuntimeInformation.OSDescription, [.. completed], failed, version));
        }
        package.Verify();
        return failed.Count == 0 ? 0 : 1;
    }

    static async Task WaitForSourceBudgetAsync()
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Agentic-FixturePreparation/1.0");
        while (true)
        {
            string json = await client.GetStringAsync(new Uri("https://api.github.com/rate_limit")).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var core = document.RootElement.GetProperty("resources").GetProperty("core");
            if (core.GetProperty("remaining").GetInt32() >= 20)
                return;
            var reset = DateTimeOffset.FromUnixTimeSeconds(core.GetProperty("reset").GetInt64()).AddSeconds(3);
            Console.WriteLine($"Published installer source API budget exhausted; waiting until {reset:O}. No fixture content is substituted.");
            while (DateTimeOffset.UtcNow < reset)
            {
                var delay = reset - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay > TimeSpan.FromSeconds(45) ? TimeSpan.FromSeconds(45) : delay).ConfigureAwait(false);
            }
        }
    }

    static async Task CaptureAsync(FixtureDefinition fixture, string fixturePath, BaselineDefinition baseline, string destination, string executable, PackageArtifact? companion)
    {
        using FixtureWorkspace workspace = new();
        FixtureFiles.MaterializeTrigger(fixturePath, workspace.Target);
        var triggerHashes = FixtureFiles.Inventory(workspace.Target);
        await workspace.InitializeAsync().ConfigureAwait(false);
        if (companion is not null)
            workspace.AddPackage(companion);
        // Reuse only responses written by real invocations during this explicit preparation operation.
        workspace.Environment["AGENTIC_CHECK_CACHE_DIR"] = Path.Combine(FixtureFiles.Reports, "preparation", Path.GetFileName(destination), "http-cache");
        workspace.Environment["AGENTIC_CHECK_CACHE_SECONDS"] = "3600";
        using SourceOracle oracle = new(workspace.Process, workspace.Root);
        var directiveSource = await oracle.SelectedAsync(SourceOracle.OwnRepository, fixture.BaselinePreview).ConfigureAwait(false);
        string reportPath = Path.Combine(workspace.Root, "installation.json");
        string[] arguments = [workspace.Target, "--yes", "--agents", fixture.Agents, "--verbose", "--report", reportPath, .. fixture.BaselinePreview ? new[] { "--preview" } : []];
        string reportDirectory = Path.Combine(FixtureFiles.Reports, "preparation", Path.GetFileName(destination));
        _ = Directory.CreateDirectory(reportDirectory);
        var result = await workspace.Process.RunAsync(executable, arguments, workspace.Target).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, fixture.Name + ".txt"), (result.Output + result.Error).Replace(workspace.Root, "$WORKSPACE", StringComparison.Ordinal)).ConfigureAwait(false);
        if (File.Exists(reportPath))
            File.Copy(reportPath, Path.Combine(reportDirectory, fixture.Name + ".json"), true);
        result.RequireSuccess($"Published baseline installation {fixture.Name}");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(false));
        VerifyReport(report.RootElement, fixture);
        var origins = await oracle.VerifySkillsAsync(workspace.Target).ConfigureAwait(false);
        foreach (var origin in origins)
        {
            FixtureFiles.Require(fixture.BaselinePreview ? origin.Pin is not null : origin.Pin is null, $"Unexpected baseline channel/pin for {origin.LocalPath}.");
            if (origin.Pin is not null)
                FixtureFiles.Require((await oracle.SnapshotAsync(origin.Repository, origin.Pin).ConfigureAwait(false)).Commit == origin.Commit, $"Baseline pin resolves to different content: {origin.LocalPath}.");
        }
        var recommendations = report.RootElement.GetProperty("recommendedSkills").EnumerateArray().ToArray();
        string[] recommendedKeys = [.. recommendations.Select(skill => skill.GetProperty("sourceRepo").GetString() + "\n" + skill.GetProperty("installArg").GetString()).Order(StringComparer.Ordinal)];
        string[] installedKeys = [.. report.RootElement.GetProperty("installResults").EnumerateArray().Select(skill => skill.GetProperty("sourceRepo").GetString() + "\n" + skill.GetProperty("installArg").GetString()).Order(StringComparer.Ordinal)];
        FixtureFiles.Require(recommendedKeys.SequenceEqual(installedKeys), "Published installer did not execute every recommended installation.");
        foreach (string agent in fixture.Agents.Contains("claude-code", StringComparison.Ordinal) ? new[] { ".agents/skills", ".claude/skills" } : [".agents/skills"])
        {
            var expectedFolders = recommendations.Select(skill => agent + "/" + skill.GetProperty("localFolder").GetString()).Order(StringComparer.Ordinal);
            var actualFolders = origins.Where(origin => origin.LocalPath.StartsWith(agent + "/", StringComparison.Ordinal)).Select(origin => origin.LocalPath).Order(StringComparer.Ordinal);
            FixtureFiles.Require(expectedFolders.SequenceEqual(actualFolders), $"Incomplete published skill inventory in {agent}.");
        }
        foreach (var origin in origins.DistinctBy(origin => (origin.Repository, origin.Reference)))
            await oracle.EnsureUnmovedAsync(await oracle.SnapshotAsync(origin.Repository, origin.Reference).ConfigureAwait(false)).ConfigureAwait(false);
        if (companion is null)
            FixtureFiles.Require(!File.Exists(Path.Combine(workspace.Target, ".config/dotnet-tools.json")), "Unexpected baseline companion manifest.");
        else
            await CompanionExercise.RunAsync(workspace, companion).ConfigureAwait(false);
        string agents = await File.ReadAllTextAsync(Path.Combine(workspace.Target, "AGENTS.md")).ConfigureAwait(false);
        FixtureFiles.Require(agents.Contains("foundation-prompt-log:start", StringComparison.Ordinal), "Missing foundation directive.");
        foreach (var (name, block) in DirectiveOracle.Expected(directiveSource.Directory, fixture.Technologies))
            FixtureFiles.Require(agents.Contains(block, StringComparison.Ordinal), $"Published directive {name} differs from {directiveSource.Commit}.");
        await oracle.EnsureUnmovedAsync(directiveSource).ConfigureAwait(false);
        if (fixture.Agents.Contains("claude-code", StringComparison.Ordinal))
            FixtureFiles.Require((await File.ReadAllTextAsync(Path.Combine(workspace.Target, "CLAUDE.md")).ConfigureAwait(false)).Contains("AGENTS.md", StringComparison.Ordinal), "Missing Claude import.");
        var capturedAt = DateTimeOffset.UtcNow;
        string capture = Path.Combine(workspace.Root, "capture");
        string installed = Path.Combine(capture, "snapshot.zip");
        FixtureFiles.CaptureSnapshot(workspace.Target, installed);
        string sanitized = report.RootElement.GetRawText().Replace(workspace.Target.Replace("\\", "\\\\", StringComparison.Ordinal), "$TARGET", StringComparison.Ordinal)
            .Replace(workspace.Root.Replace("\\", "\\\\", StringComparison.Ordinal), "$WORKSPACE", StringComparison.Ordinal);
        using var sanitizedReport = JsonDocument.Parse(sanitized);
        FixtureFiles.WriteJson(Path.Combine(capture, "metadata.json"), new FixtureCapture(fixture.Name, capturedAt,
            $"Installed using {baseline.InstallerId} {baseline.InstallerVersion} on {capturedAt:O}", fixture, triggerHashes,
            FixtureFiles.Inventory(workspace.Target), sanitizedReport.RootElement.Clone(), origins, companion is null ? null : companion with { Path = Path.GetFileName(companion.Path) }, FixtureFiles.Hash(installed), new(directiveSource.Repository, directiveSource.Reference, directiveSource.Commit),
            ["agentic-check", .. arguments.Select(argument => argument.Replace(workspace.Target, "$TARGET", StringComparison.Ordinal).Replace(workspace.Root, "$WORKSPACE", StringComparison.Ordinal))], []));
        foreach (var origin in origins.Select(origin => new SourceIdentity(origin.Repository, origin.Reference, origin.Commit))
            .Append(new(directiveSource.Repository, directiveSource.Reference, directiveSource.Commit)).DistinctBy(origin => origin.Repository))
        {
            var source = await oracle.SnapshotAsync(origin.Repository, origin.Commit).ConfigureAwait(false);
            foreach (string notice in Directory.GetFiles(source.Directory).Where(file => Path.GetFileName(file).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)))
            {
                string target = Path.Combine(capture, "licenses", origin.Repository, Path.GetFileName(notice));
                _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(notice, target, false);
            }
        }
        // Stage on the destination volume so the final rename is also portable across temp/checkout drives.
        string staged = Path.Combine(destination, $".capture-{Guid.NewGuid():N}");
        try
        {
            FixtureFiles.Copy(capture, staged);
            Directory.Move(staged, Path.Combine(destination, fixture.Name));
        }
        finally
        {
            if (Directory.Exists(staged))
                Directory.Delete(staged, true);
        }
    }

    internal static void VerifyReport(JsonElement report, FixtureDefinition fixture)
    {
        string[] technologies = [.. report.GetProperty("technologies").EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];
        FixtureFiles.Require(technologies.SequenceEqual(fixture.Technologies.Order(StringComparer.Ordinal)), $"Technology detection mismatch for {fixture.Name}: {string.Join(',', technologies)}");
        foreach (var install in report.GetProperty("installResults").EnumerateArray())
            FixtureFiles.Require(install.GetProperty("success").GetBoolean(), $"Failed skill installation: {install}");
        foreach (var copy in report.GetProperty("skillCopyResults").EnumerateArray())
            FixtureFiles.Require(copy.GetProperty("success").GetBoolean(), $"Failed skill copy: {copy}");
        foreach (var directive in report.GetProperty("directives").EnumerateArray())
            FixtureFiles.Require(!directive.GetProperty("status").GetString()!.Contains("fail", StringComparison.OrdinalIgnoreCase), $"Failed directive: {directive}");
        var actualGates = report.GetProperty("installGates").EnumerateArray().SelectMany(gate => gate.GetProperty("values").EnumerateObject())
            .GroupBy(gate => gate.Name, StringComparer.Ordinal).ToDictionary(group => group.Key,
                group => group.SelectMany(gate => gate.Value.EnumerateArray()).Select(value => value.GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        FixtureFiles.Require(actualGates.Count == fixture.Gates.Count, $"Unexpected gate keys for {fixture.Name}");
        foreach (var (gate, values) in fixture.Gates)
            FixtureFiles.Require(actualGates.TryGetValue(gate, out string[]? actual) && actual.SequenceEqual(values.Order(StringComparer.Ordinal)), $"Wrong {gate} gate for {fixture.Name}");
        if (fixture.Name == "uno-conflicting-gates")
            FixtureFiles.Require(report.GetProperty("warnings").GetArrayLength() >= 3, "Expected three conflicting gate warnings.");
    }
}
