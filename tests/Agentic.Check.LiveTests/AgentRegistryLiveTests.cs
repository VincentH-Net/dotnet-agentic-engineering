using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

// The agent registry mirrors the agents that `gh skill` supports. This maintenance test reads the
// registry source of the latest GitHub CLI release online, so a stale local gh cannot hide drift,
// and reports every id, name and project skills directory that differs.
[Collection(GitHubNetworkScope.Name)]
public sealed class AgentRegistryLiveTests(ITestOutputHelper output)
{
    internal const string SourceRepo = "cli/cli";
    internal const string RegistryPath = "internal/skills/registry/registry.go";

    [SkillMaintenanceFact]
    [Trait("Category", "SkillMaintenance")]
    public async Task AgentRegistryMatchesGhSkill()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(5));
        MaintenanceGh gh = new();
        var release = await gh.ApiAsync($"repos/{SourceRepo}/releases/latest", cancellation.Token).ConfigureAwait(true);
        string tag = release.GetProperty("tag_name").GetString()!;
        var file = await gh.ApiAsync($"repos/{SourceRepo}/contents/{RegistryPath}?ref={tag}", cancellation.Token).ConfigureAwait(true);
        string source = Encoding.UTF8.GetString(Convert.FromBase64String(file.GetProperty("content").GetString()!.Replace("\n", string.Empty, StringComparison.Ordinal)));
        string url = $"https://github.com/{SourceRepo}/blob/{tag}/{RegistryPath}";

        var upstream = GhSkillAgentRegistry.Parse(source);
        var drift = GhSkillAgentRegistry.Compare(upstream, AgentSkillRegistry.Hosts);
        foreach (string line in drift)
        {
            output.WriteLine(line);
        }

        string path = await MaintenanceReport.WriteAsync("agent-registry.md", GhSkillAgentRegistry.Render(tag, url, upstream, drift, DateTimeOffset.Now)).ConfigureAwait(true);
        output.WriteLine($"gh {tag}: {upstream.Count} agents; {drift.Count} difference(s). Report: {path}");
        Assert.True(drift.Count == 0, $"{drift.Count} difference(s) with gh {tag} ({url}):{Environment.NewLine}{string.Join(Environment.NewLine, drift)}{Environment.NewLine}Report: {path}");
    }
}

sealed record GhSkillAgent(string Id, string Name, string ProjectDir);

static partial class GhSkillAgentRegistry
{
    // Entries look like `ID: "cursor",` / `Name: "Cursor",` / `ProjectDir: sharedProjectSkillsDir,` or a quoted path.
    internal static IReadOnlyList<GhSkillAgent> Parse(string source)
    {
        Dictionary<string, string> constants = new(StringComparer.Ordinal);
        foreach (Match constant in ConstantRegex().Matches(source))
        {
            constants[constant.Groups["name"].Value] = constant.Groups["value"].Value;
        }

        List<GhSkillAgent> agents = [];
        string? id = null;
        string? name = null;
        foreach (string line in source.Split('\n'))
        {
            var field = FieldRegex().Match(line);
            if (!field.Success)
            {
                continue;
            }

            string value = field.Groups["quoted"].Success
                ? field.Groups["quoted"].Value
                : constants.GetValueOrDefault(field.Groups["identifier"].Value, field.Groups["identifier"].Value);
            switch (field.Groups["field"].Value)
            {
                case "ID":
                    id = value;
                    name = null;
                    break;
                case "Name":
                    name = value;
                    break;
                case "ProjectDir" when id is not null:
                    agents.Add(new(id, name ?? string.Empty, value));
                    id = null;
                    break;
                default:
                    break;
            }
        }

        if (agents.Count == 0)
        {
            throw new FormatException("No agents were parsed from the gh skill registry source; its layout may have changed.");
        }

        return agents;
    }

    internal static IReadOnlyList<string> Compare(IReadOnlyList<GhSkillAgent> upstream, IReadOnlyList<AgentSkillHost> hosts)
    {
        List<string> drift = [];
        var hostsById = hosts.ToDictionary(host => host.Id, StringComparer.Ordinal);
        var upstreamById = upstream.ToDictionary(agent => agent.Id, StringComparer.Ordinal);
        foreach (var agent in upstream)
        {
            if (!hostsById.TryGetValue(agent.Id, out var host))
            {
                drift.Add($"missing here: {agent.Name} ({agent.Id}) with project directory {agent.ProjectDir}");
                continue;
            }

            if (host.Name != agent.Name)
            {
                drift.Add($"name differs for {agent.Id}: here \"{host.Name}\", gh skill \"{agent.Name}\"");
            }

            if (host.ProjectDirectory != agent.ProjectDir)
            {
                drift.Add($"project directory differs for {agent.Id}: here {host.ProjectDirectory}, gh skill {agent.ProjectDir}");
            }
        }

        foreach (var host in hosts.Where(host => !upstreamById.ContainsKey(host.Id)))
        {
            drift.Add($"no longer in gh skill: {host.Name} ({host.Id})");
        }

        return drift;
    }

    internal static string Render(string tag, string url, IReadOnlyList<GhSkillAgent> upstream, IReadOnlyList<string> drift, DateTimeOffset generatedAt)
    {
        StringBuilder report = new();
        _ = report.AppendLine("# Agent registry versus gh skill")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Generated {generatedAt:yyyy-MM-dd HH:mm zzz} from gh {tag}: {url}")
            .AppendLine()
            .AppendLine(drift.Count == 0 ? "No differences." : $"{drift.Count} difference(s):")
            .AppendLine();
        foreach (string line in drift)
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        _ = report.AppendLine()
            .AppendLine("| id | name | project directory |")
            .AppendLine("|---|---|---|");
        foreach (var agent in upstream)
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"| {agent.Id} | {agent.Name} | {agent.ProjectDir} |");
        }

        return report.ToString();
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ConstantRegex();

    [GeneratedRegex(@"^\s*(?<field>ID|Name|ProjectDir):\s*(?:""(?<quoted>[^""]*)""|(?<identifier>[A-Za-z_][A-Za-z0-9_]*))\s*,?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldRegex();
}

public sealed class AgentRegistryParserTests
{
    [Fact]
    public void ParsesIdsNamesAndProjectDirectoriesIncludingTheSharedConstant()
    {
        const string source = """
            const (
            	sharedProjectSkillsDir = ".agents/skills"
            )

            var hosts = []AgentHost{
            	{
            		ID:         "github-copilot",
            		Name:       "GitHub Copilot",
            		ProjectDir: sharedProjectSkillsDir,
            		UserDir:    ".copilot/skills",
            	},
            	{
            		// https://example.test/docs
            		ID:         "claude-code",
            		Name:       "Claude Code",
            		ProjectDir: ".claude/skills",
            		UserDir:    ".claude/skills",
            	},
            }
            """;

        var agents = GhSkillAgentRegistry.Parse(source);

        Assert.Equal([new("github-copilot", "GitHub Copilot", ".agents/skills"), new GhSkillAgent("claude-code", "Claude Code", ".claude/skills")], agents);
    }

    [Fact]
    public void ReportsMissingStaleRenamedAndMovedAgents()
    {
        GhSkillAgent[] upstream = [new("cursor", "Cursor", ".agents/skills"), new("devin", "Devin", ".devin/skills"), new("bob", "IBM Bob", ".bob/skills")];
        AgentSkillHost[] hosts = [new("cursor", "Cursor IDE", ".cursor/skills"), new("windsurf", "Windsurf", ".windsurf/skills"), new("bob", "IBM Bob", ".bob/skills")];

        var drift = GhSkillAgentRegistry.Compare(upstream, hosts);

        Assert.Equal(
            [
                "name differs for cursor: here \"Cursor IDE\", gh skill \"Cursor\"",
                "project directory differs for cursor: here .cursor/skills, gh skill .agents/skills",
                "missing here: Devin (devin) with project directory .devin/skills",
                "no longer in gh skill: Windsurf (windsurf)"
            ],
            drift);
    }
}
