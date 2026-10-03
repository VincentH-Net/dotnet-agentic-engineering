using System.Net;
using System.Text.Json;

namespace Agentic.Check.Tests;

public sealed class CompanionTests
{
    // A run that applies no directive creates no AGENTS.md.
    static async Task<string> ReadIfExistsAsync(string path)
        => File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : string.Empty;

    [Fact]
    public void WorkingTreeSourceLocationAndConsumersAgree()
    {
        string root = CheckoutRoot();
        var version = ReadWorkingTreeVersion(root);
        string[] directives = Directory.GetFiles(Path.Combine(root, "directives"), "*.md");
        foreach (string path in directives)
        {
            string content = File.ReadAllText(path);
            var invocations = CompanionDependency.Invocations(content);
            foreach (var (_, minimum) in invocations)
            {
                AssertWithinToolVersion(version, minimum, path);
                Assert.Contains(CompanionDependency.Identity, CompanionDependency.ForDirective(Path.GetFileNameWithoutExtension(path), content));
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
                    AssertWithinToolVersion(version, minimum, asset);
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
        // The content keeps its 2.3 while the tool moves on within the major; it may not ask for a
        // minor the tool has not reached, nor for another major.
        ValidateSkillConsumers(temp.Path, ToolVersion.ParseMinimum("2.4"), [Consumer()]);
        _ = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateSkillConsumers(temp.Path, ToolVersion.ParseMinimum("2.2"), [Consumer()]));
        _ = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => ValidateSkillConsumers(temp.Path, ToolVersion.ParseMinimum("3.0"), [Consumer()]));
    }

    // -m is the lowest minor of the tool's major that content needs: the tool being released with it
    // must satisfy it, exactly as the tool itself decides at run time.
    static void AssertWithinToolVersion(ToolVersion version, string? minimum, string source)
    {
        Assert.True(minimum is not null, $"{source} calls dotnet agentic without a literal -m / --minver.");
        Assert.True(version.Satisfies(ToolVersion.ParseMinimum(minimum)), $"{source} asks for {minimum}, which the tool version {version} does not satisfy.");
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
        DirectivePlanItem directive = new("foundation-prompt-log", "missing", "dotnet agentic prompt-log show -m 2.3");
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
    [InlineData("git log --grep=\"^prompt-log:\"", false)]
    [InlineData("dotnet agentic prompt-log show -m 2.3", true)]
    public void DirectiveSelectionDependsOnItsCommands(string content, bool requiresCompanion)
    {
        DirectivePlanItem directive = new("foundation-prompt-log", "missing", content);
        var companion = CompanionDependency.Action();
        RecommendationSelectionState state = new(RecommendationSelectionPrompt.BuildItems([directive], [companion]));
        state.Apply(new(SkillSelectionCommand.SelectNone));
        state.SelectWithDependencies("directive:" + directive.Name);

        Assert.Equal(requiresCompanion, state.SelectedSkills.Contains(companion));
        state.DeselectWithDependents(RecommendationSelectionState.FormatSkillKey(companion.SourceRepo, companion.InstallArg));
        Assert.Equal(!requiresCompanion, state.SelectedDirectives.Contains(directive));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HistoricalDirectiveInstallsAndMigratesWithoutCompanion(bool dryRun, bool installed)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        string block = PromptLogBlock("git log --grep=\"^prompt-log:\"", "dotnet-agentic-engineering:");
        if (installed)
            temp.Write("AGENTS.md", "User instructions\n" + block);
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + block + "~~~\n" })
        {
            ProjectFailure = new DirectiveException("GitHub returned HTTP 404")
        };
        ToolRunner runner = new();
        FakePrompts prompts = new();
        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [])
            .RunAsync(new(temp.Path, dryRun, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, source.ProjectFetches);
        Assert.Null(result.Report.Companion);
        Assert.Null(result.Report.Dna);
        Assert.DoesNotContain(prompts.RecommendedSkillActions, skill => skill.IsCompanion || skill.IsDna);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet");
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(temp.Path)));
        if (dryRun)
        {
            Assert.Equal(installed, File.Exists(result.Report.AgentsFile));
            if (installed)
                Assert.Equal("User instructions\n" + block, await ReadIfExistsAsync(result.Report.AgentsFile));
        }
        else
        {
            string content = await ReadIfExistsAsync(result.Report.AgentsFile);
            Assert.Contains(DirectiveMarkers.Normalize(block, "foundation-prompt-log").TrimEnd(), content, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet-agentic-engineering:", content, StringComparison.Ordinal);
            if (installed)
                Assert.StartsWith("User instructions\n", content, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("dotnet-agentic-engineering:")]
    public async Task SwitchingToHistoricalDirectivePreservesInstalledTool(string prefix)
    {
        using TempDirectory temp = new();
        temp.Write("AGENTS.md", PromptLogBlock("dotnet agentic prompt-log show -m 2.3", prefix));
        WriteManifest(temp.Path, "2.3.0");
        string manifest = await File.ReadAllTextAsync(CompanionInstaller.ManifestPath(temp.Path));
        string block = PromptLogBlock("git log --grep=\"^prompt-log:\"", "dotnet-agentic-engineering:");
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + block + "~~~\n" })
        {
            ProjectFailure = new DirectiveException("GitHub returned HTTP 404")
        };
        ToolRunner runner = new();
        var result = await new CheckWorkflow(runner, new FakePrompts(), new RecordingReporter(), source, new FakeSourceVersionResolver(), [], new DnaInstaller(runner, string.Empty))
            .RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, source.ProjectFetches);
        Assert.Null(result.Report.Companion);
        Assert.NotNull(result.Report.Dna);
        Assert.True(result.Report.Dna.Success);
        Assert.Equal("update", result.Report.Dna.Action);
        Assert.Equal(manifest, await File.ReadAllTextAsync(CompanionInstaller.ManifestPath(temp.Path)));
        Assert.Contains(DirectiveMarkers.Normalize(block, "foundation-prompt-log").TrimEnd(), await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
        Assert.Equal(["tool", "run", "agentic", "--", "--version"], Assert.Single(runner.Calls, call => call.FileName == "dotnet" && !call.Arguments.Contains("--global")).Arguments);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("uninstall"));
    }

    static string PromptLogBlock(string commands, string prefix)
        => $"<!-- {prefix}foundation-prompt-log:start -->\n## Prompt log\n{commands}\n<!-- {prefix}foundation-prompt-log:end -->\n";

    // Nothing installed needs the companion, but a 2.3.0 is pinned and the offered directive needs 2.4.
    // Keeping the companion update while declining the directive must update along the installed 2.3
    // line, which nuget.org can satisfy, and the row must say so.
    [Fact]
    public async Task KeptCompanionUpdateFollowsTheInstalledLineWhenNothingInstalledNeedsIt()
    {
        using TempDirectory temp = new();
        WriteManifest(temp.Path, "2.3.0");
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + PromptLogBlock("dotnet agentic prompt-log show -m 2.4", "") + "~~~\n" })
        {
            ProjectContent = "<Project><Version>2.4.0</Version></Project>"
        };
        ToolRunner runner = new();
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [], new DnaInstaller(runner, string.Empty))
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var companion = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.StartsWith("currently 2.3.0; required 2.4", companion.Version, StringComparison.Ordinal);
        Assert.StartsWith("currently 2.3.0; required 2.3", companion.VersionWithoutConsumers, StringComparison.Ordinal);
        Assert.NotNull(result.Report.Companion);
        Assert.True(result.Report.Companion.Success);
        Assert.Equal("update", result.Report.Companion.Action);
        Assert.Equal("2.3", result.Report.Companion.RequiredMinimum);
        _ = Assert.Single(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global"));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["tool", "restore", ..]);
        Assert.False(File.Exists(Path.Combine(temp.Path, "AGENTS.md")));
    }

    // The installed directive needs 2.3 and the companion 2.3.0 satisfies it; the offered directive
    // update needs 2.4. Keeping the companion update while declining the directive update must update
    // against 2.3, not restore, and the row must say so.
    [Fact]
    public async Task KeptCompanionUpdateFollowsTheInstalledRequirementWhenItsDirectiveUpdateIsDeselected()
    {
        using TempDirectory temp = new();
        temp.Write("AGENTS.md", PromptLogBlock("dotnet agentic prompt-log show -m 2.3", ""));
        WriteManifest(temp.Path, "2.3.0");
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + PromptLogBlock("dotnet agentic prompt-log show -m 2.4", "") + "~~~\n" })
        {
            ProjectContent = "<Project><Version>2.4.0</Version></Project>"
        };
        ToolRunner runner = new();
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [], new DnaInstaller(runner, string.Empty))
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var companion = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Equal("update", companion.RecommendationAction);
        Assert.StartsWith("currently 2.3.0; required 2.4", companion.Version, StringComparison.Ordinal);
        Assert.StartsWith("currently 2.3.0; required 2.3", companion.VersionWithoutConsumers, StringComparison.Ordinal);
        Assert.NotNull(result.Report.Companion);
        Assert.True(result.Report.Companion.Success);
        Assert.Equal("update", result.Report.Companion.Action);
        Assert.Equal("2.3", result.Report.Companion.RequiredMinimum);
        var update = Assert.Single(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global"));
        Assert.Contains("2.*", update.Arguments);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["tool", "restore", ..]);
        Assert.Contains("-m 2.3", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
    }

    // The directive is installed and current, so nothing in the run depends on the companion. A pinned
    // companion still follows the newest stable version in its major: a newer one is offered as an
    // ordinary row, and one that is already the latest is reported as current without a row.
    [Theory]
    [InlineData("2.4.0", "2.4.1", "currently 2.4.0; required 2.4; latest 2.4.1")]
    [InlineData("2.4.1", "2.4.1", null)]
    [InlineData("2.4.2", "2.4.1", null)]
    public async Task InstalledCompanionFollowsTheNewestVersionInItsMajorWithoutDependentRecommendations(string installed, string published, string? offered)
    {
        using TempDirectory temp = new();
        var (source, runner, versions) = CurrentPromptLogRepository(temp, installed, published);
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };
        RecordingReporter reporter = new();

        var result = await new CheckWorkflow(runner, prompts, reporter, source, new FakeSourceVersionResolver(), [],
            new DnaInstaller(new DnaRunner(runner) { Version = "1.0.0" }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, source.ProjectFetches);
        var companion = prompts.RecommendedSkillActions.SingleOrDefault(skill => skill.IsCompanion);
        Assert.Equal(offered, companion?.Version);
        Assert.NotNull(result.Report.Companion);
        Assert.True(result.Report.Companion.Success);
        Assert.Equal("2.4", result.Report.Companion.RequiredMinimum);
        if (offered is null)
        {
            Assert.Equal("current", result.Report.Companion.Action);
            Assert.Contains($"current InnoWvate.Agentic: installed {installed}, required 2.4, pattern 2.*, already the latest", result.Report.Actions);
            Assert.Contains($"  ✓ InnoWvate.Agentic {installed}", reporter.Successes);
            Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "install" or "update" or "restore", ..] && !call.Arguments.Contains("--global"));
            Assert.Equal(installed, CompanionInstaller.InstalledVersion(temp.Path));
        }
        else
        {
            Assert.Equal("update", companion!.RecommendationAction);
            Assert.False(companion.IsRequiredToolRepair);
            Assert.Equal("update", result.Report.Companion.Action);
            Assert.Equal(installed, result.Report.Companion.InstalledVersion);
            Assert.Equal(published, result.Report.Companion.ResolvedVersion);
            var update = Assert.Single(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global"));
            Assert.Contains("2.*", update.Arguments);
            Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["tool", "restore", ..]);
            Assert.Equal(published, CompanionInstaller.InstalledVersion(temp.Path));
        }
        Assert.Contains("-m 2.4", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
    }

    // The offered update is an ordinary row: declining it changes nothing, and --yes applies it.
    [Theory]
    [InlineData(false, false, "2.4.0")]
    [InlineData(false, true, "2.4.1")]
    [InlineData(true, false, "2.4.1")]
    public async Task OfferedCompanionUpdateIsAppliedOnlyWhenSelected(bool yes, bool select, string expected)
    {
        using TempDirectory temp = new();
        var (source, runner, versions) = CurrentPromptLogRepository(temp, "2.4.0", "2.4.1");
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = select ? [CompanionDependency.PackageId] : [] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [],
            new DnaInstaller(new DnaRunner(runner) { Version = "1.0.0" }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, yes, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, CompanionInstaller.InstalledVersion(temp.Path));
        Assert.Equal(expected == "2.4.0" ? null : "update", result.Report.Companion?.Action);
        Assert.Equal(expected == "2.4.0" ? 0 : 1, runner.Calls.Count(call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global")));
    }

    // Offering needs a known newer version. Without a feed answer the run neither offers a blind update
    // nor claims the companion is current.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledCompanionIsLeftAloneWhenTheLatestVersionIsUnknown(bool unlisted)
    {
        using TempDirectory temp = new();
        var (source, runner, _) = CurrentPromptLogRepository(temp, "2.4.0", "2.4.1");
        FakeVersionSource? versions = unlisted
            ? new(new Dictionary<string, string[]?>(StringComparer.Ordinal) { [CompanionDependency.PackageId] = null, [DnaInstaller.PackageId] = ["1.0.0"] })
            : null;
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [],
            new DnaInstaller(new DnaRunner(runner) { Version = "1.0.0" }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Null(result.Report.Companion);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "install" or "update" or "restore", ..] && !call.Arguments.Contains("--global"));
        Assert.Equal("2.4.0", CompanionInstaller.InstalledVersion(temp.Path));
    }

    // A companion that nothing installed uses is still pinned, so it follows its own line; a newer
    // version in another major is not an update for it.
    [Theory]
    [InlineData("2.4.1", "currently 2.4.0; required 2.4; latest 2.4.1")]
    [InlineData("2.4.0", null)]
    public async Task PinnedCompanionWithoutConsumersFollowsItsOwnMajor(string published, string? offered)
    {
        using TempDirectory temp = new();
        WriteManifest(temp.Path, "2.4.0");
        ToolRunner runner = new() { Resolved = "2.4.0", Updated = published };
        FakeVersionSource versions = new(new Dictionary<string, string[]?>(StringComparer.Ordinal)
        {
            [CompanionDependency.PackageId] = [published, "3.0.0", "2.5.0-preview.1"],
            [DnaInstaller.PackageId] = ["1.0.0"]
        });
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [],
            new DnaInstaller(new DnaRunner(runner) { Version = "1.0.0" }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(offered, prompts.RecommendedSkillActions.SingleOrDefault(skill => skill.IsCompanion)?.Version);
        Assert.Equal(offered is null ? "current" : null, result.Report.Companion?.Action);
        Assert.Equal("2.4.0", CompanionInstaller.InstalledVersion(temp.Path));
    }

    // Nothing is pinned and nothing needs the companion: a published version is no reason to install it.
    [Fact]
    public async Task AbsentCompanionIsNotOfferedWhenNothingNeedsIt()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        ToolRunner runner = new();
        FakeVersionSource versions = new(new Dictionary<string, string[]?>(StringComparer.Ordinal) { [CompanionDependency.PackageId] = ["2.4.1"] });
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(), [],
            new DnaInstaller(runner, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Null(result.Report.Companion);
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(temp.Path)));
    }

    // A pinned companion that satisfies the installed directive but is not restored is repaired. With a
    // known newer version that repair is the update itself, not a restore of the older version.
    [Theory]
    [InlineData("2.4.1", "update", "currently 2.4.0; required 2.4; latest 2.4.1")]
    [InlineData("2.4.0", "restore", "currently 2.4.0; required 2.4")]
    public async Task UnrestoredCompanionTakesAKnownNewerVersionInsteadOfARestore(string published, string action, string status)
    {
        using TempDirectory temp = new();
        var (source, _, versions) = CurrentPromptLogRepository(temp, "2.4.0", published);
        ToolRunner runner = new() { Resolved = "2.4.0", Updated = published, NotRestored = true };
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };

        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver(), [],
            new DnaInstaller(new DnaRunner(runner) { Version = "1.0.0" }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var companion = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Equal(action, companion.RecommendationAction);
        Assert.Equal(status, companion.Version);
        Assert.True(companion.IsRequiredToolRepair);
        Assert.Equal(action, result.Report.Companion?.Action);
        Assert.Equal(action == "update" ? 1 : 0, runner.Calls.Count(call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global")));
        Assert.Equal(action == "restore" ? 1 : 0, runner.Calls.Count(call => call.Arguments is ["tool", "restore", ..]));
        Assert.Equal(published, CompanionInstaller.InstalledVersion(temp.Path));
    }

    // -m is the lowest minor of a major that content was written for, so content at different minors
    // of one major is compatible and the tool must satisfy the highest.
    [Theory]
    [InlineData("2.3", "2.3", "2.3")]
    [InlineData("2.3", "2.4", "2.4")]
    [InlineData("2.4", "2.3", "2.4")]
    [InlineData("2.10", "2.9", "2.10")]
    public void InstalledContentAtDifferentMinorsOfOneMajorRequiresTheHighest(string directive, string skill, string expected)
    {
        var requirement = CompanionDependency.ReadLocalRequirement([
            ("AGENTS.md (foundation-prompt-log)", $"dotnet agentic prompt-log show -m {directive}\ndotnet agentic prompt-log check -m {directive}"),
            (".agents/skills/sample/SKILL.md", $"dotnet agentic --minver {skill} prompt-log wrap --input -")]);

        Assert.Equal(expected, requirement.Minimum);
    }

    [Fact]
    public void InstalledContentForDifferentMajorsNamesWhatDisagreesAndHowToResolveIt()
    {
        var error = Assert.Throws<FormatException>(() => CompanionDependency.ReadLocalRequirement([
            ("AGENTS.md (foundation-prompt-log)", "dotnet agentic prompt-log show -m 3.0"),
            (".agents/skills/sample/SKILL.md", "dotnet agentic prompt-log wrap --input - -m 2.4\ndotnet agentic prompt-log check -m 2.3")]));

        Assert.Equal("The installed directives and skills ask for different major versions of InnoWvate.Agentic: AGENTS.md (foundation-prompt-log) 3.0; .agents/skills/sample/SKILL.md 2.4, 2.3. Run `dna check` and apply the pending updates for these items so they ask for the same major.", error.Message);
    }

    [Fact]
    public void InstalledContentWithoutALiteralMinimumNamesTheFile()
    {
        var missing = Assert.Throws<FormatException>(() => CompanionDependency.ReadLocalRequirement([(".agents/skills/sample/SKILL.md", "dotnet agentic prompt-log show")]));
        Assert.Equal(".agents/skills/sample/SKILL.md calls dotnet agentic without a literal -m / --minver.", missing.Message);
        var none = Assert.Throws<FormatException>(() => CompanionDependency.ReadLocalRequirement([(".agents/skills/sample/SKILL.md", "No tool call here.")]));
        Assert.Contains("state no -m / --minver", none.Message, StringComparison.Ordinal);
    }

    // The installed directive was written for 2.3 and an installed skill for 2.4. They share a major,
    // so nothing conflicts: the pinned tool must satisfy the higher one, and is updated when it does not.
    [Theory]
    [InlineData("2.4.0", null, null)]
    [InlineData("2.3.0", "update", "currently 2.3.0; required 2.4")]
    public async Task InstalledContentAtDifferentMinorsIsServedByOneToolAtTheHighest(string installed, string? action, string? status)
    {
        using TempDirectory temp = new();
        var (source, runner) = MixedConsumerRepository(temp, "2.3", "2.4", installed);
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };
        RecordingReporter reporter = new();

        var result = await new CheckWorkflow(runner, prompts, reporter, source, new FakeSourceVersionResolver(), [Consumer()], new DnaInstaller(runner, string.Empty))
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(reporter.Errors);
        var companion = prompts.RecommendedSkillActions.SingleOrDefault(skill => skill.IsCompanion);
        Assert.Equal(action, companion?.RecommendationAction);
        Assert.Equal(status, companion?.Version);
        Assert.Equal(action, result.Report.Companion?.Action);
        if (action is not null)
        {
            Assert.True(companion!.IsRequiredToolRepair);
            Assert.Equal("2.4", result.Report.Companion!.RequiredMinimum);
            var update = Assert.Single(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "update", ..] && !call.Arguments.Contains("--global"));
            Assert.Contains("2.*", update.Arguments);
        }

        Assert.Equal("2.4.0", CompanionInstaller.InstalledVersion(temp.Path));
    }

    // Content written for different majors cannot share one tool. The run names the files that
    // disagree and what to do about it, and changes nothing.
    [Fact]
    public async Task InstalledContentForDifferentMajorsStopsTheToolRowWithAnActionableMessage()
    {
        using TempDirectory temp = new();
        var (source, runner) = MixedConsumerRepository(temp, "3.0", "2.4", "2.4.0");
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };
        RecordingReporter reporter = new();

        var result = await new CheckWorkflow(runner, prompts, reporter, source, new FakeSourceVersionResolver(), [Consumer()], new DnaInstaller(runner, string.Empty))
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        string message = $"The installed directives and skills ask for different major versions of InnoWvate.Agentic: AGENTS.md (foundation-prompt-log) 3.0; {Path.Combine(".agents", "skills", "fixture-consumer", "SKILL.md")} 2.4. Run `dna check` and apply the pending updates for these items so they ask for the same major.";
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, reporter.Errors);
        Assert.Equal("repair", Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion).RecommendationAction);
        Assert.Equal(message, result.Report.Companion?.Error);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments is ["tool", "install" or "update" or "restore", ..] && !call.Arguments.Contains("--global"));
        Assert.Equal("2.4.0", CompanionInstaller.InstalledVersion(temp.Path));
    }

    // A repository with the Prompt Log directive and one skill installed, each calling the tool with its own -m.
    static (FakeDirectiveSource Source, ToolRunner Runner) MixedConsumerRepository(TempDirectory temp, string directiveMinimum, string skillMinimum, string installed)
    {
        string block = PromptLogBlock($"dotnet agentic prompt-log show -m {directiveMinimum}", "");
        temp.Write("AGENTS.md", block);
        temp.Write(".agents/skills/fixture-consumer/SKILL.md", $"---\nname: fixture-consumer\ndescription: Uses the tool.\n---\ndotnet agentic prompt-log check -m {skillMinimum}\n");
        WriteManifest(temp.Path, installed);
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + block + "~~~\n" });
        return (source, new ToolRunner { Resolved = installed, Updated = "2.4.0" });
    }

    // A repository whose Prompt Log directive is installed and identical to its source, with the companion pinned.
    static (FakeDirectiveSource Source, ToolRunner Runner, FakeVersionSource Versions) CurrentPromptLogRepository(TempDirectory temp, string installed, string published)
    {
        string block = PromptLogBlock("dotnet agentic prompt-log show -m 2.4", "");
        temp.Write("AGENTS.md", block);
        WriteManifest(temp.Path, installed);
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + block + "~~~\n" });
        ToolRunner runner = new() { Resolved = installed, Updated = published };
        FakeVersionSource versions = new(new Dictionary<string, string[]?>(StringComparer.Ordinal)
        {
            // Other majors and prereleases never count for a stable run.
            [CompanionDependency.PackageId] = ["2.3.0", published, "2.5.0-preview.1", "3.0.0"],
            [DnaInstaller.PackageId] = ["1.0.0"]
        });
        return (source, runner, versions);
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
        Assert.DoesNotContain("foundation-prompt-log:start", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
        Assert.NotNull(result.Report.Companion);
        Assert.False(result.Report.Companion.Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingProjectStillFailsForDirectiveThatUsesCompanion(bool dryRun)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        FakeDirectiveSource source = new() { ProjectFailure = new DirectiveException("GitHub returned HTTP 404") };
        ToolRunner runner = new();
        var result = await new CheckWorkflow(runner, new FakePrompts(), new RecordingReporter(), source, new FakeSourceVersionResolver(), [])
            .RunAsync(new(temp.Path, dryRun, true, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(1, source.ProjectFetches);
        Assert.NotNull(result.Report.Companion);
        Assert.False(result.Report.Companion.Success);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "dotnet" && call.Arguments[1] is "install" or "update" or "restore");
        if (File.Exists(result.Report.AgentsFile))
            Assert.DoesNotContain("foundation-prompt-log:start", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
    }

    internal static SkillManifestEntry Consumer()
        => new(CompanionDependency.SourceRepo, "fixture-consumer", "fixture-consumer", TechnologyNames.Dotnet, [], dependencies: [CompanionDependency.Identity]);

    [Theory]
    [InlineData("1.4.0", true, "restore", "", false)]
    [InlineData("3.0.0", false, "update", "", false)]
    [InlineData(null, false, "install", "", false)]
    [InlineData("1.4.0", true, "restore", "dotnet-agentic-engineering:", false)]
    [InlineData("3.0.0", false, "update", "dotnet-agentic-engineering:", false)]
    [InlineData(null, false, "install", "dotnet-agentic-engineering:", false)]
    [InlineData("1.4.0", true, "restore", "", true)]
    [InlineData("3.0.0", false, "update", "", true)]
    [InlineData(null, false, "install", "", true)]
    [InlineData("1.4.0", true, "restore", "dotnet-agentic-engineering:", true)]
    [InlineData("3.0.0", false, "update", "dotnet-agentic-engineering:", true)]
    [InlineData(null, false, "install", "dotnet-agentic-engineering:", true)]
    public async Task RepairUsesInstalledConsumerRequirementWhenDirectiveIsNotSelected(string? installed, bool notRestored, string action, string prefix, bool historicalSource)
    {
        using TempDirectory temp = new();
        string block = $"<!-- {prefix}foundation-prompt-log:start -->\n## Prompt log\ndotnet agentic --minver 1.3 prompt-log show\n<!-- {prefix}foundation-prompt-log:end -->\n";
        temp.Write("AGENTS.md", block);
        string sourceBlock = historicalSource ? PromptLogBlock("git log --grep=\"^prompt-log:\"", prefix) : block;
        FakeDirectiveSource source = new(new Dictionary<string, string> { ["foundation-prompt-log.md"] = "~~~md\n" + sourceBlock + "~~~\n" });
        if (installed is not null)
        {
            WriteManifest(temp.Path, installed);
        }

        ToolRunner runner = new() { Resolved = "1.4.0", NotRestored = notRestored };
        FakePrompts prompts = new() { SelectedDirectiveNames = [] };
        CheckWorkflow workflow = new(runner, prompts, new RecordingReporter(), source, new FakeSourceVersionResolver());
        var result = await workflow.RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        // Legacy markers add an update recommendation whose version is displayed before selection.
        // Declining that update must still repair using the installed 1.3 requirement, not source 2.3.
        bool dependentUpdate = !historicalSource && !string.IsNullOrEmpty(prefix);
        Assert.Equal(dependentUpdate ? 1 : 0, source.ProjectFetches);
        var recommendation = Assert.Single(prompts.RecommendedSkillActions, skill => skill.IsCompanion);
        Assert.Equal(dependentUpdate && installed is not null ? "update" : action, recommendation.RecommendationAction);
        Assert.True(recommendation.IsRequiredToolRepair);
        Assert.NotNull(result.Report.Companion);
        Assert.Equal(action, result.Report.Companion.Action);
        Assert.Equal("1.3", result.Report.Companion.RequiredMinimum);
        Assert.Equal("1.4.0", result.Report.Companion.ResolvedVersion);
        Assert.Equal(block, await ReadIfExistsAsync(result.Report.AgentsFile));
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
        Assert.DoesNotContain("foundation-prompt-log:start", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
        _ = Assert.Throws<FormatException>(() => CompanionDependency.ReadLocalRequirement([
            ("AGENTS.md (foundation-prompt-log)", "dotnet agentic prompt-log show -m 1.3"), (".agents/skills/sample/SKILL.md", "dotnet agentic --minver 2.3 prompt-log check")]));
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
        Assert.DoesNotContain("foundation-prompt-log:start", await ReadIfExistsAsync(result.Report.AgentsFile), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "install", "required 2.3")]
    [InlineData("2.2.0", "update", "currently 2.2.0; required 2.3; refreshes to the latest 2.*")]
    [InlineData("2.3.0", "update", "currently 2.3.0; required 2.3; refreshes to the latest 2.*")]
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

    // Nothing is selected, so an offered action leaves no report; a current tool is reported without being offered.
    [Theory]
    [InlineData("2.3.0", "2.3.0", "1.0.0", false, null, null)]
    [InlineData("2.3.1", "2.3.0", "1.0.0", false, null, null)]
    [InlineData("2.3.0", "2.3.1", "1.0.0", false, "currently 2.3.0; required 2.3; latest 2.3.1", null)]
    [InlineData("2.3.0", "2.3.0", "1.0.0", true, "currently 2.3.0; required 2.3; latest 2.3.0", null)]
    [InlineData("2.3.0", "2.3.0", "0.9.0", false, null, "global launcher; currently 0.9.0")]
    public async Task ToolsThatAreAlreadyTheLatestAreReportedNotOffered(string installed, string published, string dna, bool notRestored, string? companionStatus, string? dnaStatus)
    {
        using TempDirectory temp = new();
        WriteManifest(temp.Path, installed);
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };
        ToolRunner runner = new() { NotRestored = notRestored, Resolved = installed };
        FakeVersionSource versions = new(new Dictionary<string, string[]?>(StringComparer.Ordinal)
        {
            // Prereleases and other majors never count for the stable pattern or the global shorthand.
            [CompanionDependency.PackageId] = ["2.2.0", published, "2.4.0-preview.1", "3.0.0"],
            [DnaInstaller.PackageId] = ["1.0.0", "1.1.0-preview.1"]
        });
        var result = await new CheckWorkflow(runner, prompts, new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver(),
            null, new DnaInstaller(new DnaRunner(runner) { Version = dna }, string.Empty), null, versions)
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var companion = prompts.RecommendedSkillActions.SingleOrDefault(skill => skill.IsCompanion);
        Assert.Equal(companionStatus, companion?.Version);
        Assert.Equal(companionStatus is null ? "current" : null, result.Report.Companion?.Action);
        if (companionStatus is null)
            Assert.Contains(result.Report.Actions, action => action == $"current InnoWvate.Agentic: installed {installed}, required 2.3, pattern 2.*, already the latest");
        var shorthand = prompts.RecommendedSkillActions.SingleOrDefault(skill => skill.IsDna);
        Assert.Equal(dnaStatus, shorthand?.Version);
        Assert.Equal(dnaStatus is null ? "current" : null, result.Report.Dna?.Action);
    }

    [Theory]
    [InlineData(null, "install InnoWvate.Agentic: installed absent, required 2.3, pattern 2.*, resolved 2.3.0")]
    [InlineData("2.2.0", "update InnoWvate.Agentic: installed 2.2.0, required 2.3, pattern 2.*, resolved 2.3.0")]
    [InlineData("2.3.0", "update InnoWvate.Agentic: installed 2.3.0, required 2.3, pattern 2.*, already the latest 2.3.0")]
    public async Task PreparedToolReportsWhetherTheVersionActuallyChanged(string? installed, string expected)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory("target");
        if (installed is not null)
            WriteManifest(temp.Path, installed);
        FakePrompts prompts = new() { SelectedDirectiveNames = ["foundation-prompt-log"], SelectedSkillInstallArgs = [CompanionDependency.PackageId] };
        var result = await new CheckWorkflow(new ToolRunner(), prompts, new RecordingReporter(), new FakeDirectiveSource(), new FakeSourceVersionResolver())
            .RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Report.Companion?.Success, result.Report.Companion?.Error);
        Assert.Contains(expected, result.Report.Actions);
    }

    [Fact]
    public void RepositoryPinIsResolvedFromTheTargetUpToTheGitRoot()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory(".git");
        string backend = temp.CreateDirectory("backend");
        string api = temp.CreateDirectory("backend/api");
        string rootManifest = Path.Combine(temp.Path, ".config", "dotnet-tools.json");

        // Nothing pinned yet: a subfolder check creates the pin at the git root, not in the subfolder.
        Assert.Equal(rootManifest, CompanionInstaller.ManifestPath(backend));
        Assert.Null(CompanionInstaller.InstalledVersion(backend));

        WriteManifest(temp.Path, "2.3.0");
        Assert.Equal(rootManifest, CompanionInstaller.ManifestPath(api));
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(api));

        // A nearer pin wins for its own subtree, as the SDK resolves it.
        WriteManifest(backend, "2.2.0");
        Assert.Equal(Path.Combine(backend, ".config", "dotnet-tools.json"), CompanionInstaller.ManifestPath(api));
        Assert.Equal("2.2.0", CompanionInstaller.InstalledVersion(api));
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(temp.Path));
    }

    [Fact]
    public void WithoutAGitRootOnlyTheTargetCounts()
    {
        using TempDirectory temp = new();
        WriteManifest(temp.Path, "2.3.0");
        string child = temp.CreateDirectory("child");

        Assert.Equal(Path.Combine(child, ".config", "dotnet-tools.json"), CompanionInstaller.ManifestPath(child));
        Assert.Null(CompanionInstaller.InstalledVersion(child));
    }

    [Fact]
    public void PinsBelowTheTargetAreReportedExceptInExcludedFolders()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory(".git");
        WriteManifest(temp.Path, "2.3.0");
        WriteManifest(temp.CreateDirectory("backend"), "2.2.0");
        WriteManifest(temp.CreateDirectory("frontend"), null);
        WriteManifest(temp.CreateDirectory("node_modules/pkg"), "2.1.0");
        temp.Write("tools/.config/dotnet-tools.json", "not json");

        Assert.Equal([Path.Combine(temp.Path, "backend", ".config", "dotnet-tools.json")], CompanionInstaller.PinsBelow(temp.Path));
        Assert.Empty(CompanionInstaller.PinsBelow(Path.Combine(temp.Path, "backend")));
    }

    [Theory]
    [InlineData("2.4", true)]
    [InlineData("3.0", false)]
    public async Task MajorChangesAreOnlyMadeFromTheFolderThatOwnsThePin(string minimum, bool allowed)
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory(".git");
        WriteManifest(temp.Path, "2.3.0");
        string backend = temp.CreateDirectory("backend");
        string rootManifest = Path.Combine(temp.Path, ".config", "dotnet-tools.json");
        ToolRunner runner = new() { Resolved = allowed ? "2.4.1" : "3.0.0" };

        var result = await new CompanionInstaller(runner).EnsureAsync(backend, ToolVersion.ParseMinimum(minimum), false, false, false, CancellationToken.None);

        Assert.Equal(allowed, result.Success);
        Assert.Equal(rootManifest, result.ManifestPath);
        if (allowed)
        {
            Assert.Contains(rootManifest, Assert.Single(runner.Calls).Arguments);
            Assert.Equal("2.4.1", CompanionInstaller.InstalledVersion(backend));
            Assert.False(Directory.Exists(Path.Combine(backend, ".config")));
            return;
        }

        Assert.Empty(runner.Calls);
        Assert.Contains("different major", result.Error, StringComparison.Ordinal);
        Assert.Contains(temp.Path, result.Error, StringComparison.Ordinal);
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(backend));
        // The folder that owns the pin may move the repository to the new major.
        var fromRoot = await new CompanionInstaller(runner).EnsureAsync(temp.Path, ToolVersion.ParseMinimum(minimum), false, false, false, CancellationToken.None);
        Assert.True(fromRoot.Success, fromRoot.Error);
        Assert.Equal("3.0.0", CompanionInstaller.InstalledVersion(backend));
    }

    // Writes the manifest of this exact folder; production code resolves the repository pin instead.
    internal static void WriteManifest(string target, string? version)
    {
        string manifest = Path.Combine(target, ".config", "dotnet-tools.json");
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
    // What an install or update resolves to when that differs from the version that runs before it.
    internal string? Updated { get; init; }
    string? written;
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
                int manifestIndex = arguments.ToList().IndexOf("--tool-manifest");
                string folder = manifestIndex >= 0 ? Path.GetDirectoryName(Path.GetDirectoryName(arguments[manifestIndex + 1]))! : workingDirectory;
                written = Updated ?? Resolved;
                CompanionTests.WriteManifest(folder, written);
            }

            return Task.FromResult(new CommandResult(0, arguments[1] == "run" ? written ?? Resolved : "localized SDK output", ""));
        }

        if (FakeGh.IsInstall(Calls[^1]))
            FakeGh.WriteInstalledSkill(Calls[^1], "# Installed");
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
