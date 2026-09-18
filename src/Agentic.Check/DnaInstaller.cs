using System.Text.Json;

namespace Agentic.Check;

sealed record DnaInstallation(string? Version, string? Error = null)
{
    internal string Action => Version is null ? "install" : "update";
}

sealed record DnaReport(string Action, string? InstalledVersion, string? ResolvedVersion, bool Success, bool Skipped,
    IReadOnlyList<string> Conflicts, string? Error);

sealed class DnaInstaller(ICommandRunner runner, string? searchPath = null, string? globalDirectory = null)
{
    internal const string PackageId = "InnoWvate.Dna";
    internal static SkillDependency Identity { get; } = new(string.Empty, PackageId);

    internal static SkillManifestEntry Action(DnaInstallation installation)
        => new(string.Empty, PackageId, "`dna` shorthand for `dotnet agentic`", string.Empty, [],
            dependencies: [CompanionDependency.Identity], recommendationAction: installation.Action,
            version: installation.Error ?? (installation.Version is null ? "global launcher" : $"global launcher; currently {installation.Version}"));

    internal string CommandPath => Path.Combine(globalDirectory ?? Path.Combine(
        Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dotnet", "tools"), OperatingSystem.IsWindows() ? "dna.exe" : "dna");

    internal async Task<DnaInstallation> InspectAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync("dotnet", ["tool", "list", "--global", "--format", "json"], directory, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return new(null, $"Cannot inspect global tools: {result.StandardError} {result.StandardOutput}".TrimEnd());
            using var document = JsonDocument.Parse(result.StandardOutput);
            foreach (var tool in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (!string.Equals(tool.GetProperty("packageId").GetString(), PackageId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!tool.GetProperty("commands").EnumerateArray().Any(command => command.GetString() == "dna"))
                    return new(null, $"The installed {PackageId} package does not expose the dna command.");
                string version = tool.GetProperty("version").GetString()!;
                _ = ToolVersion.Parse(version);
                return new(version);
            }
            return new(null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new(null, $"Cannot identify the installed {PackageId}: {exception.Message}");
        }
    }

    internal IReadOnlyList<string> Conflicts(DnaInstallation installation)
        => [.. FindPathCommands(searchPath ?? Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATHEXT"), OperatingSystem.IsWindows(), Environment.CurrentDirectory)
            .Where(path => installation.Version is null || !SamePath(path, CommandPath))];

    internal async Task<DnaReport> EnsureAsync(DnaInstallation installation, string directory, bool dryRun, bool unattended,
        IUserPrompts prompts, CancellationToken cancellationToken)
    {
        if (installation.Error is not null)
            return new(installation.Action, installation.Version, null, false, false, [], installation.Error);
        IReadOnlyList<string> conflicts;
        try
        {
            conflicts = Conflicts(installation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(installation.Action, installation.Version, null, false, false, [], $"Cannot check PATH for dna: {exception.Message}");
        }
        if (dryRun)
            return new(installation.Action, installation.Version, null, true, false, conflicts, null);
        if (conflicts.Count > 0)
        {
            string message = $"Another dna command exists at {Spectre.Console.Markup.Escape(string.Join(", ", conflicts))} and may hide the shorthand. Install anyway?";
            if (unattended || !prompts.IsInteractive || !await prompts.ConfirmAsync(message, false, cancellationToken).ConfigureAwait(false))
                return new(installation.Action, installation.Version, null, true, true, conflicts, "Shorthand action deselected because of a dna command conflict.");
        }

        var result = await runner.RunAsync("dotnet", ["tool", installation.Action, "--global", PackageId], directory, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new(installation.Action, installation.Version, null, false, false, conflicts,
                $"dotnet tool {installation.Action} --global {PackageId} failed: {result.StandardError} {result.StandardOutput}".TrimEnd());
        }

        var installed = await InspectAsync(directory, cancellationToken).ConfigureAwait(false);
        bool success = installed.Error is null && installed.Version is not null;
        return new(installation.Action, installation.Version, installed.Version, success, false, conflicts,
            success ? null : installed.Error ?? $"SDK succeeded but {PackageId} is absent from the global tool list.");
    }

    // Enumerate files in PATH order without starting any candidate, shell, or command-discovery utility.
    internal static IReadOnlyList<string> FindPathCommands(string? path, string? pathExt, bool windows, string directory)
    {
        if (path is null)
            return [];
        string[] suffixes = windows ? (pathExt ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries) : [string.Empty];
        List<string> found = [];
        foreach (string entry in path.Split(windows ? ';' : ':'))
        {
            string folder = entry.Trim('"');
            folder = Path.GetFullPath(folder.Length == 0 ? "." : folder, directory);
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(folder, "dna" + suffix);
                if (!File.Exists(candidate))
                    continue;
                if (!OperatingSystem.IsWindows() && !windows
                    && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                {
                    continue;
                }

                if (!found.Contains(candidate, windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                    found.Add(candidate);
            }
        }
        return found;
    }

    static bool SamePath(string left, string right)
        => string.Equals(Canonical(left), Canonical(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static string Canonical(string path)
        => (File.Exists(path) ? File.ResolveLinkTarget(path, true)?.FullName : null) ?? Path.GetFullPath(path);
}
