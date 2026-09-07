using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Agentic.Check.LiveTests;

sealed class SkillMaintenanceFactAttribute : FactAttribute
{
    public SkillMaintenanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AGENTIC_CHECK_SKILL_MAINTENANCE") != "1")
        {
            Skip = "Opt in with AGENTIC_CHECK_SKILL_MAINTENANCE=1; requires gh and GitHub authentication.";
        }
    }
}

sealed record SkillTreeFile(string Path, string BlobSha, string Name, string Description = "", string DirectoryTreeSha = "", bool HasFrontmatter = false);

sealed record SkillSourceSnapshot(string Ref, string CommitSha, DateTimeOffset CommittedAt);

sealed class MaintenanceGh(Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>>? runCommand = null)
{
    readonly Dictionary<string, JsonElement> responses = new(StringComparer.Ordinal);
    readonly Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>> runCommand = runCommand ?? RunAsync;

    internal string CacheDuration { get; } = ResolveCacheDuration();

    internal async Task<JsonElement> ApiAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (responses.TryGetValue(endpoint, out var cached))
        {
            return cached;
        }

        var result = await runCommand(["api", "--hostname", "github.com", "--cache", CacheDuration, endpoint], cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new IOException($"gh api {endpoint}: {result.StandardError.Trim()} {result.StandardOutput.Trim()}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var response = document.RootElement.Clone();
        responses.Add(endpoint, response);
        return response;
    }

    internal async Task<SkillSourceSnapshot> DefaultBranchAsync(string repo, CancellationToken cancellationToken)
    {
        var metadata = await ApiAsync($"repos/{repo}", cancellationToken).ConfigureAwait(false);
        return await ResolveAsync(repo, metadata.GetProperty("default_branch").GetString()!, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<SkillSourceSnapshot> StableAsync(string repo, SkillSourceSnapshot defaultBranch, CancellationToken cancellationToken)
    {
        // Listing releases caches a successful empty result for repos without releases.
        for (int page = 1; ; page++)
        {
            var releases = await ApiAsync($"repos/{repo}/releases?per_page=100&page={page}", cancellationToken).ConfigureAwait(false);
            string? tag = FindStableTag(releases);
            if (tag is not null)
            {
                // Use GitHub's latest-release choice rather than assuming tag order.
                var latest = await ApiAsync($"repos/{repo}/releases/latest", cancellationToken).ConfigureAwait(false);
                return await ResolveAsync(repo, latest.GetProperty("tag_name").GetString()!, cancellationToken).ConfigureAwait(false);
            }

            if (releases.GetArrayLength() < 100)
            {
                return defaultBranch;
            }
        }
    }

    internal static string? FindStableTag(JsonElement releases)
        => releases.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean())
            .Select(release => release.GetProperty("tag_name").GetString())
            .FirstOrDefault();

    internal async Task<SkillSourceSnapshot> ResolveAsync(string repo, string reference, CancellationToken cancellationToken)
    {
        var commit = await ApiAsync($"repos/{repo}/commits/{Uri.EscapeDataString(reference)}", cancellationToken).ConfigureAwait(false);
        return new(reference, commit.GetProperty("sha").GetString()!, commit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTimeOffset());
    }

    internal async Task<IReadOnlyList<SkillTreeFile>> InventoryAsync(string repo, string sha, CancellationToken cancellationToken)
    {
        var tree = await ApiAsync($"repos/{repo}/git/trees/{sha}?recursive=1", cancellationToken).ConfigureAwait(false);
        return ReadTree(tree);
    }

    internal static IReadOnlyList<SkillTreeFile> ReadTree(JsonElement tree)
    {
        if (tree.GetProperty("truncated").GetBoolean())
        {
            throw new IOException("GitHub returned a truncated tree; inventory is incomplete. Do not advance the review baseline.");
        }

        var directories = tree.GetProperty("tree").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "tree")
            .ToDictionary(item => item.GetProperty("path").GetString()!, item => item.GetProperty("sha").GetString()!, StringComparer.Ordinal);
        directories[""] = tree.TryGetProperty("sha", out var rootSha) ? rootSha.GetString()! : "";
        return [.. tree.GetProperty("tree").EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "blob")
            .Select(item => (Path: item.GetProperty("path").GetString()!, Sha: item.GetProperty("sha").GetString()!))
            .Where(item => item.Path == "SKILL.md" || item.Path.EndsWith("/SKILL.md", StringComparison.Ordinal))
            .Select(item => new SkillTreeFile(item.Path, item.Sha, item.Path == "SKILL.md" ? "(root)" : item.Path.Split('/')[^2],
                DirectoryTreeSha: directories.GetValueOrDefault(item.Path == "SKILL.md" ? "" : item.Path[..item.Path.LastIndexOf('/')], "")))
            .OrderBy(skill => skill.Path, StringComparer.Ordinal)];
    }

    internal async Task<SkillTreeFile> DescribeAsync(string repo, SkillTreeFile skill, CancellationToken cancellationToken, bool allowInvalidFrontmatter = false)
    {
        var blob = await ApiAsync($"repos/{repo}/git/blobs/{skill.BlobSha}", cancellationToken).ConfigureAwait(false);
        if (blob.GetProperty("encoding").GetString() != "base64")
        {
            throw new IOException($"Unsupported blob encoding for {repo}/{skill.Path}.");
        }

        string content = Encoding.UTF8.GetString(Convert.FromBase64String(blob.GetProperty("content").GetString()!));
        return ReadIdentity(skill, content, allowInvalidFrontmatter);
    }

    internal static SkillTreeFile ReadIdentity(SkillTreeFile skill, string content, bool allowInvalidFrontmatter)
    {
        try
        {
            return ReadFrontmatter(skill, content);
        }
        catch (IOException) when (allowInvalidFrontmatter)
        {
            // Historical fixtures may intentionally contain invalid YAML. Never deduplicate an unverified identity.
            return skill with { Description = "Frontmatter could not be verified; not deduplicated.", HasFrontmatter = false };
        }
    }

    internal static SkillTreeFile ReadFrontmatter(SkillTreeFile skill, string content)
    {
        using StringReader reader = new(content.TrimStart('\uFEFF'));
        if (reader.ReadLine()?.Trim() != "---")
        {
            throw new IOException($"Missing YAML frontmatter: {skill.Path}");
        }

        StringBuilder frontmatter = new();
        while (reader.ReadLine() is { } line)
        {
            if (line.Trim() is "---" or "...")
            {
                try
                {
                    using StringReader yamlReader = new(frontmatter.ToString());
                    YamlStream yaml = [];
                    yaml.Load(yamlReader);
                    return yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping
                        ? throw new IOException($"Expected YAML mapping: {skill.Path}")
                        : (skill with { Name = Scalar(mapping, "name", skill.Path), Description = Scalar(mapping, "description", skill.Path), HasFrontmatter = true });
                }
                catch (YamlException exception)
                {
                    throw new IOException($"Invalid YAML frontmatter: {skill.Path}: {exception.Message}", exception);
                }
            }

            _ = frontmatter.AppendLine(line);
        }

        throw new IOException($"Unterminated YAML frontmatter: {skill.Path}");
    }

    static string Scalar(YamlMappingNode mapping, string key, string path)
        => mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode { Value: { } value } && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new IOException($"Missing scalar {key} in {path}");

    internal static async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ProcessStartInfo startInfo = new("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GH_PAGER"] = "cat";
        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";
        using Process process = new() { StartInfo = startInfo };
        try
        {
            _ = process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new IOException("Could not start gh. Install GitHub CLI before running maintenance tests.", exception);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _ = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Observe both cancelled stream reads after terminating the child process.
            }

            throw new IOException($"gh {string.Join(' ', arguments)} timed out or was cancelled.");
        }
    }

    static string ResolveCacheDuration()
    {
        string? configured = Environment.GetEnvironmentVariable("AGENTIC_CHECK_MAINTENANCE_CACHE_SECONDS");
        return configured is null
            ? "1800s"
            : int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds >= 0
            ? $"{seconds.ToString(CultureInfo.InvariantCulture)}s"
            : throw new IOException("AGENTIC_CHECK_MAINTENANCE_CACHE_SECONDS must be a non-negative integer.");
    }
}
