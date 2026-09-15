using System.Text.Json;

namespace Agentic.PackageFixtures;

// One gateway per test host / preparation operation; no changes to the user's gh configuration.
sealed class GitHubFixtureRun : IAsyncDisposable
{
    internal static GitHubFixtureRun Shared { get; } = new();
    readonly Lazy<Task<GitHubCachingProxy>> proxy = new(StartAsync);
    readonly SemaphoreSlim configurationLock = new(1);
    readonly List<string> contexts = [];
    internal Task<GitHubCachingProxy> ProxyAsync => proxy.Value;
    bool completed;

    static async Task<GitHubCachingProxy> StartAsync()
    {
        string reports = Path.Combine(FixtureFiles.Reports, "github-runs", Guid.NewGuid().ToString("N"));
        GitHubCachingProxy gateway = new(reports);
        try
        {
            await gateway.StartAsync().ConfigureAwait(false);
            return gateway;
        }
        catch
        {
            await gateway.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task ConfigureAsync(Dictionary<string, string> environment)
    {
        var gateway = await proxy.Value.ConfigureAwait(false);
        string directory = environment["GH_CONFIG_DIR"];
        // All callers provide an owned temporary directory, never the ambient user's configuration.
        FixtureFiles.Require(Path.IsPathFullyQualified(directory) && directory != Environment.GetEnvironmentVariable("GH_CONFIG_DIR"), "Expected an isolated gh configuration.");
        _ = Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "config.yml"), "http_unix_socket: " + JsonSerializer.Serialize(gateway.SocketPath) + "\n").ConfigureAwait(false);
        environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        environment["GH_NO_EXTENSION_UPDATE_NOTIFIER"] = "1";
        environment["GH_DEBUG"] = string.Empty;
        await configurationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!contexts.Contains(directory, StringComparer.Ordinal))
                contexts.Add(directory);
            FixtureFiles.WriteJson(Path.Combine(gateway.Reports, "contexts.json"), new { socket = gateway.SocketPath, configurations = contexts });
        }
        finally
        {
            _ = configurationLock.Release();
        }
    }

    internal async Task PrimeAsync(string credential)
    {
        var gateway = await proxy.Value.ConfigureAwait(false);
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = ["token " + credential],
            ["Accept"] = ["application/vnd.github+json"],
            ["User-Agent"] = ["agentic-fixture-source-validation"],
            ["X-GitHub-Api-Version"] = ["2022-11-28"]
        };
        foreach (string repository in new[] { SourceOracle.OwnRepository, "dotnet/skills", "mtmattei/UnoPlatformSkills", "unoplatform/studio" })
        {
            string prefix = "/repos/" + repository;
            var metadata = await GetAsync(prefix).ConfigureAwait(false);
            using var document = JsonDocument.Parse(metadata.JsonBody());
            string branch = document.RootElement.GetProperty("default_branch").GetString()!;
            _ = await GetAsync(prefix + "/commits/" + Uri.EscapeDataString(branch)).ConfigureAwait(false);
            var release = await gateway.SendAsync("GET", prefix + "/releases/latest", headers, [], phase: "start-validation").ConfigureAwait(false);
            FixtureFiles.Require(release.Status is 200 or 404, $"Could not resolve stable source for {repository}: HTTP {release.Status}; rerun required.");
            if (release.Status == 200)
            {
                using var latest = JsonDocument.Parse(release.JsonBody());
                _ = await GetAsync(prefix + "/commits/" + Uri.EscapeDataString(latest.RootElement.GetProperty("tag_name").GetString()!)).ConfigureAwait(false);
            }
        }

        async Task<StoredResponse> GetAsync(string path)
        {
            var response = await gateway.SendAsync("GET", path, headers, [], phase: "start-validation").ConfigureAwait(false);
            FixtureFiles.Require(response.Status == 200, $"Could not establish run source snapshot: {path}, HTTP {response.Status}; rerun required.");
            return response;
        }
    }

    internal static async Task<ProcessResult> RunGhAsync(IReadOnlyList<string> arguments, string? directory = null, CancellationToken cancellationToken = default)
    {
        _ = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(false);
        using FixtureWorkspace workspace = new();
        await FixtureAuthentication.Shared.ApplyAsync(workspace.Environment).ConfigureAwait(false);
        return await workspace.Process.RunAsync("gh", arguments, directory ?? workspace.Root, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal async Task CompleteAsync()
    {
        if (completed || !proxy.IsValueCreated)
            return;
        completed = true;
        var gateway = await proxy.Value.ConfigureAwait(false);
        try
        {
            await gateway.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (proxy.IsValueCreated && proxy.Value.IsCompletedSuccessfully)
            await (await proxy.Value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        configurationLock.Dispose();
    }
}
