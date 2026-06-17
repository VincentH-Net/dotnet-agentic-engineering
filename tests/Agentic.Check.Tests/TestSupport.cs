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

sealed class FakePrompts : IUserPrompts
{
    public bool ConfirmResult { get; init; } = true;

    public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConfirmResult);
    }

    public Task<IReadOnlyList<SkillManifestEntry>> SelectSkillsAsync(
        IReadOnlyList<SkillManifestEntry> missingSkills,
        SkillSelectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(missingSkills);
    }
}
