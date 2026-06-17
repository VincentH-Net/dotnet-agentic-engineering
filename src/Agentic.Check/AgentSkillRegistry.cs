namespace Agentic.Check;

sealed record AgentSkillHost(string Id, string ProjectDirectory);

static class AgentSkillRegistry
{
    public const string DefaultAgents = "claude-code,codex";

    static readonly IReadOnlyList<AgentSkillHost> Hosts =
    [
        new("github-copilot", ".agents/skills"),
        new("claude-code", ".claude/skills"),
        new("cursor", ".agents/skills"),
        new("codex", ".agents/skills"),
        new("gemini-cli", ".agents/skills"),
        new("antigravity", ".agents/skills"),
        new("adal", ".adal/skills"),
        new("amp", ".agents/skills"),
        new("augment", ".augment/skills"),
        new("bob", ".bob/skills"),
        new("cline", ".agents/skills"),
        new("codebuddy", ".codebuddy/skills"),
        new("command-code", ".commandcode/skills"),
        new("continue", ".continue/skills"),
        new("cortex", ".cortex/skills"),
        new("crush", ".crush/skills"),
        new("deepagents", ".agents/skills"),
        new("droid", ".factory/skills"),
        new("firebender", ".agents/skills"),
        new("goose", ".goose/skills"),
        new("iflow-cli", ".iflow/skills"),
        new("junie", ".junie/skills"),
        new("kilo", ".kilocode/skills"),
        new("kimi-cli", ".agents/skills"),
        new("kiro-cli", ".kiro/skills"),
        new("kode", ".kode/skills"),
        new("mcpjam", ".mcpjam/skills"),
        new("mistral-vibe", ".vibe/skills"),
        new("mux", ".mux/skills"),
        new("neovate", ".neovate/skills"),
        new("openclaw", "skills"),
        new("opencode", ".agents/skills"),
        new("openhands", ".openhands/skills"),
        new("pi", ".pi/skills"),
        new("pochi", ".pochi/skills"),
        new("qoder", ".qoder/skills"),
        new("qwen-code", ".qwen/skills"),
        new("replit", ".agents/skills"),
        new("roo", ".roo/skills"),
        new("trae", ".trae/skills"),
        new("trae-cn", ".trae/skills"),
        new("universal", ".agents/skills"),
        new("warp", ".agents/skills"),
        new("windsurf", ".windsurf/skills"),
        new("zencoder", ".zencoder/skills")
    ];

    static readonly Dictionary<string, AgentSkillHost> HostsById =
        Hosts.ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

    public static string ValidAgentIds => string.Join(", ", Hosts.Select(host => host.Id));

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
            return AgentDirectoryResolution.Invalid("At least one agent must be specified.");
        }

        List<string> unknownAgents = [];
        List<string> directories = [];
        HashSet<string> seenDirectories = new(StringComparer.OrdinalIgnoreCase);

        foreach (string agentId in agentIds)
        {
            if (!HostsById.TryGetValue(agentId, out var host))
            {
                unknownAgents.Add(agentId);
                continue;
            }

            string directory = Path.GetFullPath(Path.Combine(repoRoot, host.ProjectDirectory));
            if (seenDirectories.Add(directory))
            {
                directories.Add(directory);
            }
        }

        if (unknownAgents.Count > 0)
        {
            return AgentDirectoryResolution.Invalid(
                $"Unknown agent value(s): {string.Join(", ", unknownAgents)}. Valid values: {ValidAgentIds}.");
        }

        return AgentDirectoryResolution.Valid(directories);
    }
}

sealed record AgentDirectoryResolution(
    bool Success,
    IReadOnlyList<string> Directories,
    string? Error)
{
    public static AgentDirectoryResolution Valid(IReadOnlyList<string> directories)
        => new(true, directories, null);

    public static AgentDirectoryResolution Invalid(string error)
        => new(false, [], error);
}
