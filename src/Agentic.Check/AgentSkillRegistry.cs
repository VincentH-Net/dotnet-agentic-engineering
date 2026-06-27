namespace Agentic.Check;

sealed record AgentSkillHost(string Id, string ProjectDirectory);

static class AgentSkillRegistry
{
    public const string DefaultAgents = "claude-code,codex";
    public const string AgentsProjectDirectory = ".agents/skills";
    public const string ClaudeCodeAgentId = "claude-code";

    static readonly IReadOnlyList<AgentSkillHost> Hosts =
    [
        new("github-copilot", AgentsProjectDirectory),
        new(ClaudeCodeAgentId, ".claude/skills"),
        new("cursor", AgentsProjectDirectory),
        new("codex", AgentsProjectDirectory),
        new("gemini-cli", AgentsProjectDirectory),
        new("antigravity", AgentsProjectDirectory),
        new("adal", ".adal/skills"),
        new("amp", AgentsProjectDirectory),
        new("augment", ".augment/skills"),
        new("bob", ".bob/skills"),
        new("cline", AgentsProjectDirectory),
        new("codebuddy", ".codebuddy/skills"),
        new("command-code", ".commandcode/skills"),
        new("continue", ".continue/skills"),
        new("cortex", ".cortex/skills"),
        new("crush", ".crush/skills"),
        new("deepagents", AgentsProjectDirectory),
        new("droid", ".factory/skills"),
        new("firebender", AgentsProjectDirectory),
        new("goose", ".goose/skills"),
        new("iflow-cli", ".iflow/skills"),
        new("junie", ".junie/skills"),
        new("kilo", ".kilocode/skills"),
        new("kimi-cli", AgentsProjectDirectory),
        new("kiro-cli", ".kiro/skills"),
        new("kode", ".kode/skills"),
        new("mcpjam", ".mcpjam/skills"),
        new("mistral-vibe", ".vibe/skills"),
        new("mux", ".mux/skills"),
        new("neovate", ".neovate/skills"),
        new("openclaw", "skills"),
        new("opencode", AgentsProjectDirectory),
        new("openhands", ".openhands/skills"),
        new("pi", ".pi/skills"),
        new("pochi", ".pochi/skills"),
        new("qoder", ".qoder/skills"),
        new("qwen-code", ".qwen/skills"),
        new("replit", AgentsProjectDirectory),
        new("roo", ".roo/skills"),
        new("trae", ".trae/skills"),
        new("trae-cn", ".trae/skills"),
        new("universal", AgentsProjectDirectory),
        new("warp", AgentsProjectDirectory),
        new("windsurf", ".windsurf/skills"),
        new("zencoder", ".zencoder/skills")
    ];

    static readonly Dictionary<string, AgentSkillHost> HostsById =
        Hosts.ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

    public static string AgentIds => string.Join(", ", Hosts
        .Select(host => host.Id));

    public static AgentDirectoryResolution ResolveProjectDirectories(
        string? agentsValue,
        string repoRoot)
    {
        string value = string.IsNullOrWhiteSpace(agentsValue) ? DefaultAgents : agentsValue;
        string[] agentIds = [.. value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
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
