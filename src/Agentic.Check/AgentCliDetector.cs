namespace Agentic.Check;

sealed record AgentCliProbe(
    string AgentId,
    string FileName,
    IReadOnlyList<string> Arguments,
    string IdentifyingText);

static class AgentCliDetector
{
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    static readonly IReadOnlyList<AgentCliProbe> Probes =
    [
        new("github-copilot", "copilot", ["version"], "GitHub Copilot"),
        new("claude-code", "claude", ["--help"], "Claude Code"),
        new("codex", "codex", ["--version"], "codex"),
        new("gemini-cli", "gemini", ["--help"], "Gemini CLI"),
        new("crush", "crush", ["--help"], "Crush"),
        new("goose", "goose", ["--help"], "Goose"),
        new("opencode", "opencode", ["--version"], "opencode"),
        new("qwen-code", "qwen", ["--help"], "Qwen Code")
    ];

    internal static async Task<string> DetectDefaultAgentsAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var detected = await DetectAsync(commandRunner, workingDirectory, cancellationToken).ConfigureAwait(false);
        return AgentSkillRegistry.FormatDefaultAgentsValue(detected);
    }

    internal static async Task<IReadOnlySet<string>> DetectAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var tasks = Probes.Select(probe => DetectAsync(commandRunner, workingDirectory, probe, cancellationToken));
        string[] detectedIds = [.. (await Task.WhenAll(tasks).ConfigureAwait(false))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)];
        return detectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static async Task<string?> DetectAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        AgentCliProbe probe,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var result = await commandRunner
                .RunAsync(probe.FileName, probe.Arguments, workingDirectory, timeout.Token)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return null;
            }

            string output = result.StandardOutput + Environment.NewLine + result.StandardError;
            return output.Contains(probe.IdentifyingText, StringComparison.OrdinalIgnoreCase)
                ? probe.AgentId
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
