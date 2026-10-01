using System.Text.Json;

namespace Agentic.Check;

interface INuGetVersionSource
{
    // Every published version of a package, or null when the source cannot answer.
    Task<IReadOnlyList<ToolVersion>?> VersionsAsync(string packageId, CancellationToken cancellationToken);
}

// nuget.org's flat container lists every version of a package in one small document, which is
// enough to tell whether an installed tool is already the latest. A directory of .nupkg files can
// stand in for it. Any failure answers null: the SDK and the target's NuGet configuration stay
// authoritative for the update itself, so the check only ever removes work.
sealed class NuGetVersionSource(HttpClient client, string? index = null) : INuGetVersionSource
{
    internal const string IndexVariable = "AGENTIC_CHECK_NUGET_INDEX";
    const string DefaultIndex = "https://api.nuget.org/v3-flatcontainer/";

    // CA1308 guards security decisions made on normalized strings; this is a URL segment that the NuGet V3 flat container defines as the lowercase package id.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The NuGet V3 flat container addresses packages by lowercase id.")]
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

            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(source), $"{packageId.ToLowerInvariant()}/index.json"));
            _ = request.Headers.TryAddWithoutValidation("User-Agent", "Agentic.Check");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return [.. document.RootElement.GetProperty("versions").EnumerateArray()
                .Select(version => TryParse(version.GetString() ?? string.Empty))
                .OfType<ToolVersion>()];
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or UriFormatException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

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
