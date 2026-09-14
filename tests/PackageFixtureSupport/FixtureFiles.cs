using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;

namespace Agentic.PackageFixtures;

static class FixtureFiles
{
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string Checkout
    {
        get
        {
            var directory = new DirectoryInfo(Environment.GetEnvironmentVariable("AGENTIC_E2E_SOURCE_CHECKOUT") ?? AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Set AGENTIC_E2E_SOURCE_CHECKOUT to this repository.");
        }
    }

    internal static string Reports => Environment.GetEnvironmentVariable("AGENTIC_E2E_REPORTS") ?? Path.Combine(Checkout, "tests/Agentic.Check.LiveTests/TestResults/package-fixtures");
    internal static string Cache => Environment.GetEnvironmentVariable("AGENTIC_E2E_CACHE") ?? Path.Combine(Reports, "cache");
    internal static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static SortedDictionary<string, string> Inventory(string directory)
        => new(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path).Split(Path.DirectorySeparatorChar).Contains(".git", StringComparer.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), Hash, StringComparer.Ordinal), StringComparer.Ordinal);

    internal static bool EqualInventory(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    internal static void Copy(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);
        foreach (string relative in Inventory(source).Keys)
        {
            string path = Path.Combine(source, relative);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Refusing snapshot symlink: {relative}");
            string target = Path.Combine(destination, relative);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, File.GetUnixFileMode(path));
        }
    }

    internal static void MaterializeTrigger(string definitionDirectory, string destination)
    {
        var files = ReadJson<Dictionary<string, string>>(Path.Combine(definitionDirectory, "trigger.json"));
        foreach (var (relative, content) in files)
        {
            string target = Path.GetFullPath(Path.Combine(destination, relative));
            Require(target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Trigger path escapes target.");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }
    }

    internal static void CaptureSnapshot(string source, string archivePath)
    {
        using FixtureWorkspace staging = new();
        Copy(source, staging.Target);
        Require(!Inventory(staging.Target).Keys.Any(path => path.Split('/').Any(part => part is "obj" or "bin")), "Build output found in fixture; capture rejected.");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        ZipFile.CreateFromDirectory(staging.Target, archivePath);
    }

    internal static void ExtractSnapshot(string archivePath, string destination)
        => ZipFile.ExtractToDirectory(archivePath, destination);

    internal static void WriteJson<T>(string path, T value)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    internal static T ReadJson<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException($"Empty JSON: {path}");

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}

sealed record FixtureDefinition(string Name, string Agents, bool BaselinePreview, string[] Technologies, Dictionary<string, string[]> Gates);
sealed record BaselineDefinition(string InstallerId, string InstallerVersion, string PackageUrl, bool CompanionExpected, PublishedToolDefinition? Companion = null);
sealed record FixtureCapture(string Name, DateTimeOffset PreparedAtUtc, string Label, FixtureDefinition Definition,
    SortedDictionary<string, string> TriggerHashes, SortedDictionary<string, string> Files, JsonElement Report,
    IReadOnlyList<SkillOrigin> Sources, PackageArtifact? Companion, string SnapshotSha256, SourceIdentity DirectiveSource,
    string[] Invocation, string[] RejectedItems);
sealed record BaselineCollection(string Id, BaselineDefinition Definition, PackageArtifact Installer, DateTimeOffset RetrievedAtUtc,
    string SdkVersion, string GhVersion, string OperatingSystem, string[] Completed, Dictionary<string, string> Failed, string InstallerRuntimeVersion);

sealed record SourceIdentity(string Repository, string Reference, string Commit);

sealed record PublishedToolDefinition(string Id, string Version, string Url);
