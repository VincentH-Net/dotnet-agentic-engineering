using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

// Shared across the serial rows in PackageFixtureTests within one test-host process.
sealed class PackageTestRun
{
    readonly string reports;
    string? installerHash;
    string? root;
    readonly HashSet<bool> channels = [];

    internal PackageTestRun() : this(FixtureFiles.Reports) { }

    internal PackageTestRun(string reports) => this.reports = reports;

    internal void UseCache(FixtureWorkspace workspace, string packageHash, bool preview, Action<string> log)
    {
        FixtureFiles.Require(installerHash is null || installerHash == packageHash, "Cannot share a run cache between different installer packages.");
        installerHash = packageHash;
        root ??= Path.Combine(reports, "runs", Guid.NewGuid().ToString("N"));
        string channel = preview ? "preview" : "stable";
        string directory = Path.Combine(root, channel);
        bool first = channels.Add(preview);
        if (first)
        {
            FixtureFiles.Require(!Directory.Exists(directory), "A run must start with an empty product cache.");
            _ = Directory.CreateDirectory(directory);
            FixtureFiles.WriteJson(Path.Combine(root, channel + ".json"), new { packageHash, channel, startedAtUtc = DateTimeOffset.UtcNow });
        }
        workspace.Environment["AGENTIC_CHECK_CACHE_DIR"] = directory;
        workspace.Environment["AGENTIC_CHECK_CACHE_SECONDS"] = "3600";
        log($"Product HTTP cache ({channel}, {(first ? "cold first use" : "shared reuse")}): {directory}");
    }
}
