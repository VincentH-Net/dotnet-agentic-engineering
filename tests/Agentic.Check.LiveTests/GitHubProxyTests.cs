using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class GitHubProxyTests
{
    [Fact]
    public async Task RealSocketCoalescesRequestsPreservesResponsesAndSeparatesCredentials()
    {
        using FixtureWorkspace workspace = new();
        int requests = 0;
        using Handler handler = new(request =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            int count = Interlocked.Increment(ref requests);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("payload-" + count) };
            response.Headers.Add("X-RateLimit-Limit", "5000");
            response.Headers.Add("X-RateLimit-Remaining", (5000 - count).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        });
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        await proxy.StartAsync().ConfigureAwait(true);
        using SocketsHttpHandler transport = new();
        ConfigureSocket(transport, proxy.SocketPath);
        using HttpClient client = new(transport, disposeHandler: false);
        client.DefaultRequestHeaders.Authorization = new("token", "first-secret");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.GetAsync(new Uri("http://api.github.com/test/blob")))).ConfigureAwait(true);
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal("payload-1", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
                Assert.Equal("4999", response.Headers.GetValues("X-RateLimit-Remaining").Single());
            }
        }
        Assert.Equal(1, requests);
        Assert.Equal(5, proxy.CacheHits);
        client.DefaultRequestHeaders.Authorization = new("token", "second-secret");
        Assert.Equal("payload-2", await client.GetStringAsync(new Uri("http://api.github.com/test/blob")).ConfigureAwait(true));
        client.DefaultRequestHeaders.Add(GitHubCachingProxy.BypassHeader, "bypass");
        Assert.Equal("payload-3", await client.GetStringAsync(new Uri("http://api.github.com/test/blob")).ConfigureAwait(true));
        using var forbidden = await client.GetAsync(new Uri("http://other.invalid/test/blob")).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(3, requests);
        await proxy.CompleteAsync().ConfigureAwait(true);
        string trace = await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "requests.jsonl")).ConfigureAwait(true);
        Assert.DoesNotContain("first-secret", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("second-secret", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-", trace, StringComparison.Ordinal);
        Assert.Equal(8, trace.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData(401, 2)]
    [InlineData(403, 2)]
    [InlineData(429, 2)]
    [InlineData(503, 2)]
    [InlineData(404, 1)]
    public async Task ExpectedMissingRefsAreCachedButAuthQuotaAndTransientFailuresAreNot(int status, int expectedRequests)
    {
        using FixtureWorkspace workspace = new();
        int requests = 0;
        using Handler handler = new(_ =>
        {
            requests++;
            return new((HttpStatusCode)status) { Content = new StringContent("{}") };
        });
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        for (int index = 0; index < 2; index++)
        {
            var response = await proxy.SendAsync("GET", "/test/result", [], []).ConfigureAwait(true);
            Assert.Equal(status, response.Status);
        }
        Assert.Equal(expectedRequests, requests);
    }

    [Fact]
    public async Task EndValidationDetectsMovementDespiteCacheAndPreservesInconclusiveEvidence()
    {
        using FixtureWorkspace workspace = new();
        string sha = "before";
        List<Uri> upstreamUris = [];
        using Handler handler = new(request =>
        {
            upstreamUris.Add(request.RequestUri!);
            if (upstreamUris.Count == 2)
                Assert.True(request.Headers.CacheControl?.NoCache);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { sha })) };
        });
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        var before = await proxy.SendAsync("GET", "/repos/owner/repo/commits/main", [], []).ConfigureAwait(true);
        sha = "after";
        var cached = await proxy.SendAsync("GET", "/repos/owner/repo/commits/main", [], []).ConfigureAwait(true);
        Assert.Equal(before.Body, cached.Body);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(proxy.CompleteAsync).ConfigureAwait(true);
        Assert.Contains("rerun required", failure.Message, StringComparison.Ordinal);
        Assert.Equal(2, proxy.UpstreamCount);
        Assert.NotEqual(upstreamUris[0], upstreamUris[1]);
        Assert.Contains("agentic_fixture_probe=", upstreamUris[1].Query, StringComparison.Ordinal);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "summary.json")).ConfigureAwait(true));
        Assert.Equal("inconclusive-source-movement", summary.RootElement.GetProperty("outcome").GetString());
        _ = Assert.Single(summary.RootElement.GetProperty("changed").EnumerateArray());
    }

    [Fact]
    public void ImmutableCommitsAreNotRecheckedButNewReleaseSelectionsAreDetected()
    {
        StoredResponse response = new(200, [], Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"sha\":\"unchanged\"}"));
        Assert.Null(GitHubCachingProxy.SourceIdentity("/repos/owner/repo/commits/" + new string('a', 40), response));
        StoredResponse release = new(200, [], Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"id\":123,\"tag_name\":\"v2\"}"));
        Assert.Equal("123:v2", GitHubCachingProxy.SourceIdentity("/repos/owner/repo/releases/latest", release));
    }

    [Fact]
    public async Task RepresentationAndMethodKeysStaySeparateAndQuotaMeasurementsStayLive()
    {
        using FixtureWorkspace workspace = new();
        using Handler handler = new(_ => new(HttpStatusCode.OK) { Content = new StringContent("{}") });
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        Dictionary<string, string[]> json = new(StringComparer.OrdinalIgnoreCase) { ["Accept"] = ["application/json"] };
        Dictionary<string, string[]> raw = new(StringComparer.OrdinalIgnoreCase) { ["Accept"] = ["application/vnd.github.raw"] };
        _ = await proxy.SendAsync("GET", "/blob", json, []).ConfigureAwait(true);
        _ = await proxy.SendAsync("GET", "/blob", raw, []).ConfigureAwait(true);
        _ = await proxy.SendAsync("HEAD", "/blob", json, []).ConfigureAwait(true);
        for (int index = 0; index < 2; index++)
        {
            _ = await proxy.SendAsync("GET", "/blob", json, []).ConfigureAwait(true);
            _ = await proxy.SendAsync("POST", "/test", json, [1]).ConfigureAwait(true);
            _ = await proxy.SendAsync("GET", "/rate_limit", json, []).ConfigureAwait(true);
        }
        Assert.Equal(7, proxy.UpstreamCount);
        Assert.Equal(2, proxy.CacheHits);
    }

    [Theory]
    [InlineData(200, "{\"id\":2,\"tag_name\":\"v2\"}", "inconclusive-source-movement")]
    [InlineData(503, "{}", "incomplete-network-verification")]
    public async Task NewReleaseOrUnavailableBoundaryInvalidatesRun(int finalStatus, string finalJson, string outcome)
    {
        using FixtureWorkspace workspace = new();
        int requests = 0;
        using Handler handler = new(_ => ++requests == 1
            ? new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":1,\"tag_name\":\"v1\"}") }
            : new((HttpStatusCode)finalStatus) { Content = new StringContent(finalJson) });
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        _ = await proxy.SendAsync("GET", "/repos/owner/repo/releases/latest", [], []).ConfigureAwait(true);
        _ = await Assert.ThrowsAsync<InvalidDataException>(proxy.CompleteAsync).ConfigureAwait(true);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "summary.json")).ConfigureAwait(true));
        Assert.Equal(outcome, summary.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(2, requests);
    }

    static void ConfigureSocket(SocketsHttpHandler handler, string path)
        => handler.ConnectCallback = async (_, cancellationToken) =>
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
