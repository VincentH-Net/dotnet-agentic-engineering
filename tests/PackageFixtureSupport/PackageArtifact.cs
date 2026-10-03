using System.IO.Compression;
using System.Xml.Linq;

namespace Agentic.PackageFixtures;

sealed record PackageArtifact(string Path, string Id, string Version, string Sha256, string? RepositoryCommit, DateTimeOffset? RetrievedAtUtc = null)
{
    internal static PackageArtifact Read(string path, string expectedId, string? expectedVersion = null)
    {
        FixtureFiles.Require(System.IO.Path.IsPathFullyQualified(path) && File.Exists(path), $"Expected absolute package path: {path}");
        using var archive = ZipFile.OpenRead(path);
        using var stream = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        var document = XDocument.Load(stream);
        string id = document.Descendants().Single(element => element.Name.LocalName == "id").Value;
        string version = document.Descendants().Single(element => element.Name.LocalName == "version").Value;
        string? commit = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "repository")?.Attribute("commit")?.Value;
        FixtureFiles.Require(id == expectedId && (expectedVersion is null || version == expectedVersion), $"Wrong package identity: {id} {version}");
        FixtureFiles.Require(archive.Entries.Any(entry => entry.Name == "DotnetToolSettings.xml"), $"Not a tool package: {path}");
        return new(path, id, version, FixtureFiles.Hash(path), commit);
    }

    internal void Verify()
    {
        FixtureFiles.Require(File.Exists(Path), $"Package disappeared: {Path}");
        string actual = FixtureFiles.Hash(Path);
        FixtureFiles.Require(actual == Sha256, $"Package bytes changed: {Path}; expected {Sha256}, actual {actual}");
    }

    // Puts the package in a feed folder. The same bytes may arrive twice, because an unchanged package
    // is its own published package; the same name with other bytes means a version was reused.
    internal void CopyInto(string feed)
    {
        Verify();
        string name = System.IO.Path.GetFileName(Path);
        string destination = System.IO.Path.Combine(feed, name);
        if (File.Exists(destination))
        {
            FixtureFiles.Require(FixtureFiles.Hash(destination) == Sha256,
                $"The feed already holds different bytes for {name}. A changed package needs a new version; an unchanged one is tested as its published package.");
            return;
        }

        File.Copy(Path, destination, false);
    }

    // The package nuget.org serves under this identity, or null when that version is not published.
    // CA1308 guards security decisions made on normalized strings; these are URL segments that the
    // NuGet V3 flat container defines as the lowercase package id and version.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The NuGet V3 flat container addresses packages by lowercase id and version; no security decision depends on the result.")]
    internal static async Task<PackageArtifact?> TryDownloadPublishedAsync(string id, string version)
    {
        string lowerId = id.ToLowerInvariant();
        string lowerVersion = version.ToLowerInvariant();
        try
        {
            return await DownloadAsync(new PublishedToolDefinition(id, version,
                $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg")).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    internal static Task<PackageArtifact> DownloadAsync(BaselineDefinition definition)
        => DownloadAsync(new PublishedToolDefinition(definition.InstallerId, definition.InstallerVersion, definition.PackageUrl));

    internal static async Task<PackageArtifact> DownloadAsync(PublishedToolDefinition definition)
    {
        string folder = System.IO.Path.Combine(FixtureFiles.Cache, definition.Id, definition.Version);
        _ = Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, $"{definition.Id}.{definition.Version}.nupkg");
        string metadata = path + ".json";
        if (File.Exists(metadata))
        {
            var recorded = FixtureFiles.ReadJson<PackageArtifact>(metadata);
            var cached = recorded with { Path = path, RetrievedAtUtc = recorded.RetrievedAtUtc ?? File.GetCreationTimeUtc(path) };
            cached.Verify();
            FixtureFiles.Require(cached.Id == definition.Id && cached.Version == definition.Version, "Cache identity mismatch.");
            return cached;
        }
        FixtureFiles.Require(!File.Exists(path), $"Unverified cache entry: {path}; remove it explicitly before retrying.");
        using HttpClient client = new();
        byte[] bytes = await client.GetByteArrayAsync(new Uri(definition.Url)).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        var package = Read(path, definition.Id, definition.Version) with { RetrievedAtUtc = DateTimeOffset.UtcNow };
        FixtureFiles.WriteJson(metadata, package);
        return package;
    }
}
