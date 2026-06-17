namespace Agentic.Check;

sealed record AgentSkillHost(string Id, string Name, string ProjectDirectory);

static class AgentSkillRegistry
{
    public const string DefaultAgents = "claude-code";
    public const string StandardProjectDirectory = ".agents/skills";

    static readonly IReadOnlyList<AgentSkillHost> Hosts =
    [
        new("github-copilot", "GitHub Copilot", StandardProjectDirectory),
        new("claude-code", "Claude Code", ".claude/skills"),
        new("cursor", "Cursor", StandardProjectDirectory),
        new("codex", "Codex", StandardProjectDirectory),
        new("gemini-cli", "Gemini CLI", StandardProjectDirectory),
        new("antigravity", "Antigravity", StandardProjectDirectory),
        new("adal", "AdaL", ".adal/skills"),
        new("amp", "Amp", StandardProjectDirectory),
        new("augment", "Augment", ".augment/skills"),
        new("bob", "IBM Bob", ".bob/skills"),
        new("cline", "Cline", StandardProjectDirectory),
        new("codebuddy", "CodeBuddy", ".codebuddy/skills"),
        new("command-code", "Command Code", ".commandcode/skills"),
        new("continue", "Continue", ".continue/skills"),
        new("cortex", "Cortex Code", ".cortex/skills"),
        new("crush", "Crush", ".crush/skills"),
        new("deepagents", "Deep Agents", StandardProjectDirectory),
        new("droid", "Droid", ".factory/skills"),
        new("firebender", "Firebender", StandardProjectDirectory),
        new("goose", "Goose", ".goose/skills"),
        new("iflow-cli", "iFlow CLI", ".iflow/skills"),
        new("junie", "Junie", ".junie/skills"),
        new("kilo", "Kilo Code", ".kilocode/skills"),
        new("kimi-cli", "Kimi Code CLI", StandardProjectDirectory),
        new("kiro-cli", "Kiro CLI", ".kiro/skills"),
        new("kode", "Kode", ".kode/skills"),
        new("mcpjam", "MCPJam", ".mcpjam/skills"),
        new("mistral-vibe", "Mistral Vibe", ".vibe/skills"),
        new("mux", "Mux", ".mux/skills"),
        new("neovate", "Neovate", ".neovate/skills"),
        new("openclaw", "OpenClaw", "skills"),
        new("opencode", "OpenCode", StandardProjectDirectory),
        new("openhands", "OpenHands", ".openhands/skills"),
        new("pi", "Pi", ".pi/skills"),
        new("pochi", "Pochi", ".pochi/skills"),
        new("qoder", "Qoder", ".qoder/skills"),
        new("qwen-code", "Qwen Code", ".qwen/skills"),
        new("replit", "Replit", StandardProjectDirectory),
        new("roo", "Roo Code", ".roo/skills"),
        new("trae", "Trae", ".trae/skills"),
        new("trae-cn", "Trae CN", ".trae/skills"),
        new("universal", "Universal", StandardProjectDirectory),
        new("warp", "Warp", StandardProjectDirectory),
        new("windsurf", "Windsurf", ".windsurf/skills"),
        new("zencoder", "Zencoder", ".zencoder/skills")
    ];

    static readonly Dictionary<string, AgentSkillHost> HostsById =
        Hosts.ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

    public static string StandardAgentNames => string.Join(", ", Hosts
        .Where(IsStandardPath)
        .Select(host => host.Name));

    public static string AdditionalAgentIds => string.Join(", ", Hosts
        .Where(IsAdditionalAgentPath)
        .Select(host => host.Id));

    public static AgentDirectoryResolution ResolveProjectDirectories(
        string? agentsValue,
        string repoRoot)
    {
        string value = string.IsNullOrWhiteSpace(agentsValue) ? DefaultAgents : agentsValue;
        string[] agentIds = [.. value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        List<string> unknownAgents = [];
        List<string> standardAgents = [];
        List<string> directories = [];
        HashSet<string> seenDirectories = new(StringComparer.OrdinalIgnoreCase);
        AddDirectory(Path.GetFullPath(Path.Combine(repoRoot, StandardProjectDirectory)));

        foreach (string agentId in agentIds)
        {
            if (!HostsById.TryGetValue(agentId, out var host))
            {
                unknownAgents.Add(agentId);
                continue;
            }

            if (IsStandardPath(host))
            {
                standardAgents.Add(agentId);
                continue;
            }

            AddDirectory(Path.GetFullPath(Path.Combine(repoRoot, host.ProjectDirectory)));
        }

        if (unknownAgents.Count > 0)
        {
            return AgentDirectoryResolution.Invalid(
                $"Unknown additional agent value(s): {string.Join(", ", unknownAgents)}. Valid values: {AdditionalAgentIds}.");
        }

        if (standardAgents.Count > 0)
        {
            return AgentDirectoryResolution.Invalid(
                $"Agent value(s) already use {StandardProjectDirectory} and should not be specified with --agents: {string.Join(", ", standardAgents)}. Use --agents only for additional non-standard agent paths. Valid values: {AdditionalAgentIds}.");
        }

        return AgentDirectoryResolution.Valid(directories);

        void AddDirectory(string directory)
        {
            if (seenDirectories.Add(directory))
            {
                directories.Add(directory);
            }
        }
    }

    static bool IsStandardPath(AgentSkillHost host)
        => host.ProjectDirectory.Equals(StandardProjectDirectory, StringComparison.OrdinalIgnoreCase);

    static bool IsAdditionalAgentPath(AgentSkillHost host)
        => !IsStandardPath(host);
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
