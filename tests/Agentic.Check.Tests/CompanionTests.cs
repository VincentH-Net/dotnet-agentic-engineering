using System.Net;
using System.Text.Json;

namespace Agentic.Check.Tests;

public sealed class CompanionTests
{
    [Fact]
    public void WorkingTreeSourceLocationAndConsumersAgree()
    {
        string root = CheckoutRoot();
        var version = ReadWorkingTreeVersion(root);
        string[] directives = Directory.GetFiles(Path.Combine(root, "directives"), "*.md");
        foreach (string path in directives)
        {
            var invocations = CompanionDependency.Invocations(File.ReadAllText(path));
            foreach (var (_, minimum) in invocations)
            {
                Assert.Equal(version.Minimum, minimum);
                Assert.Contains(CompanionDependency.Identity, CompanionDependency.ForDirective(Path.GetFileNameWithoutExtension(path)));
            }

            if (Path.GetFileName(path) == "foundation-prompt-log.md")
            {
                foreach (string command in new[] { "wrap", "show", "check" })
                {
                    Assert.Contains(invocations, invocation => invocation.Command.Contains("prompt-log " + command, StringComparison.Ordinal));
                }
            }
        }

        ValidateSkillConsumers(Path.Combine(root, "plugins"), version, [.. StaticSkillManifest.All, .. StaticSkillManifest.Preview]);
    }

    static void ValidateSkillConsumers(string plugins, ToolVersion version, IReadOnlyList<SkillManifestEntry> manifest)
    {
        foreach (string skill in Directory.EnumerateFiles(plugins, "SKILL.md", SearchOption.AllDirectories))
        {
            string folder = Path.GetDirectoryName(skill)!;
            foreach (string asset in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                foreach (var (_, minimum) in CompanionDependency.Invocations(File.ReadAllText(asset)))
                {
                    Assert.Equal(version.Minimum, minimum);
                    Assert.Contains(manifest, entry => entry.LocalFolder == Path.GetFileName(folder) && entry.SourceRepo == CompanionDependency.SourceRepo && entry.Dependencies.Contains(CompanionDependency.Identity));
                }
            }
        }
    }

    [Fact]
    public void SyntheticAuthoredSkillDiscoversInvocationsInNestedScripts()
    {
        using TempDirectory temp = new();
        temp.Write("plugin/skills/fixture-consumer/SKILL.md", "Run scripts/wrap.sh to wrap prompt logs.");
        temp.Write("plugin/skills/fixture-consumer/scripts/wrap.sh", "dotnet agentic --minver 2.3 prompt-log wrap \\\n --input entry --prompt-log block\n");
        ValidateSkillConsumers(temp.Path, ToolVersion.ParseMinimum("2.3"), [Consumer()]);
        _ = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateSkillConsumers(temp.Path, ToolVersion.ParseMinimum("2.3"), []));
    }

    internal static string CheckoutRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")) && !File.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate the working checkout independently of the companion project.");
    }

    internal static ToolVersion ReadWorkingTreeVersion(string root)
    {
        string path = root;
        const string explanation = "Agentic.Check retrieves the package version from this location on GitHub. A deliberate move requires updating the production path and related references together: ";
        foreach (string component in CompanionSourceVersionReader.ProjectPath.Split('/'))
        {
            Assert.True(Directory.EnumerateFileSystemEntries(path).Any(entry => Path.GetFileName(entry).Equals(component, StringComparison.Ordinal)), explanation + CompanionSourceVersionReader.ProjectPath);
            path = Path.Combine(path, component);
        }

        try
        {
            return CompanionSourceVersionReader.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException)
        {
            throw new InvalidOperationException(explanation + CompanionSourceVersionReader.ProjectPath, exception);
        }
    }

    [Theory]
    [InlineData("dotnet agentic -m 2.3 prompt-log show")]
    [InlineData("dotnet agentic --minver 2.3 prompt-log show")]
    [InlineData("dotnet agentic prompt-log wrap --input - --prompt-log out -m 2.3")]
    [InlineData("dotnet agentic prompt-log check --minver 2.3")]
    [InlineData("dotnet agentic prompt-log show \\\n  -m 2.3")]
    public void InvocationDiscoveryCoversSkillAssetsAndDirectives(string content)
        => Assert.Equal("2.3", Assert.Single(CompanionDependency.Invocations(content)).Minimum);

    [Theory]
    [InlineData("<Project />")]
    [InlineData("<Project><Version>2.3.0</Version><Version>2.4.0</Version></Project>")]
    [InlineData("<Project><Version>$(VersionPrefix)</Version></Project>")]
    [InlineData("<Project><Version>2.3.*</Version></Project>")]
    [InlineData("<Project><PropertyGroup Condition='x'><Version>2.3.0</Version></PropertyGroup></Project>")]
    public void UnreadableSourceVersionsFail(string xml)
        => Assert.Throws<FormatException>(() => CompanionSourceVersionReader.Parse(xml));

    [Theory]
    [InlineData("v2.3.0", 3600)]
    [InlineData("development", 3600)]
    [InlineData("development", 0)]
    public async Task SourceRequestUsesSharedPathRefAndExistingCache(string sourceRef, int duration)
    {
        using TempDirectory temp = new();
        using ProjectTransport transport = new();
        using HttpClient client = new(transport);
        DirectiveCacheSettings settings = new(duration, temp.Path, []);
        GitHubDirectiveSource source = new(client, settings);
        CompanionSourceVersionReader reader = new(source);
        Assert.Equal("2.3", (await reader.ReadAsync(sourceRef, CancellationToken.None)).Minimum);
        _ = await reader.ReadAsync(sourceRef, CancellationToken.None);
        _ = Assert.Single(transport.Requests);
        Assert.EndsWith($"/{sourceRef}/{CompanionSourceVersionReader.ProjectPath}", transport.Requests[0], StringComparison.Ordinal);
        _ = await new CompanionSourceVersionReader(source).ReadAsync(sourceRef, CancellationToken.None);
        Assert.Equal(duration == 0 ? 2 : 1, transport.Requests.Count);
        _ = await reader.ReadAsync("another-ref", CancellationToken.None);
        Assert.Equal(duration == 0 ? 3 : 2, transport.Requests.Count);
    }

    [Fact]
    public async Task FetchFailureDoesNotGuessVersion()
    {
        using TempDirectory temp = new();
        using ProjectTransport transport = new() { Status = HttpStatusCode.NotFound };
        using HttpClient client = new(transport);
        CompanionSourceVersionReader reader = new(new GitHubDirectiveSource(client, new(0, temp.Path, [])));
        _ = await Assert.ThrowsAsync<DirectiveException>(() => reader.ReadAsync("v2.3.0", CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceContentAndProjectUseSameResolvedStableOrPreviewRevision(bool preview)
    {
        using TempDirectory temp = new();
        using RevisionTransport transport = new();
        using HttpClient client = new(transport);
        GitHubDirectiveSource source = new(client, new(0, temp.Path, []), sourceVersionMode: preview ? SourceVersionMode.Preview : SourceVersionMode.Stable);
        var files = await source.ListAsync(CancellationToken.None);
        var file = Assert.Single(files);
        string expectedRef = preview ? RevisionTransport.CommitSha : "v2.3.0";
        Assert.Equal(expectedRef, file.SourceRef);
        _ = await new CompanionSourceVersionReader(source).ReadAsync(file.SourceRef, CancellationToken.None);
        Assert.Contains(transport.Requests, url => url.EndsWith("/" + expectedRef + "/" + CompanionSourceVersionReader.ProjectPath, StringComparison.Ordinal));
        Assert.Contains(transport.Requests, url => url.EndsWith("/contents/directives?ref=" + expectedRef, StringComparison.Ordinal));
        if (preview)
        {
            Assert.Contains(transport.Requests, url => url.EndsWith("/branches/development", StringComparison.Ordinal));
            Assert.DoesNotContain(transport.Requests, url => url.Contains("/main", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(null, "2.4.0", false, true)]
    [InlineData("2.8.0", "2.4.0", false, true)]
    [InlineData("3.0.0", "2.4.0", false, true)]
    [InlineData("2.5.0-preview.1", "2.4.0", false, true)]
    [InlineData(null, "2.4.0-preview.1", true, true)]
    [InlineData(null, "2.4.0", true, true)]
    [InlineData(null, "2.2.99", false, false)]
    [InlineData(null, "3.0.0", true, false)]
    [InlineData(null, "2.4.0-preview.1", false, false)]
    public async Task ManifestVerificationAndSdkArguments(string? installed, string resolved, bool preview, bool success)
    {
        using TempDirectory temp = new();
        string target = temp.CreateDirectory("parent/child");
        WriteManifest(temp.Path, "9.0.0");
        WriteManifest(target, installed);
        string parent = await File.ReadAllTextAsync(CompanionInstaller.ManifestPath(temp.Path));
        ToolRunner runner = new() { Resolved = resolved };
        var result = await new CompanionInstaller(runner).EnsureAsync(target, ToolVersion.ParseMinimum("2.3"), preview, false, false, CancellationToken.None);
        Assert.Equal(success, result.Success);
        Assert.Equal(resolved, result.ResolvedVersion);
        Assert.Equal(installed != resolved, result.Changed);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(target, call.WorkingDirectory);
        Assert.Contains(CompanionInstaller.ManifestPath(target), call.Arguments);
        Assert.Contains(preview ? "2.*-*" : "2.*", call.Arguments);
        Assert.Contains("--allow-downgrade", call.Arguments);
        Assert.Equal(installed is null, call.Arguments.Contains("--allow-roll-forward"));
        Assert.DoesNotContain("--source", call.Arguments);
        Assert.Equal(parent, await File.ReadAllTextAsync(CompanionInstaller.ManifestPath(temp.Path)));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(CompanionInstaller.ManifestPath(target)));
        Assert.True(manifest.RootElement.GetProperty("isRoot").GetBoolean());
        Assert.Equal("1.0.0", manifest.RootElement.GetProperty("tools").GetProperty("unrelated").GetProperty("version").GetString());
    }

    [Fact]
    public async Task DryRunAndExactRestore()
    {
        using TempDirectory temp = new();
        string target = temp.CreateDirectory("target");
        ToolRunner runner = new();
        var dry = await new CompanionInstaller(runner).EnsureAsync(target, ToolVersion.ParseMinimum("2.3"), true, false, true, CancellationToken.None);
        Assert.True(dry.Success);
        Assert.Equal("2.*-*", dry.Pattern);
        Assert.Empty(runner.Calls);
        Assert.False(File.Exists(dry.ManifestPath));
        WriteManifest(target, "1.4.5");
        var restored = await new CompanionInstaller(runner).EnsureAsync(target, ToolVersion.ParseMinimum("1.3"), false, true, false, CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal("1.4.5", restored.ResolvedVersion);
        Assert.Equal(["tool", "restore", "--tool-manifest", restored.ManifestPath], Assert.Single(runner.Calls).Arguments);
        var incompatible = await new CompanionInstaller(runner).EnsureAsync(target, ToolVersion.ParseMinimum("2.3"), false, true, false, CancellationToken.None);
        Assert.False(incompatible.Success);
        _ = Assert.Single(runner.Calls);
    }

    [Fact]
    public void GraphSelectsOnceAndDeselectsTransitiveConsumers()
    {
        var consumer = Consumer();
        SkillManifestEntry transitive = new("fixture", "transitive", "transitive", TechnologyNames.Dotnet, [], dependencies: [new(consumer.SourceRepo, consumer.InstallArg)]);
        var companion = CompanionDependency.Action();
        DirectivePlanItem directive = new("foundation-prompt-log", "missing", "content");
        var items = RecommendationSelectionPrompt.BuildItems([directive], [consumer, transitive, companion]);
        RecommendationSelectionState state = new(items);
        state.Apply(new(SkillSelectionCommand.SelectNone));
        state.SelectWithDependencies(RecommendationSelectionState.FormatSkillKey(transitive.SourceRepo, transitive.InstallArg));
        Assert.Equal(3, state.SelectedSkills.Count);
        state.SelectWithDependencies("directive:" + directive.Name);
        _ = Assert.Single(state.SelectedSkills, entry => entry.IsCompanion);
        state.DeselectWithDependents(RecommendationSelectionState.FormatSkillKey(companion.SourceRepo, companion.InstallArg));
        Assert.Empty(state.SelectedSkills);
        Assert.Empty(state.SelectedDirectives);
        state.Apply(new(SkillSelectionCommand.SelectAll));
        state.ApplySpecializationScanResult(new(new Dictionary<string, IReadOnlyList<string>> { [items[^1].Key] = ["../.agents/skills/InnoWvate.Agentic/SKILL.md"] }));
        Assert.Contains(companion, state.SelectedSkills);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SeparateStableUpdatePromptEnforcesPrerequisiteWithNoSelectedRecommendations(bool accept, bool fail)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        temp.Write(".agents/skills/fixture-consumer/SKILL.md", "old content");
        var consumer = Consumer();
        ToolRunner runner = new() { Fail = fail, UpdateCandidate = true };
        FakePrompts prompts = new() { ConfirmResult = accept, SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, prompts, reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(), [consumer]);
        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Contains("Update these skill(s)?", prompts.ConfirmPrompts);
        Assert.Equal(accept, runner.Calls.Any(call => call.FileName == "dotnet" && call.Arguments.Contains("install")));
        bool updated = runner.Calls.Any(call => call.FileName == "gh" && call.Arguments.Contains("update") && !call.Arguments.Contains("--dry-run"));
        Assert.Equal(accept && !fail, updated);
        if (updated)
        {
            Assert.True(runner.Calls.FindIndex(call => call.FileName == "dotnet") < runner.Calls.FindLastIndex(call => call.FileName == "gh"));
        }

        Assert.Equal(accept && fail ? 1 : 0, result.ExitCode);
        if (accept && fail)
        {
            Assert.DoesNotContain(reporter.Successes, message => message.Contains("successfully", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(true, "2.4.0")]
    [InlineData(false, "2.2.0")]
    public async Task FailedPrerequisiteCannotApplyDependentDirective(bool commandFailure, string resolved)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        ToolRunner runner = new() { Fail = commandFailure, Resolved = resolved };
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver());
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("foundation-prompt-log:start", await File.ReadAllTextAsync(result.Report.AgentsFile), StringComparison.Ordinal);
        Assert.NotNull(result.Report.Companion);
        Assert.False(result.Report.Companion.Success);
    }

    internal static SkillManifestEntry Consumer()
        => new(CompanionDependency.SourceRepo, "fixture-consumer", "fixture-consumer", TechnologyNames.Dotnet, [], dependencies: [CompanionDependency.Identity]);

    [Theory]
    [InlineData("1.4.0", true, "restore")]
    [InlineData("3.0.0", false, "update")]
    [InlineData(null, false, "install")]
    public async Task RepairUsesInstalledConsumerRequirementWithoutFetchingNewProject(string? installed, bool notRestored, string action)
    {
        using TempDirectory temp = new();
        const string block = "<!-- dotnet-agentic-engineering:foundation-prompt-log:start -->\n## Prompt log\ndotnet agentic --minver 1.3 prompt-log show\n<!-- dotnet-agentic-engineering:foundation-prompt-log:end -->\n";
        temp.Write("AGENTS.md", block);
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + block + "~~~\n" });
        if (installed is not null)
        {
            WriteManifest(temp.Path, installed);
        }

        ToolRunner runner = new() { Resolved = "1.4.0", NotRestored = notRestored };
        FakePrompts prompts = new();
        CheckWorkflow workflow = new(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver());
        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, source.ProjectFetches);
        var recommendation = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Equal(action, recommendation.RecommendationAction);
        Assert.True(recommendation.IsRequiredToolRepair);
        Assert.NotNull(result.Report.Companion);
        Assert.Equal(action, result.Report.Companion.Action);
        Assert.Equal("1.3", result.Report.Companion.RequiredMinimum);
        Assert.Equal("1.4.0", result.Report.Companion.ResolvedVersion);
        Assert.Equal(block, await File.ReadAllTextAsync(result.Report.AgentsFile));
        _ = Assert.Single(runner.Calls, call => call.FileName == "dotnet" && call.Arguments[1] == action && !call.Arguments.Contains("--global"));
        if (installed == "3.0.0")
        {
            Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments[1] is "restore" or "run");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleConsumersReadProjectAndPrepareCompanionOnce(bool dryRun)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        ToolRunner runner = new();
        FakeDirectiveSource source = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(), source, new FakeSourceVersionResolver(), [Consumer()]);
        var result = await workflow.RunAsync(new(temp.Path, dryRun, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, source.ProjectFetches);
        Assert.Equal(dryRun ? 0 : 1, runner.Calls.Count(call => call.FileName == "dotnet" && call.Arguments.Contains(CompanionDependency.PackageId)));
        if (dryRun)
            Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments[1] is "install" or "update" or "restore");
        Assert.NotNull(result.Report.Companion);
        Assert.Equal(dryRun, !File.Exists(result.Report.Companion.ManifestPath));
    }

    [Fact]
    public async Task MalformedSourceVersionAndConflictingLocalRequirementsFailWithoutConsumerWrites()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        FakeDirectiveSource source = new() { ProjectContent = "<Project><Version>$(Expression)</Version></Project>" };
        ToolRunner runner = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), new RecordingReporter(), source, new FakeSourceVersionResolver());
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments[1] != "list");
        Assert.DoesNotContain("foundation-prompt-log:start", await File.ReadAllTextAsync(result.Report.AgentsFile), StringComparison.Ordinal);
        _ = Assert.Throws<FormatException>(() => CompanionDependency.ReadLocalRequirement(["dotnet agentic prompt-log show -m 1.3", "dotnet agentic --minver 2.3 prompt-log check"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentToolChoiceIsHonoredWithoutSelectedConsumers(bool selectTool)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        ToolRunner runner = new();
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = selectTool ? [CompanionDependency.PackageId] : [] };
        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver())
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(selectTool ? 1 : 0, runner.Calls.Count(call => call.FileName == "dotnet" && call.Arguments[1] == "install"));
        Assert.Equal(selectTool, File.Exists(CompanionInstaller.ManifestPath(temp.Path)));
        Assert.DoesNotContain("foundation-prompt-log:start", await File.ReadAllTextAsync(result.Report.AgentsFile), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "install", "required 2.3")]
    [InlineData("2.2.0", "update", "currently 2.2.0; required 2.3")]
    [InlineData("2.3.0", "update", "currently 2.3.0; required 2.3")]
    public async Task ToolRecommendationDescribesPlannedActionAndCurrentVersion(string? installed, string action, string detail)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        if (installed is not null)
            WriteManifest(temp.Path, installed);
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };
        var result = await new CheckWorkflow(new ToolRunner(), prompts, new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver())
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var recommendation = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Equal($"InnoWvate.Agentic ({action})", RecommendationSelectionPrompt.FormatSkillListItem(recommendation));
        Assert.Equal(detail, recommendation.Version);
        Assert.False(recommendation.IsRequiredToolRepair);
    }

    internal static void WriteManifest(string target, string? version)
    {
        string manifest = CompanionInstaller.ManifestPath(target);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        var tools = new System.Text.Json.Nodes.JsonObject { ["unrelated"] = new System.Text.Json.Nodes.JsonObject { ["version"] = "1.0.0", ["commands"] = new System.Text.Json.Nodes.JsonArray("other") } };
        if (version is not null)
        {
            tools["innowvate.agentic"] = new System.Text.Json.Nodes.JsonObject { ["version"] = version, ["commands"] = new System.Text.Json.Nodes.JsonArray("agentic") };
        }

        File.WriteAllText(manifest, new System.Text.Json.Nodes.JsonObject { ["version"] = 1, ["isRoot"] = true, ["tools"] = tools }.ToJsonString());
    }
}

sealed class ToolRunner : ICommandRunner
{
    internal List<CommandCall> Calls { get; } = [];
    internal string Resolved { get; init; } = "2.3.0";
    internal bool Fail { get; init; }
    internal bool UpdateCandidate { get; init; }
    internal bool NotRestored { get; init; }

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        Calls.Add(new(fileName, arguments, workingDirectory) { Environment = environment });
        if (AuthenticationTestCommands.Response(fileName, arguments) is { } authentication)
            return Task.FromResult(authentication);
        if (fileName == "dotnet")
        {
            if (arguments.Contains("--global"))
                return Task.FromResult(CompanionTestCommands.Succeed(arguments, workingDirectory));
            if (Fail || (NotRestored && arguments[1] == "run"))
            {
                return Task.FromResult(new CommandResult(1, "", "fixture SDK failure"));
            }

            if (arguments[1] is "install" or "update")
            {
                CompanionTests.WriteManifest(workingDirectory, Resolved);
            }

            return Task.FromResult(new CommandResult(0, arguments[1] == "run" ? Resolved : "localized SDK output", ""));
        }

        return Task.FromResult(new CommandResult(0, arguments.Contains("--version") ? "gh version 2.93.0" : UpdateCandidate && arguments.Contains("--dry-run") ? $"Would update fixture-consumer ({CompanionDependency.SourceRepo})" : "ok", ""));
    }
}

sealed class ProjectTransport : HttpMessageHandler
{
    internal List<string> Requests { get; } = [];
    internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("<Project><Version>2.3.0-preview.1</Version></Project>") });
    }
}

sealed class RevisionTransport : HttpMessageHandler
{
    internal const string CommitSha = "1234567890123456789012345678901234567890";
    internal List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string uri = request.RequestUri!.AbsoluteUri;
        Requests.Add(uri);
        string content = uri.Contains("/releases/latest", StringComparison.Ordinal)
            ? "{\"tag_name\":\"v2.3.0\",\"published_at\":\"2026-09-01T00:00:00Z\"}"
            : uri.Contains("/branches/", StringComparison.Ordinal)
                ? "{\"commit\":{\"sha\":\"" + CommitSha + "\",\"commit\":{\"committer\":{\"date\":\"2026-09-01T00:00:00Z\"}}}}"
                : uri.Contains("/contents/directives", StringComparison.Ordinal)
                    ? "[{\"name\":\"foundation-prompt-log.md\",\"type\":\"file\",\"download_url\":\"https://example.test/directive\"}]"
                    : uri.EndsWith(CompanionSourceVersionReader.ProjectPath, StringComparison.Ordinal)
                        ? "<Project><Version>2.3.0</Version></Project>"
                        : "{\"default_branch\":\"development\"}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
    }
}
