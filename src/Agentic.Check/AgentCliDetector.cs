namespace Agentic.Check;

sealed record AgentCliProbe(
    string FileName,
    IReadOnlyList<string> Arguments,
    string IdentifyingText);

// A folder a harness leaves on this machine. Two roots follow one rule on every OS: a home dotfolder
// under the user profile, and the application-data folder that .NET resolves to Application Support on
// macOS, %APPDATA% on Windows and ~/.config on Linux, which is where desktop apps keep their user data.
sealed record AgentFootprint(string RelativePath, bool InApplicationData = false)
{
    public Environment.SpecialFolder Root
        => InApplicationData ? Environment.SpecialFolder.ApplicationData : Environment.SpecialFolder.UserProfile;
}

sealed record AgentDetection(string AgentId, AgentCliProbe? Cli, IReadOnlyList<AgentFootprint> Footprints);

static class AgentCliDetector
{
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    // An agent counts as present when its CLI answers on PATH or when a documented folder of its CLI or
    // desktop app exists. Only folders that follow one of the two root rules are listed; Goose keeps a
    // different Windows layout (%APPDATA%\Block\goose\config) and stays CLI-only.
    static readonly IReadOnlyList<AgentDetection> Detections =
    [
        new("github-copilot", new("copilot", ["version"], "GitHub Copilot"), [new(".copilot")]),
        new("claude-code", new("claude", ["--help"], "Claude Code"), [new(".claude"), new("Claude", InApplicationData: true)]),
        new("codex", new("codex", ["--version"], "codex"), [new(".codex")]),
        new("gemini-cli", new("gemini", ["--help"], "Gemini CLI"), [new(".gemini")]),
        new("antigravity", null, [new(Path.Combine(".gemini", "antigravity-ide"))]),
        new("antigravity-cli", null, [new(Path.Combine(".gemini", "antigravity-cli"))]),
        new("crush", new("crush", ["--help"], "Crush"), []),
        new("goose", new("goose", ["--help"], "Goose"), []),
        new("opencode", new("opencode", ["--version"], "opencode"), [new(Path.Combine(".config", "opencode"))]),
        new("qwen-code", new("qwen", ["--help"], "Qwen Code"), [new(".qwen")]),
        new("cursor", null, [new(".cursor")]),
        new("devin", null, [new("Devin", InApplicationData: true), new("Windsurf", InApplicationData: true), new(".devin"), new(".windsurf")]),
        new("kiro-cli", null, [new(".kiro")]),
        new("trae", null, [new("Trae", InApplicationData: true)]),
        new("warp", null, [new(".warp")]),
        new("qoder", null, [new(".qoder")]),
        new("bob", null, [new(".bob")]),
        new("mux", null, [new(".xum")]),
        new("openclaw", null, [new(".openclaw")]),
        new("kimi-cli", null, [new(".kimi-code")])
    ];

    internal static async Task<string> DetectDefaultAgentsAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        CancellationToken cancellationToken,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        var detected = await DetectAsync(commandRunner, workingDirectory, cancellationToken, getFolderPath).ConfigureAwait(false);
        return AgentSkillRegistry.FormatDefaultAgentsValue(detected);
    }

    internal static async Task<IReadOnlySet<string>> DetectAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        CancellationToken cancellationToken,
        Func<Environment.SpecialFolder, string>? getFolderPath = null)
    {
        var resolveRoot = getFolderPath ?? Environment.GetFolderPath;
        var tasks = Detections.Select(detection => DetectAsync(commandRunner, workingDirectory, detection, resolveRoot, cancellationToken));
        string[] detectedIds = [.. (await Task.WhenAll(tasks).ConfigureAwait(false))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)];
        return detectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool Exists(AgentFootprint footprint, Func<Environment.SpecialFolder, string> getFolderPath)
    {
        string root = getFolderPath(footprint.Root);
        return !string.IsNullOrWhiteSpace(root) && Directory.Exists(Path.Combine(root, footprint.RelativePath));
    }

    static async Task<string?> DetectAsync(
        ICommandRunner commandRunner,
        string workingDirectory,
        AgentDetection detection,
        Func<Environment.SpecialFolder, string> getFolderPath,
        CancellationToken cancellationToken)
    {
        if (detection.Footprints.Any(footprint => Exists(footprint, getFolderPath)))
        {
            return detection.AgentId;
        }

        return detection.Cli is not null && await ProbeAsync(commandRunner, workingDirectory, detection.Cli, cancellationToken).ConfigureAwait(false)
            ? detection.AgentId
            : null;
    }

    static async Task<bool> ProbeAsync(
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
                return false;
            }

            string output = result.StandardOutput + Environment.NewLine + result.StandardError;
            return output.Contains(probe.IdentifyingText, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
