using System.Net;
using System.Text.Json;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class GitHubBudgetTests
{
    [Theory]
    [InlineData(1000, 5000, 12, null, 2)]
    [InlineData(2000, 5000, 12, null, null)]
    [InlineData(1000, 6000, 12, null, null)]
    [InlineData(1000, 5000, 9, null, null)]
    [InlineData(1000, 5000, 12, "HTTP 403", null)]
    public void OnlyComparableMeasurementsProduceObservedDeltas(long reset, int limit, int used, string? error, int? delta)
    {
        BudgetSample before = new(DateTimeOffset.UnixEpoch, 200, 5000, 4990, 10, 1000, "core", "first", null);
        var after = before with { Reset = reset, Limit = limit, Used = used, Remaining = limit - used, Error = error };
        Assert.Equal(delta, GitHubBudgetMeasurements.Compare("authenticated", before, after).ObservedDelta);
    }

    [Theory]
    [InlineData(true, 60, 59, 1)]
    [InlineData(false, 5000, 4999, 1)]
    [InlineData(true, 5000, 4999, 20)]
    public void WrongAuthenticationBudgetOrInconsistentHeadersAreNotValidSamples(bool authenticated, int limit, int remaining, int used)
    {
        using var response = Response(limit, remaining, used);
        var sample = GitHubBudgetMeasurements.ReadSample(new(200, response.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase), []), authenticated);
        Assert.NotNull(sample.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FreshMeasurementsSurviveSourceFailureAndCountOnlyUpstreamTraffic(bool sourceMoves, bool differentCounters)
    {
        using FixtureWorkspace workspace = new();
        using Handler handler = new() { DifferentCounters = differentCounters };
        GitHubCachingProxy proxy = new(Path.Combine(workspace.Root, "reports"), handler);
        await using var lifetime = proxy.ConfigureAwait(true);
        GitHubBudgetMeasurements measurements = new(proxy);
        await measurements.StartAsync("test-secret").ConfigureAwait(true);
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = ["token test-secret"] };
        _ = await proxy.SendAsync("GET", "/repos/owner/repo/commits/main", headers, []).ConfigureAwait(true);
        _ = await proxy.SendAsync("GET", "/repos/owner/repo/commits/main", headers, []).ConfigureAwait(true);
        handler.Sha = sourceMoves ? "after" : "before";
        if (sourceMoves)
            _ = await Assert.ThrowsAsync<InvalidDataException>(() => proxy.CompleteAsync(measurements.CompleteAsync)).ConfigureAwait(true);
        else
            await proxy.CompleteAsync(measurements.CompleteAsync).ConfigureAwait(true);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "budgets.json")).ConfigureAwait(true));
        var json = report.RootElement;
        Assert.Equal("measured", json.GetProperty("outcome").GetString());
        Assert.Equal(3, json.GetProperty("intervals")[0].GetProperty("observedDelta").GetInt32());
        Assert.Equal(1, json.GetProperty("intervals")[1].GetProperty("observedDelta").GetInt32());
        Assert.Equal(4, json.GetProperty("measurementRequests").GetInt32());
        Assert.Equal(6, json.GetProperty("upstreamCount").GetInt32());
        Assert.Equal(1, json.GetProperty("cacheHits").GetInt32());
        Assert.Equal(differentCounters, json.GetProperty("counterSequencesDifferOrDecrease").GetBoolean());
        Assert.Equal(4, handler.Probes.Count);
        Assert.Equal(4, handler.Probes.Distinct().Count());
        Assert.Equal(2, handler.Anonymous);
        using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "summary.json")).ConfigureAwait(true));
        Assert.Equal(1, summary.RootElement.GetProperty("sourceChecks").GetInt32());
        Assert.Equal(proxy.UpstreamCount, summary.RootElement.GetProperty("upstreamCount").GetInt32());
        Assert.DoesNotContain("test-secret", await File.ReadAllTextAsync(Path.Combine(proxy.Reports, "budgets.json")).ConfigureAwait(true), StringComparison.Ordinal);
    }

    static HttpResponseMessage Response(int limit, int remaining, int used)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.Add("X-RateLimit-Limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        response.Headers.Add("X-RateLimit-Remaining", remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
        response.Headers.Add("X-RateLimit-Used", used.ToString(System.Globalization.CultureInfo.InvariantCulture));
        response.Headers.Add("X-RateLimit-Reset", "1000");
        response.Headers.Add("X-RateLimit-Resource", "core");
        return response;
    }

    sealed class Handler : HttpMessageHandler
    {
        int authenticated;
        internal int Anonymous { get; private set; }
        internal string Sha { get; set; } = "before";
        internal List<Uri> Probes { get; } = [];
        internal bool DifferentCounters { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool auth = request.Headers.Authorization is not null;
            int used = auth ? ++authenticated : ++Anonymous;
            int limit = auth ? 5000 : 60;
            var response = Response(limit, limit - used, used);
            if (DifferentCounters && request.RequestUri!.AbsolutePath.Contains("/commits/", StringComparison.Ordinal))
            {
                _ = response.Headers.Remove("X-RateLimit-Reset");
                response.Headers.Add("X-RateLimit-Reset", "2000");
            }
            response.Content = new StringContent(JsonSerializer.Serialize(new { default_branch = "main", sha = Sha }));
            if (request.RequestUri!.AbsolutePath == "/repos/" + SourceOracle.OwnRepository)
            {
                Assert.True(request.Headers.CacheControl?.NoCache);
                Assert.Contains("agentic_fixture_probe=", request.RequestUri.Query, StringComparison.Ordinal);
                Probes.Add(request.RequestUri);
            }
            return Task.FromResult(response);
        }
    }
}
