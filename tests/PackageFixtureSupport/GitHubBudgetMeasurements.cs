using System.Globalization;
using System.Text.Json;

namespace Agentic.PackageFixtures;

sealed record BudgetSample(DateTimeOffset AtUtc, int? Status, int? Limit, int? Remaining, int? Used,
    long? Reset, string? Resource, string? RequestId, string? Error);
sealed record BudgetInterval(string Budget, BudgetSample Before, BudgetSample After, int? ObservedDelta, string Interpretation);
sealed record BudgetCounter(bool Authenticated, int Limit, int Remaining, int Used, long Reset, string Resource);

// Samples are observations of shared account/IP budgets, never exclusive test attribution.
sealed class GitHubBudgetMeasurements(GitHubCachingProxy gateway)
{
    const string Endpoint = "/repos/" + SourceOracle.OwnRepository;
    readonly Dictionary<string, BudgetSample> before = new(StringComparer.Ordinal);
    string credential = string.Empty;

    internal async Task StartAsync(string token)
    {
        credential = token;
        foreach (bool authenticated in new[] { true, false })
        {
            string name = authenticated ? "authenticated" : "anonymous";
            before.Add(name, await SampleAsync(authenticated, "budget-start").ConfigureAwait(false));
        }
        // Retain start evidence even if the host crashes before collection cleanup.
        FixtureFiles.WriteJson(Path.Combine(gateway.Reports, "budgets.json"), new { outcome = "started", endpoint = Endpoint, before });
        foreach (var (name, sample) in before)
            Console.WriteLine($"GitHub {name} REST budget before: {Display(sample)}");
    }

    internal async Task CompleteAsync()
    {
        Dictionary<string, BudgetSample> after = new(StringComparer.Ordinal);
        foreach (bool authenticated in new[] { true, false })
            after.Add(authenticated ? "authenticated" : "anonymous", await SampleAsync(authenticated, "budget-end").ConfigureAwait(false));
        List<BudgetCounter> counters = [];
        Dictionary<string, int> requests = new(StringComparer.Ordinal);
        await foreach (string line in File.ReadLinesAsync(Path.Combine(gateway.Reports, "requests.jsonl")).ConfigureAwait(false))
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            if (entry.GetProperty("cacheHit").GetBoolean())
                continue;
            bool authenticated = entry.GetProperty("authorizationPresent").GetBoolean();
            string kind = entry.GetProperty("path").GetString() == "/graphql" ? "GraphQL" : "REST";
            string key = (authenticated ? "authenticated" : "anonymous") + kind;
            requests[key] = requests.GetValueOrDefault(key) + 1;
            if (entry.TryGetProperty("quota", out var quota))
            {
                var headers = quota.EnumerateObject().ToDictionary(item => item.Name, item => new[] { item.Value.GetString()! }, StringComparer.OrdinalIgnoreCase);
                var sample = ReadSample(new(entry.GetProperty("status").GetInt32(), headers, []), authenticated);
                if (sample.Error is null)
                    counters.Add(new(authenticated, sample.Limit!.Value, sample.Remaining!.Value, sample.Used!.Value, sample.Reset!.Value, sample.Resource!));
            }
        }
        BudgetInterval[] intervals = [.. before.Select(item => Compare(item.Key, item.Value, after[item.Key]))];
        var sequences = counters.GroupBy(item => (item.Authenticated, item.Resource, item.Limit, item.Reset))
            .Select(group => new { group.Key.Authenticated, group.Key.Resource, group.Key.Limit, group.Key.Reset,
                observations = group.Count(), minimumUsed = group.Min(item => item.Used), maximumUsed = group.Max(item => item.Used),
                counterDecreased = group.Zip(group.Skip(1)).Any(pair => pair.Second.Used < pair.First.Used) }).ToArray();
        bool inconsistent = sequences.Any(sequence => sequence.counterDecreased)
            || sequences.GroupBy(sequence => (sequence.Authenticated, sequence.Resource)).Any(group => group.Count() > 1);
        string report = Path.Combine(gateway.Reports, "budgets.json");
        FixtureFiles.WriteJson(report, new
        {
            outcome = intervals.All(interval => interval.Before.Error is null && interval.After.Error is null) ? "measured" : "incomplete-measurements",
            endpoint = Endpoint, intervals, counterSequencesDifferOrDecrease = inconsistent, counterSequences = sequences,
            upstreamRequests = requests, upstreamCount = gateway.UpstreamCount, cacheHits = gateway.CacheHits,
            measurementRequests = 4,
            attribution = "Endpoint deltas include unrelated account/IP traffic and the ending probe. Start probes precede gh authentication and source priming; end probes follow source validation. Direct product HTTP requests bypass the gateway, so anonymous upstream counts here count probes only. Resets or differing counter sequences prevent interpreting a delta as actual test usage."
        });
        string summary = string.Join('\n', intervals.Select(interval => $"GitHub {interval.Budget} REST budget: {Display(interval.Before)} -> {Display(interval.After)}; observed delta {interval.ObservedDelta?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}. {interval.Interpretation}"))
            + $"\nCounter sequences differ/decrease: {inconsistent}. Gateway upstream requests: {gateway.UpstreamCount}; cache hits: {gateway.CacheHits}. Not exclusive test quota usage. Details: {report}\n";
        await File.WriteAllTextAsync(Path.Combine(gateway.Reports, "budgets.txt"), summary).ConfigureAwait(false);
        Console.WriteLine(summary);
    }

    async Task<BudgetSample> SampleAsync(bool authenticated, string phase)
    {
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = ["agentic-fixture-budget-measurement"], ["Accept"] = ["application/vnd.github+json"],
            ["X-GitHub-Api-Version"] = ["2022-11-28"]
        };
        if (authenticated)
            headers["Authorization"] = ["token " + credential];
        try
        {
            var response = await gateway.SendAsync("GET", Endpoint, headers, [], true, phase).ConfigureAwait(false);
            return ReadSample(response, authenticated);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            // Never persist exception messages, which may contain sensitive HTTP details.
            return new(DateTimeOffset.UtcNow, null, null, null, null, null, null, null, exception.GetType().Name);
        }
    }

    internal static BudgetSample ReadSample(StoredResponse response, bool authenticated)
    {
        string? Header(string name) => response.Headers.GetValueOrDefault(name)?.SingleOrDefault();
        int? Number(string name) => int.TryParse(Header(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        int? limit = Number("X-RateLimit-Limit"), remaining = Number("X-RateLimit-Remaining"), used = Number("X-RateLimit-Used");
        long? reset = long.TryParse(Header("X-RateLimit-Reset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;
        string? resource = Header("X-RateLimit-Resource");
        string? error = response.Status != 200 ? $"HTTP {response.Status}"
            : limit is null or <= 0 || remaining is null or < 0 || used is null or < 0 || reset is null or <= 0 || resource != "core" ? "Missing or invalid core quota headers"
            : remaining + used != limit ? "Inconsistent used/remaining/limit headers"
            : authenticated ? limit <= 60 ? "Authenticated probe returned anonymous-sized budget" : null
            : limit > 60 ? "Anonymous probe returned unexpected budget size" : null;
        return new(DateTimeOffset.UtcNow, response.Status, limit, remaining, used, reset, resource, Header("X-GitHub-Request-Id"), error);
    }

    internal static BudgetInterval Compare(string name, BudgetSample before, BudgetSample after)
    {
        string? reason = before.Error is not null || after.Error is not null ? "Measurement unavailable; no delta inferred."
            : before.Reset != after.Reset ? "Reset window changed; no delta inferred."
            : before.Limit != after.Limit || before.Resource != after.Resource ? "Counter identity changed; no delta inferred."
            : after.Used < before.Used || after.Remaining > before.Remaining ? "Counter moved backwards; no delta inferred."
            : after.Used - before.Used != before.Remaining - after.Remaining ? "Inconsistent counter deltas; no delta inferred." : null;
        return new(name, before, after, reason is null ? before.Remaining - after.Remaining : null,
            reason ?? "Observed shared-budget change at the sampled endpoint, including the ending probe; not exclusive test usage.");
    }

    static string Display(BudgetSample sample) => sample.Error is not null ? $"unavailable ({sample.Error})"
        : $"{sample.Remaining}/{sample.Limit} remaining, reset {sample.Reset}";
}
