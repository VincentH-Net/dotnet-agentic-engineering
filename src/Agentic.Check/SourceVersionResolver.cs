using System.Net.Http.Headers;
using System.Text.Json;

namespace Agentic.Check;

interface ISourceVersionResolver
{
    Task<IReadOnlyDictionary<string, SourceVersionInfo>> ResolvePreviewVersionsAsync(
        IEnumerable<string> sourceRepos,
        DirectiveCacheSettings cacheSettings,
        CancellationToken cancellationToken);
}

sealed record SourceVersionInfo(string SourceRepo, string Ref, DateTimeOffset LastChangedAtUtc)
{
    public string Display
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Ref} @ {LastChangedAtUtc:yyyy-MM-dd HH:mm} UTC");
}

sealed class GitHubSourceVersionResolver(HttpClient? httpClient = null, IReporter? reporter = null) : ISourceVersionResolver
{
    readonly HttpClient httpClient = httpClient ?? CreateHttpClient();
    readonly IReporter? reporter = reporter;

    public async Task<IReadOnlyDictionary<string, SourceVersionInfo>> ResolvePreviewVersionsAsync(
        IEnumerable<string> sourceRepos,
        DirectiveCacheSettings cacheSettings,
        CancellationToken cancellationToken)
    {
        DirectiveHttpCache cache = new(cacheSettings, reporter);
        Dictionary<string, SourceVersionInfo> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string sourceRepo in sourceRepos.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string repositoryUrl = $"https://api.github.com/repos/{sourceRepo}";
            string repositoryContent = await GetStringAsync(new Uri(repositoryUrl), repositoryUrl, "repository metadata", cache, cancellationToken)
                .ConfigureAwait(false);
            using var repositoryDocument = JsonDocument.Parse(repositoryContent);
            string defaultBranch = repositoryDocument.RootElement.GetProperty("default_branch").GetString()
                ?? throw new DirectiveException($"Repository metadata from {repositoryUrl} is missing default_branch.");

            string escapedBranch = Uri.EscapeDataString(defaultBranch);
            string branchUrl = $"https://api.github.com/repos/{sourceRepo}/branches/{escapedBranch}";
            string branchContent = await GetStringAsync(new Uri(branchUrl), branchUrl, "default branch metadata", cache, cancellationToken)
                .ConfigureAwait(false);
            using var branchDocument = JsonDocument.Parse(branchContent);
            var commit = branchDocument.RootElement.GetProperty("commit").GetProperty("commit");
            string? dateValue = null;
            if (commit.TryGetProperty("committer", out var committer)
                && committer.TryGetProperty("date", out var committerDate))
            {
                dateValue = committerDate.GetString();
            }

            if (string.IsNullOrWhiteSpace(dateValue)
                && commit.TryGetProperty("author", out var author)
                && author.TryGetProperty("date", out var authorDate))
            {
                dateValue = authorDate.GetString();
            }

            if (!DateTimeOffset.TryParse(
                dateValue,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var lastChangedAtUtc))
            {
                throw new DirectiveException($"Default branch metadata from {branchUrl} is missing a valid commit date.");
            }

            versions[sourceRepo] = new SourceVersionInfo(sourceRepo, defaultBranch, lastChangedAtUtc.ToUniversalTime());
        }

        return versions;
    }

    async Task<string> GetStringAsync(
        Uri uri,
        string displayUrl,
        string description,
        DirectiveHttpCache cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryReadFresh(uri, out string? cachedContent))
        {
            return cachedContent;
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            if (cache.TryReadStale(uri, displayUrl, exception.Message, out cachedContent))
            {
                return cachedContent;
            }

            throw new DirectiveException($"Could not fetch {displayUrl}: {exception.Message}", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                int statusCode = (int)response.StatusCode;
                if (statusCode is 403 or 429
                    && cache.TryReadStale(uri, displayUrl, $"HTTP {statusCode}", out cachedContent))
                {
                    return cachedContent;
                }

                throw new DirectiveException($"Could not fetch {description}: HTTP {statusCode}.");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            cache.TryWrite(uri, content);
            return content;
        }
    }

    static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Agentic.Check", "0.1"));
        return client;
    }
}
