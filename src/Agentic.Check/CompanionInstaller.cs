using System.Text.Json;

namespace Agentic.Check;

sealed record CompanionReport(string ManifestPath, string? InstalledVersion, string RequiredMinimum, string Pattern, string Action, bool Success, string? ResolvedVersion, bool Changed, string? Error);

sealed class CompanionInstaller(ICommandRunner runner)
{
    internal static string ManifestPath(string target) => Path.Combine(target, ".config", "dotnet-tools.json");

    internal static string? InstalledVersion(string target)
    {
        string manifest = ManifestPath(target);
        if (!File.Exists(manifest))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var tools = document.RootElement.GetProperty("tools");
        foreach (var tool in tools.EnumerateObject())
        {
            if (tool.Name.Equals(CompanionDependency.PackageId, StringComparison.OrdinalIgnoreCase))
            {
                return tool.Value.GetProperty("version").GetString();
            }
        }

        return null;
    }

    internal async Task<CompanionReport> EnsureAsync(string target, ToolVersion requirement, bool preview, bool restoreOnly, bool dryRun, CancellationToken cancellationToken)
    {
        string manifest = ManifestPath(target);
        string? before = null;
        string? after = null;
        string action = restoreOnly ? "restore" : "install/update";
        try
        {
            before = InstalledVersion(target);
            after = before;
            action = restoreOnly ? "restore" : before is null ? "install" : "update";
            if (restoreOnly && (before is null || !ToolVersion.Parse(before).Satisfies(requirement)))
            {
                throw new FormatException("Refusing to restore an absent or incompatible companion.");
            }

            if (dryRun)
            {
                return new(manifest, before, requirement.Minimum, requirement.Pattern(preview), action, true, null, false, null);
            }

            if (!File.Exists(manifest))
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
                // Preserve parent discovery for unrelated tools. Never rewrite an existing manifest.
                using FileStream stream = new(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(stream, new { version = 1, isRoot = false, tools = new Dictionary<string, string>() }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            string[] arguments = restoreOnly
                ? ["tool", "restore", "--tool-manifest", manifest]
                : ["tool", action, CompanionDependency.PackageId, "--local", "--tool-manifest", manifest, "--version", requirement.Pattern(preview), "--allow-downgrade", .. (action == "install" ? new[] { "--allow-roll-forward" } : Array.Empty<string>())];
            var result = await runner.RunAsync("dotnet", arguments, target, cancellationToken).ConfigureAwait(false);
            after = InstalledVersion(target);
            if (!result.Success)
            {
                throw new IOException($"dotnet tool {action} failed: {result.StandardError} {result.StandardOutput}".TrimEnd());
            }

            var resolved = after is null ? throw new FormatException("SDK succeeded but companion is missing from the target manifest.") : ToolVersion.Parse(after);
            if (!resolved.Satisfies(requirement) || (!restoreOnly && !preview && resolved.IsPrerelease))
            {
                throw new FormatException($"Resolved companion {after} does not satisfy minimum {requirement.Minimum} with the requested {(preview ? "preview" : "stable")} channel.");
            }

            return new(manifest, before, requirement.Minimum, requirement.Pattern(preview), action, true, after, before != after, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return new(manifest, before, requirement.Minimum, requirement.Pattern(preview), action, false, after, before != after, exception.Message);
        }
    }
}
