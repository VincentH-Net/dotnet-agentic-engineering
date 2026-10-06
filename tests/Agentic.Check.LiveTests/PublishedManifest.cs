using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

sealed record PublishedSkill(string SourceRepo, string LocalFolder, string Set);

// The skill manifest a published Agentic.Check offered, read from its package: the manifest is code, so the
// released assembly is what says which set each entry ended up in.
static class PublishedManifest
{
    const string PackageId = "Agentic.Check";

    // The highest version on nuget.org below this checkout's, and the entries of both its sets.
    internal static async Task<(string Version, IReadOnlyList<PublishedSkill> Skills)> PreviousReleaseAsync()
    {
        var current = ToolVersion.Parse(CandidateInputs.ProjectVersion(Path.Combine(FixtureFiles.Checkout, "src", "Agentic.Check", "Agentic.Check.csproj")));
        using HttpClient client = new();
        using var index = JsonDocument.Parse(await client.GetStringAsync(new Uri("https://api.nuget.org/v3-flatcontainer/agentic.check/index.json")).ConfigureAwait(false));
        var previous = index.RootElement.GetProperty("versions").EnumerateArray().Select(version => ToolVersion.Parse(version.GetString()!))
            .Where(version => version.CompareTo(current) < 0).Max()
            ?? throw new InvalidOperationException($"No published {PackageId} below {current}.");
        string version = previous.Suffix.Length > 0 ? $"{previous.Major}.{previous.Minor}.{previous.Patch}{previous.Suffix}" : $"{previous.Major}.{previous.Minor}.{previous.Patch}";
        var package = await PackageArtifact.TryDownloadPublishedAsync(PackageId, version).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{PackageId} {version} is listed but not downloadable.");
        return (version, Read(package.Path, version));
    }

    internal static IReadOnlyList<PublishedSkill> Read(string packagePath, string label)
    {
        string directory = Path.Combine(Path.GetTempPath(), "agentic-check-manifest-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(packagePath, directory);
        string assemblyPath = Directory.GetFiles(directory, PackageId + ".dll", SearchOption.AllDirectories).Single();
        AssemblyLoadContext context = new(label, isCollectible: true);
        context.Resolving += (loadContext, name) =>
        {
            string candidate = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
            return File.Exists(candidate) ? loadContext.LoadFromAssemblyPath(candidate) : null;
        };
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var manifest = assembly.GetTypes().Single(type => type.Name == nameof(StaticSkillManifest));
        List<PublishedSkill> skills = [];
        foreach (string set in new[] { nameof(StaticSkillManifest.All), nameof(StaticSkillManifest.Preview) })
        {
            // The preview set exists from 2.2.0 on.
            if (manifest.GetProperty(set, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null) is not System.Collections.IEnumerable entries)
                continue;
            foreach (object entry in entries)
            {
                var type = entry.GetType();
                string Read(string property) => type.GetProperty(property)?.GetValue(entry)?.ToString() ?? throw new InvalidOperationException($"{label}: no {property} on {type.Name}.");
                skills.Add(new(Read(nameof(SkillManifestEntry.SourceRepo)), Read(nameof(SkillManifestEntry.LocalFolder)), set));
            }
        }

        return skills;
    }
}
