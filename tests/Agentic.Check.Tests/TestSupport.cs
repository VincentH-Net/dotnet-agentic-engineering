namespace Agentic.Check.Tests;

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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommandCall call = new(fileName, [.. arguments], workingDirectory);
        Calls.Add(call);
        OnRun?.Invoke(call);
        return results.Count == 0
            ? throw new InvalidOperationException($"No fake command result queued for {fileName} {string.Join(' ', arguments)}.")
            : Task.FromResult(results.Dequeue());
    }
}

sealed record CommandCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CommandCall(fileName, [.. arguments], workingDirectory));
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
        IReadOnlyList<UnoGateReport> unoGates,
        string targetAgents,
        IReadOnlyList<string> skillsDirectories,
        DirectiveSummary directiveSummary,
        int recommendedCount,
        int missingCount,
        int outdatedCount)
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
    readonly IReadOnlyList<DirectiveSourceFile> files;
    readonly Dictionary<string, string> contents;

    public FakeDirectiveSource(IReadOnlyDictionary<string, string>? directiveContents = null)
    {
        contents = directiveContents is null
            ? new Dictionary<string, string>(DefaultDirectiveContents(), StringComparer.Ordinal)
            : new Dictionary<string, string>(directiveContents, StringComparer.Ordinal);
        files = [.. contents.Keys.Select(name => new DirectiveSourceFile(name, $"https://example.test/{name}"))];
    }

    public Task<IReadOnlyList<DirectiveSourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(files);
    }

    public Task<string> FetchAsync(DirectiveSourceFile sourceFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
