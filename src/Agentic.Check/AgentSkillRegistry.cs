namespace Agentic.Check;

sealed record AgentSkillHost(string Id, string Name, string ProjectDirectory);

static class AgentSkillRegistry
{
    public const string DefaultAgents = "claude-code,codex";
    public const string AgentsProjectDirectory = ".agents/skills";
    public const string ClaudeCodeAgentId = "claude-code";

    static readonly IReadOnlyList<AgentSkillHost> Hosts =
    [
        new("github-copilot", "GitHub Copilot", AgentsProjectDirectory),
        new(ClaudeCodeAgentId, "Claude Code", ".claude/skills"),
        new("cursor", "Cursor", AgentsProjectDirectory),
        new("codex", "Codex", AgentsProjectDirectory),
        new("gemini-cli", "Gemini CLI", AgentsProjectDirectory),
        new("antigravity", "Antigravity", AgentsProjectDirectory),
        new("adal", "AdaL", ".adal/skills"),
        new("amp", "Amp", AgentsProjectDirectory),
        new("augment", "Augment", ".augment/skills"),
        new("bob", "IBM Bob", ".bob/skills"),
        new("cline", "Cline", AgentsProjectDirectory),
        new("codebuddy", "CodeBuddy", ".codebuddy/skills"),
        new("command-code", "Command Code", ".commandcode/skills"),
        new("continue", "Continue", ".continue/skills"),
        new("cortex", "Cortex Code", ".cortex/skills"),
        new("crush", "Crush", ".crush/skills"),
        new("deepagents", "Deep Agents", AgentsProjectDirectory),
        new("droid", "Droid", ".factory/skills"),
        new("firebender", "Firebender", AgentsProjectDirectory),
        new("goose", "Goose", ".goose/skills"),
        new("iflow-cli", "iFlow CLI", ".iflow/skills"),
        new("junie", "Junie", ".junie/skills"),
        new("kilo", "Kilo Code", ".kilocode/skills"),
        new("kimi-cli", "Kimi Code CLI", AgentsProjectDirectory),
        new("kiro-cli", "Kiro CLI", ".kiro/skills"),
        new("kode", "Kode", ".kode/skills"),
        new("mcpjam", "MCPJam", ".mcpjam/skills"),
        new("mistral-vibe", "Mistral Vibe", ".vibe/skills"),
        new("mux", "Mux", ".mux/skills"),
        new("neovate", "Neovate", ".neovate/skills"),
        new("openclaw", "OpenClaw", "skills"),
        new("opencode", "OpenCode", AgentsProjectDirectory),
        new("openhands", "OpenHands", ".openhands/skills"),
        new("pi", "Pi", ".pi/skills"),
        new("pochi", "Pochi", ".pochi/skills"),
        new("qoder", "Qoder", ".qoder/skills"),
        new("qwen-code", "Qwen Code", ".qwen/skills"),
        new("replit", "Replit", AgentsProjectDirectory),
        new("roo", "Roo Code", ".roo/skills"),
        new("trae", "Trae", ".trae/skills"),
        new("trae-cn", "Trae CN", ".trae/skills"),
        new("universal", "Universal", AgentsProjectDirectory),
        new("warp", "Warp", AgentsProjectDirectory),
        new("windsurf", "Windsurf", ".windsurf/skills"),
        new("zencoder", "Zencoder", ".zencoder/skills")
    ];

    static readonly Dictionary<string, AgentSkillHost> HostsById =
        Hosts.ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

    public static string AgentIds => string.Join(", ", Hosts
        .Select(host => host.Id));

    public static string AgentHelpLines => string.Join(Environment.NewLine, Hosts
        .Select(host => $"  - {host.Name} ({host.Id})"));

    public static AgentValueValidation ValidateAgentsValue(string? agentsValue)
    {
        if (string.IsNullOrWhiteSpace(agentsValue))
        {
            return AgentValueValidation.Valid();
        }

        string[] agentIds = ParseAgentIds(agentsValue);
        if (agentIds.Length == 0)
        {
            return AgentValueValidation.Invalid(
                $"No agent values were specified. Valid values: {AgentIds}.");
        }

        string[] unknownAgents = [.. agentIds
            .Where(agentId => !HostsById.ContainsKey(agentId))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        return unknownAgents.Length > 0
            ? AgentValueValidation.Invalid(
                $"Unknown agent value(s): {string.Join(", ", unknownAgents)}. Valid values: {AgentIds}.")
            : AgentValueValidation.Valid();
    }

    public static AgentDirectoryResolution ResolveProjectDirectories(
        string? agentsValue,
        string repoRoot)
    {
        string value = string.IsNullOrWhiteSpace(agentsValue) ? DefaultAgents : agentsValue;
        string[] agentIds = ParseAgentIds(value);
        if (agentIds.Length == 0)
        {
            return AgentDirectoryResolution.Invalid(
                $"No agent values were specified. Valid values: {AgentIds}.");
        }

        List<string> unknownAgents = [];
        List<string> directories = [];
        HashSet<string> seenDirectories = new(StringComparer.OrdinalIgnoreCase);
        bool manageClaude = false;

        foreach (string agentId in agentIds)
        {
            if (!HostsById.TryGetValue(agentId, out var host))
            {
                unknownAgents.Add(agentId);
                continue;
            }

            AddDirectory(Path.GetFullPath(Path.Combine(repoRoot, host.ProjectDirectory)));
            manageClaude |= host.Id.Equals(ClaudeCodeAgentId, StringComparison.OrdinalIgnoreCase);
        }

        return unknownAgents.Count > 0
            ? AgentDirectoryResolution.Invalid(
                $"Unknown agent value(s): {string.Join(", ", unknownAgents)}. Valid values: {AgentIds}.")
            : AgentDirectoryResolution.Valid(directories, manageClaude);

        void AddDirectory(string directory)
        {
            if (seenDirectories.Add(directory))
            {
                directories.Add(directory);
            }
        }
    }

    static string[] ParseAgentIds(string value)
        => [.. value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

sealed record AgentValueValidation(bool Success, string? Error)
{
    public static AgentValueValidation Valid()
        => new(true, null);

    public static AgentValueValidation Invalid(string error)
        => new(false, error);
}

sealed record AgentDirectoryResolution(
    bool Success,
    IReadOnlyList<string> Directories,
    bool ManageClaude,
    string? Error)
{
    public static AgentDirectoryResolution Valid(IReadOnlyList<string> directories, bool manageClaude)
        => new(true, directories, manageClaude, null);

    public static AgentDirectoryResolution Invalid(string error)
        => new(false, [], false, error);
}
