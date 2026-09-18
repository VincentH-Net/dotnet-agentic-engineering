namespace Agentic.Check.Tests;

static class AuthenticationTestCommands
{
    internal const string Token = "fixture-authentication-secret";
    internal const string RepositoryResponse = "HTTP/2.0 200 OK\r\nX-RateLimit-Limit: 5000\r\nX-RateLimit-Remaining: 4999\r\n\r\n{}";

    internal static CommandResult? Response(string executable, IReadOnlyList<string> arguments)
        => executable != "gh" ? null
            : arguments is ["auth", "token", "--hostname", "github.com"] ? new(0, Token, string.Empty)
            : arguments is ["api", "--hostname", "github.com", "--include", "--method", "GET", GitHubAuthentication.ProbePath]
                ? new(0, RepositoryResponse, string.Empty) : null;
}

sealed class TempDirectory : IDisposable
{
    public TempDirectory()
        => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentic-check-{Guid.NewGuid():N}");

    public string Path { get; }

    public string CreateDirectory(string relativePath)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        _ = Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Write(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        string? directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

sealed class FakeCommandRunner : ICommandRunner
{
    readonly Queue<CommandResult> results = [];

    public List<CommandCall> Calls { get; } = [];

    public Action<CommandCall>? OnRun { get; init; }

    public void Enqueue(CommandResult result)
        => results.Enqueue(result);

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommandCall call = new(fileName, [.. arguments], workingDirectory) { Environment = environment };
        Calls.Add(call);
        OnRun?.Invoke(call);
        if (AuthenticationTestCommands.Response(fileName, arguments) is { } authentication)
            return Task.FromResult(authentication);
        if (fileName == "dotnet" && arguments[0] == "tool")
        {
            return Task.FromResult(CompanionTestCommands.Succeed(arguments, workingDirectory));
        }

        return results.Count == 0
            ? throw new InvalidOperationException($"No fake command result queued for {fileName} {string.Join(' ', arguments)}.")
            : Task.FromResult(results.Dequeue());
    }
}

sealed record CommandCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    internal IReadOnlyDictionary<string, string?>? Environment { get; init; }
}

sealed class MappedCommandRunner : ICommandRunner
{
    readonly Dictionary<string, CommandResult> results = new(StringComparer.Ordinal);

    public List<CommandCall> Calls { get; } = [];

    public void Set(string fileName, IReadOnlyList<string> arguments, CommandResult result)
        => results[CreateKey(fileName, arguments)] = result;

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CommandCall(fileName, [.. arguments], workingDirectory) { Environment = environment });
        if (!results.ContainsKey(CreateKey(fileName, arguments)) && AuthenticationTestCommands.Response(fileName, arguments) is { } authentication)
            return Task.FromResult(authentication);
        if (fileName == "dotnet" && arguments[0] == "tool" && !results.ContainsKey(CreateKey(fileName, arguments)))
        {
            return Task.FromResult(CompanionTestCommands.Succeed(arguments, workingDirectory));
        }

        return Task.FromResult(results.GetValueOrDefault(
            CreateKey(fileName, arguments),
            new CommandResult(127, string.Empty, "command not found")));
    }

    static string CreateKey(string fileName, IReadOnlyList<string> arguments)
        => $"{fileName}\n{string.Join('\n', arguments)}";
}

sealed class FakePrompts : IUserPrompts
{
    public bool ConfirmResult { get; init; } = true;

    public List<string> ConfirmPrompts { get; } = [];

    public IReadOnlyList<string>? SelectedDirectiveNames { get; init; }

    public IReadOnlyList<string>? SelectedSkillInstallArgs { get; init; }

    public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfirmPrompts.Add(prompt);
        return Task.FromResult(ConfirmResult);
    }

    public Task<RecommendationSelectionResult> SelectRecommendationsAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        string targetDirectory,
        IReadOnlyList<string> skillsDirectories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedDirectives = SelectedDirectiveNames is null
            ? recommendedDirectives
            : [.. recommendedDirectives.Where(directive => SelectedDirectiveNames.Contains(directive.Name, StringComparer.Ordinal))];
        var selectedSkills = SelectedSkillInstallArgs is null
            ? missingSkills
            : [.. missingSkills.Where(skill => SelectedSkillInstallArgs.Contains(skill.InstallArg, StringComparer.Ordinal))];
        return Task.FromResult(new RecommendationSelectionResult(selectedDirectives, selectedSkills));
    }

    public Task WaitForHelpKeyAsync(string url, string purpose, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

sealed class RecordingReporter : IReporter
{
    public List<string> Infos { get; } = [];

    public List<string> PlainMessages { get; } = [];

    public List<string> InfoMessages { get; } = [];

    public List<string> BoldMessages { get; } = [];

    public List<(string Message, string Color)> ColoredBoldMessages { get; } = [];

    public List<string> Successes { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> ProgressDescriptions { get; } = [];

    public Dictionary<string, int> ProgressTicksByDescription { get; } = new(StringComparer.Ordinal);

    public int ProgressTicks { get; private set; }

    public string? TargetAgents { get; private set; }

    public int? OutdatedSkillCount { get; private set; }

    public void Plain(string message)
    {
        PlainMessages.Add(message);
        Infos.Add(message);
    }

    public void Bold(string message)
    {
        BoldMessages.Add(message);
        Infos.Add(message);
    }

    public void Bold(string message, string color)
    {
        BoldMessages.Add(message);
        ColoredBoldMessages.Add((message, color));
        Infos.Add(message);
    }

    public void Info(string message)
    {
        InfoMessages.Add(message);
        Infos.Add(message);
    }

    public void Success(string message)
        => Successes.Add(message);

    public void Warning(string message)
        => Warnings.Add(message);

    public void Error(string message)
        => Errors.Add(message);

    public void Summary(
        string repoRoot,
        IReadOnlySet<string> technologies,
        IReadOnlyList<InstallGateReport> installGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount,
        SourceVersionMode sourceMode = SourceVersionMode.Stable)
    {
        TargetAgents = targetAgents;
        OutdatedSkillCount = outdatedCount;
    }

    public async Task RunProgressAsync(
        string description,
        int total,
        Func<Action, Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProgressDescriptions.Add(description);
        ProgressTicksByDescription[description] = 0;
        await action(() =>
        {
            ProgressTicks++;
            ProgressTicksByDescription[description]++;
        }).ConfigureAwait(false);
    }
}

sealed class FakeDirectiveSource : IDirectiveSource
{
    public int ProjectFetches { get; private set; }

    public string ProjectContent { get; init; } = "<Project><Version>2.3.0</Version></Project>";

    readonly IReadOnlyList<DirectiveSourceFile> files;
    readonly Dictionary<string, string> contents;

    public FakeDirectiveSource(IReadOnlyDictionary<string, string>? directiveContents = null)
    {
        contents = directiveContents is null
            ? new Dictionary<string, string>(DefaultDirectiveContents(), StringComparer.Ordinal)
            : new Dictionary<string, string>(directiveContents, StringComparer.Ordinal);
        files = [.. contents.Keys.Select(name => new DirectiveSourceFile(name, $"https://example.test/{name}", SourceRef: "fixture-ref"))];
    }

    public Task<IReadOnlyList<DirectiveSourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(files);
    }

    public Task<string> FetchAsync(DirectiveSourceFile sourceFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceFile.FileName == CompanionSourceVersionReader.ProjectPath)
        {
            ProjectFetches++;
            return Task.FromResult(ProjectContent);
        }

        return contents.TryGetValue(sourceFile.FileName, out string? content)
            ? Task.FromResult(content)
            : throw new InvalidOperationException($"Missing fake directive content for {sourceFile.FileName}.");
    }

    public static Dictionary<string, string> DefaultDirectiveContents()
        => new(StringComparer.Ordinal)
        {
            ["foundation-prompt-log.md"] = DirectiveFile("foundation-prompt-log"),
            ["dotnet-cli-run.md"] = DirectiveFile("dotnet-cli-run"),
            ["uno-build-and-run.md"] = DirectiveFile("uno-build-and-run")
        };

    public static string DirectiveFile(string directiveName)
        => $"""
            # {directiveName}

            ~~~md
            <!-- dotnet-agentic-engineering:{directiveName}:start -->
            ## {directiveName}
            Body for {directiveName}.
            <!-- dotnet-agentic-engineering:{directiveName}:end -->
            ~~~
            """;
}

sealed class FakeSourceVersionResolver : ISourceVersionResolver
{
    public string StableRef { get; init; } = "v1.2.3";

    public bool StableUsesDefaultBranch { get; init; }

    public List<string> RequestedSourceRepos { get; } = [];

    public Task<IReadOnlyDictionary<string, SourceVersionInfo>> ResolveVersionsAsync(
        IEnumerable<string> sourceRepos,
        SourceVersionMode sourceVersionMode,
        DirectiveCacheSettings cacheSettings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, SourceVersionInfo> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string sourceRepo in sourceRepos.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RequestedSourceRepos.Add(sourceRepo);
            result[sourceRepo] = sourceVersionMode == SourceVersionMode.Preview
                ? new SourceVersionInfo(
                    sourceRepo,
                    "main",
                    new DateTimeOffset(2026, 6, 30, 9, 12, 0, TimeSpan.Zero))
                : new SourceVersionInfo(
                    sourceRepo,
                    StableRef,
                    new DateTimeOffset(2026, 6, 29, 8, 11, 0, TimeSpan.Zero))
                {
                    IsDefaultBranch = StableUsesDefaultBranch
                };
        }

        return Task.FromResult((IReadOnlyDictionary<string, SourceVersionInfo>)result);
    }
}

static class CompanionTestCommands
{
    internal static CommandResult Succeed(IReadOnlyList<string> arguments, string target)
    {
        if (arguments.Contains("--global"))
            return new(0, arguments[1] == "list" ? "{\"version\":1,\"data\":[{\"packageId\":\"InnoWvate.Dna\",\"version\":\"1.0.0\",\"commands\":[\"dna\"]}]}" : string.Empty, string.Empty);
        if (arguments[1] is "install" or "update")
        {
            string manifest = CompanionInstaller.ManifestPath(target);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
            json["tools"]!["innowvate.agentic"] = new System.Text.Json.Nodes.JsonObject
            {
                ["version"] = "2.3.0",
                ["commands"] = new System.Text.Json.Nodes.JsonArray("agentic")
            };
            File.WriteAllText(manifest, json.ToJsonString());
        }

        return new(0, arguments[1] == "run" ? "2.3.0" : string.Empty, string.Empty);
    }
}
