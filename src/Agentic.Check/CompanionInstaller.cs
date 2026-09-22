using System.Text.Json;

namespace Agentic.Check;

sealed record CompanionReport(string ManifestPath, string? InstalledVersion, string RequiredMinimum, string Pattern, string Action, bool Success, string? ResolvedVersion, bool Changed, string? Error);

sealed class CompanionInstaller(ICommandRunner runner)
{
    // One pin per repository. The nearest manifest on the path from the target up to the git root that
    // pins the companion is the one to read and update; when none does, the pin is created at the git
    // root, or at the target outside a git repository. The SDK resolves local tools upward through
    // manifests, so that single pin serves every folder of the repository.
    internal static string ManifestPath(string target)
    {
        var directories = RepositoryScope.DirectoriesUpToGitRoot(target);
        foreach (string directory in directories)
        {
            string manifest = ManifestIn(directory);
            if (ReadVersion(manifest) is not null)
            {
                return manifest;
            }
        }

        return ManifestIn(directories[^1]);
    }

    internal static string? InstalledVersion(string target)
        => ReadVersion(ManifestPath(target));

    // A pin below the target shadows the repository pin for its own subtree, so the check reports it.
    internal static IReadOnlyList<string> PinsBelow(string target)
    {
        string repositoryPin = ManifestPath(target);
        List<string> pins = [];
        try
        {
            Collect(Path.GetFullPath(target));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Report what could be read; an unreadable folder is not this check's concern.
        }

        return pins;

        void Collect(string directory)
        {
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (StackDetector.ExcludedDirectoryNames.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string manifest = ManifestIn(child);
                if (manifest != repositoryPin && TryReadVersion(manifest) is not null)
                {
                    pins.Add(manifest);
                }

                Collect(child);
            }
        }
    }

    static bool OwnsManifest(string target, string manifest)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            Path.GetDirectoryName(Path.GetDirectoryName(manifest)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static string ManifestIn(string directory)
        => Path.Combine(directory, ".config", "dotnet-tools.json");

    static string? TryReadVersion(string manifest)
    {
        try
        {
            return ReadVersion(manifest);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? ReadVersion(string manifest)
    {
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

            // A major change moves the whole repository, so only the folder that owns the pin may make it.
            if (before is not null && !restoreOnly && !OwnsManifest(target, manifest) && ToolVersion.Parse(before).Major != requirement.Major)
            {
                string owner = Path.GetDirectoryName(Path.GetDirectoryName(manifest))!;
                throw new FormatException($"The repository pins {CompanionDependency.PackageId} {before} in {manifest}, and the selected content needs {requirement.Minimum}, a different major. Run dna check in {owner} first so the repository moves as a whole.");
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
