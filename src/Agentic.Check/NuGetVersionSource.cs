using System.IO.Compression;
using System.Text.Json;

namespace Agentic.Check;

interface INuGetVersionSource
{
    // Every published version of a package, or null when the source cannot answer.
    Task<IReadOnlyList<ToolVersion>?> VersionsAsync(string packageId, CancellationToken cancellationToken);

    // The published versions that an install or update without a version number can select. nuget.org
    // keeps an unlisted version downloadable but never selects it, so a run that stops or warns over a
    // newer version must not count one. Null when the source cannot answer.
    Task<IReadOnlyList<ToolVersion>?> ListedVersionsAsync(string packageId, CancellationToken cancellationToken)
        => VersionsAsync(packageId, cancellationToken);
}

// nuget.org's flat container lists every version of a package in one small document, which is
// enough to tell whether an installed tool is already the latest. A directory of .nupkg files can
// stand in for it. Any failure answers null: the SDK and the target's NuGet configuration stay
// authoritative for the update itself, so the check only ever removes work.
sealed class NuGetVersionSource(HttpClient client, string? index = null) : INuGetVersionSource
{
    internal const string IndexVariable = "AGENTIC_CHECK_NUGET_INDEX";
    const string DefaultIndex = "https://api.nuget.org/v3-flatcontainer/";
    // The flat container lists unlisted versions too; the registration documents say which are listed.
    const string DefaultRegistration = "https://api.nuget.org/v3/registration5-gz-semver2/";

    // An index that replaces nuget.org, such as a folder of packages, has no notion of unlisted versions.
    public async Task<IReadOnlyList<ToolVersion>?> ListedVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(index))
        {
            return await VersionsAsync(packageId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var document = await ReadJsonAsync(new Uri(new Uri(DefaultRegistration), $"{Lowercase(packageId)}/index.json"), cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return null;
            }

            List<ToolVersion> listed = [];
            foreach (var page in document.RootElement.GetProperty("items").EnumerateArray())
            {
                // A package with many versions keeps each page in a document of its own.
                if (page.TryGetProperty("items", out var inline))
                {
                    AddListed(inline, listed);
                    continue;
                }

                using var pageDocument = await ReadJsonAsync(new Uri(page.GetProperty("@id").GetString()!), cancellationToken).ConfigureAwait(false);
                if (pageDocument is null)
                {
                    return null;
                }

                AddListed(pageDocument.RootElement.GetProperty("items"), listed);
            }

            return listed;
        }
        catch (Exception exception) when (IsUnanswered(exception, cancellationToken))
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ToolVersion>?> VersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        string source = string.IsNullOrWhiteSpace(index) ? DefaultIndex : index;
        try
        {
            if (Directory.Exists(source))
            {
                string prefix = packageId + ".";
                return [.. Directory.EnumerateFiles(source, "*.nupkg")
                    .Select(file => Path.GetFileNameWithoutExtension(file))
                    .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(name => TryParse(name[prefix.Length..]))
                    .OfType<ToolVersion>()];
            }

            using var document = await ReadJsonAsync(new Uri(new Uri(source), $"{Lowercase(packageId)}/index.json"), cancellationToken).ConfigureAwait(false);
            return document is null
                ? null
                : [.. document.RootElement.GetProperty("versions").EnumerateArray()
                    .Select(version => TryParse(version.GetString() ?? string.Empty))
                    .OfType<ToolVersion>()];
        }
        catch (Exception exception) when (IsUnanswered(exception, cancellationToken))
        {
            return null;
        }
    }

    // Null for a response that is not a success. The registration documents are stored compressed and
    // say so, whether or not the client asked for that.
    async Task<JsonDocument?> ReadJsonAsync(Uri address, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, address);
        _ = request.Headers.TryAddWithoutValidation("User-Agent", "Agentic.Check");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            if (!response.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase))
            {
                return await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            GZipStream decompressed = new(content, CompressionMode.Decompress);
            await using (decompressed.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(decompressed, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    static void AddListed(JsonElement items, List<ToolVersion> listed)
    {
        foreach (var item in items.EnumerateArray())
        {
            var entry = item.GetProperty("catalogEntry");
            bool isListed = !entry.TryGetProperty("listed", out var flag) || flag.ValueKind != JsonValueKind.False;
            if (isListed && TryParse(entry.GetProperty("version").GetString() ?? string.Empty) is { } version)
            {
                listed.Add(version);
            }
        }
    }

    static bool IsUnanswered(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or UriFormatException or InvalidDataException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    // CA1308 guards security decisions made on normalized strings; this is a URL segment that the NuGet V3 resources define as the lowercase package id.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The NuGet V3 resources address packages by lowercase id.")]
    static string Lowercase(string packageId) => packageId.ToLowerInvariant();

    // The newest version dotnet tool would pick: within one major when the install pins one, and
    // prereleases only when they are asked for. Null when nothing qualifies or the source is unknown.
    internal static ToolVersion? Latest(IReadOnlyList<ToolVersion>? versions, int? major, bool includePrerelease)
        => versions?
            .Where(version => (major is null || version.Major == major) && (includePrerelease || !version.IsPrerelease))
            .Max();

    static ToolVersion? TryParse(string value)
    {
        try
        {
            return ToolVersion.Parse(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
