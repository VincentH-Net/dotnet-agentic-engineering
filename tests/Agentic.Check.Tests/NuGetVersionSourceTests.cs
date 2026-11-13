using System.IO.Compression;
using System.Net;
using System.Text;

namespace Agentic.Check.Tests;

public sealed class NuGetVersionSourceTests
{
    [Fact]
    public void VersionsOrderBySemanticVersioningPrecedence()
    {
        // The ordering example from the Semantic Versioning 2.0.0 specification, plus build metadata.
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1-preview.2", "1.0.1-preview.10", "1.0.1"];
        ToolVersion[] versions = [.. ordered.Select(ToolVersion.Parse)];

        for (int index = 1; index < versions.Length; index++)
        {
            Assert.True(versions[index - 1].CompareTo(versions[index]) < 0, $"{ordered[index - 1]} < {ordered[index]}");
        }

        Assert.Equal(0, ToolVersion.Parse("1.0.0+build.1").CompareTo(ToolVersion.Parse("1.0.0+build.2")));
        Assert.Equal("2.3.1-preview.1", ToolVersion.Parse("2.3.1-preview.1").ToString());
    }

    [Fact]
    public void LatestFollowsTheMajorAndPrereleaseRules()
    {
        string[] published = ["2.3.0", "2.3.1-preview.1", "2.3.1", "2.4.0-rc.1", "3.0.0", "1.9.9"];
        ToolVersion[] versions = [.. published.Select(ToolVersion.Parse)];

        Assert.Equal("2.3.1", NuGetVersionSource.Latest(versions, 2, includePrerelease: false)?.ToString());
        Assert.Equal("2.4.0-rc.1", NuGetVersionSource.Latest(versions, 2, includePrerelease: true)?.ToString());
        Assert.Equal("3.0.0", NuGetVersionSource.Latest(versions, null, includePrerelease: false)?.ToString());
        Assert.Null(NuGetVersionSource.Latest(versions, 4, includePrerelease: true));
        Assert.Null(NuGetVersionSource.Latest(null, 2, includePrerelease: true));
    }

    [Fact]
    public async Task DirectoryIndexListsThePackageVersionsByFileName()
    {
        using TempDirectory temp = new();
        foreach (string package in new[] { "InnoWvate.Agentic.2.3.0.nupkg", "InnoWvate.Agentic.2.3.1-preview.1.nupkg", "InnoWvate.Dna.1.0.0.nupkg", "Other.9.9.9.nupkg" })
            temp.Write(package, string.Empty);
        using HttpClient client = new();
        NuGetVersionSource source = new(client, temp.Path);

        var versions = await source.VersionsAsync("InnoWvate.Agentic", CancellationToken.None);

        Assert.Equal(["2.3.0", "2.3.1-preview.1"], versions!.Select(version => version.ToString()).Order(StringComparer.Ordinal));
        Assert.Empty((await source.VersionsAsync("Missing.Package", CancellationToken.None))!);
    }

    [Fact]
    public async Task FlatContainerIndexIsReadWithTheLowercasePackageId()
    {
        using StubHandler handler = new(request => new(HttpStatusCode.OK) { Content = new StringContent("{\"versions\":[\"2.2.0\",\"2.3.0\",\"not-a-version\"]}") });
        using HttpClient client = new(handler, disposeHandler: false);
        NuGetVersionSource source = new(client);

        var versions = await source.VersionsAsync("InnoWvate.Agentic", CancellationToken.None);

        Assert.Equal(["2.2.0", "2.3.0"], versions!.Select(version => version.ToString()));
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/innowvate.agentic/index.json", handler.Requested?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UnavailableIndexAnswersNull(HttpStatusCode status)
    {
        using StubHandler handler = new(request => new(status));
        using HttpClient client = new(handler, disposeHandler: false);

        Assert.Null(await new NuGetVersionSource(client).VersionsAsync("InnoWvate.Agentic", CancellationToken.None));
    }

    [Fact]
    public async Task UnreachableIndexAnswersNull()
    {
        using StubHandler handler = new(request => throw new HttpRequestException("offline"));
        using HttpClient client = new(handler, disposeHandler: false);

        Assert.Null(await new NuGetVersionSource(client).VersionsAsync("InnoWvate.Agentic", CancellationToken.None));
    }

    // nuget.org keeps an unlisted version downloadable but never selects it for an install or update without
    // a version number. Its registration documents say which versions are listed, and are stored compressed.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListedVersionsLeaveOutTheUnlistedOnes(bool compressed)
    {
        const string registration = """
            {"items":[{"items":[
              {"catalogEntry":{"version":"1.0.0","listed":true}},
              {"catalogEntry":{"version":"1.0.1","listed":false}},
              {"catalogEntry":{"version":"1.0.2+build.7"}},
              {"catalogEntry":{"version":"not-a-version","listed":true}}]}]}
            """;
        using StubHandler handler = new(request => new(HttpStatusCode.OK) { Content = compressed ? Compressed(registration) : new StringContent(registration) });
        using HttpClient client = new(handler, disposeHandler: false);

        var versions = await new NuGetVersionSource(client).ListedVersionsAsync("InnoWvate.Dna", CancellationToken.None);

        Assert.Equal(["1.0.0", "1.0.2+build.7"], versions!.Select(version => version.ToString()));
        Assert.Equal("https://api.nuget.org/v3/registration5-gz-semver2/innowvate.dna/index.json", handler.Requested?.ToString());
    }

    // A package with many versions keeps each page of its registration in a document of its own.
    [Fact]
    public async Task ListedVersionsFollowThePagesOfALongHistory()
    {
        const string page = "https://api.nuget.org/v3/registration5-gz-semver2/innowvate.dna/page/1.0.0/1.0.1.json";
        using StubHandler handler = new(request => new(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.ToString() == page
                ? """{"items":[{"catalogEntry":{"version":"1.0.0","listed":true}},{"catalogEntry":{"version":"1.0.1","listed":true}}]}"""
                : """{"items":[{"@id":"PAGE"},{"items":[{"catalogEntry":{"version":"1.1.0","listed":true}}]}]}""".Replace("PAGE", page, StringComparison.Ordinal))
        });
        using HttpClient client = new(handler, disposeHandler: false);

        var versions = await new NuGetVersionSource(client).ListedVersionsAsync("InnoWvate.Dna", CancellationToken.None);

        Assert.Equal(["1.0.0", "1.0.1", "1.1.0"], versions!.Select(version => version.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    public async Task ListedVersionsAreUnknownWhenTheRegistrationCannotBeRead(HttpStatusCode status, string content)
    {
        using StubHandler handler = new(request => new(status) { Content = new StringContent(content) });
        using HttpClient client = new(handler, disposeHandler: false);

        Assert.Null(await new NuGetVersionSource(client).ListedVersionsAsync("InnoWvate.Dna", CancellationToken.None));
    }

    // A folder of packages that stands in for nuget.org has no unlisted versions.
    [Fact]
    public async Task ListedVersionsOfADirectoryIndexAreItsVersions()
    {
        using TempDirectory temp = new();
        foreach (string package in new[] { "InnoWvate.Dna.1.0.0.nupkg", "InnoWvate.Dna.1.0.1.nupkg" })
            temp.Write(package, string.Empty);
        using HttpClient client = new();

        var versions = await new NuGetVersionSource(client, temp.Path).ListedVersionsAsync("InnoWvate.Dna", CancellationToken.None);

        Assert.Equal(["1.0.0", "1.0.1"], versions!.Select(version => version.ToString()).Order(StringComparer.Ordinal));
    }

    static ByteArrayContent Compressed(string text)
    {
        using MemoryStream buffer = new();
        using (GZipStream gzip = new(buffer, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        ByteArrayContent content = new(buffer.ToArray());
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }
}
