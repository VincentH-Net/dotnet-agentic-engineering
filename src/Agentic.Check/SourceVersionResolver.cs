using System.Net.Http.Headers;
using System.Text.Json;

namespace Agentic.Check;

interface ISourceVersionResolver
{
    Task<IReadOnlyDictionary<string, SourceVersionInfo>> ResolveVersionsAsync(
        IEnumerable<string> sourceRepos,
        SourceVersionMode sourceVersionMode,
        DirectiveCacheSettings cacheSettings,
        CancellationToken cancellationToken);
}

sealed record SourceVersionInfo(string SourceRepo, string Ref, DateTimeOffset LastChangedAtUtc)
{
    public string Display
        => LastChangedAtUtc == DateTimeOffset.MinValue
            ? Ref
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Ref} @ {LastChangedAtUtc:yyyy-MM-dd HH:mm} UTC");
}

sealed class GitHubSourceVersionResolver(HttpClient? httpClient = null, IReporter? reporter = null) : ISourceVersionResolver
{
    const string NoReleaseSentinel = "__agentic_check_no_latest_release__";

    readonly HttpClient httpClient = httpClient ?? CreateHttpClient();
    readonly IReporter? reporter = reporter;

    public async Task<IReadOnlyDictionary<string, SourceVersionInfo>> ResolveVersionsAsync(
        IEnumerable<string> sourceRepos,
        SourceVersionMode sourceVersionMode,
        DirectiveCacheSettings cacheSettings,
        CancellationToken cancellationToken)
    {
        DirectiveHttpCache cache = new(cacheSettings, reporter);
        Dictionary<string, SourceVersionInfo> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach (string sourceRepo in sourceRepos.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (sourceVersionMode == SourceVersionMode.Stable)
                {
                    var stableVersion = await TryResolveLatestReleaseAsync(sourceRepo, cache, cancellationToken)
                        .ConfigureAwait(false);
                    if (stableVersion is not null)
                    {
                        versions[sourceRepo] = stableVersion;
                        continue;
                    }
                }

                versions[sourceRepo] = await ResolveDefaultBranchAsync(sourceRepo, cache, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DirectiveException exception)
            {
                reporter?.Warning($"Could not resolve skill source version for {sourceRepo}: {exception.Message}");
            }
        }

        return versions;
    }

    async Task<SourceVersionInfo?> TryResolveLatestReleaseAsync(
        string sourceRepo,
        DirectiveHttpCache cache,
        CancellationToken cancellationToken)
    {
        string latestReleaseUrl = $"https://api.github.com/repos/{sourceRepo}/releases/latest";
        string? releaseContent = await GetOptionalStringAsync(new Uri(latestReleaseUrl), latestReleaseUrl, "latest release", cache, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(releaseContent)
            || releaseContent.Equals(NoReleaseSentinel, StringComparison.Ordinal))
        {
            return null;
        }

        using var document = JsonDocument.Parse(releaseContent);
        string? tagName = document.RootElement.GetProperty("tag_name").GetString();
        var releaseDate = ResolveReleaseDate(document, sourceRepo, latestReleaseUrl);
        return string.IsNullOrWhiteSpace(tagName)
            ? null
            : new SourceVersionInfo(sourceRepo, tagName, releaseDate.ToUniversalTime());
    }

    static DateTimeOffset ResolveReleaseDate(JsonDocument document, string sourceRepo, string latestReleaseUrl)
    {
        string? dateValue = null;
        if (document.RootElement.TryGetProperty("published_at", out var publishedAt))
        {
            dateValue = publishedAt.GetString();
        }

        if (string.IsNullOrWhiteSpace(dateValue)
            && document.RootElement.TryGetProperty("created_at", out var createdAt))
        {
            dateValue = createdAt.GetString();
        }

        if (!DateTimeOffset.TryParse(
            dateValue,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var releaseDate))
        {
            throw new DirectiveException($"Latest release metadata for {sourceRepo} from {latestReleaseUrl} is missing a valid release date.");
        }

        return releaseDate;
    }

    async Task<SourceVersionInfo> ResolveDefaultBranchAsync(
        string sourceRepo,
        DirectiveHttpCache cache,
        CancellationToken cancellationToken)
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

        return new SourceVersionInfo(sourceRepo, defaultBranch, lastChangedAtUtc.ToUniversalTime());
    }

    async Task<string?> GetOptionalStringAsync(
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
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                cache.TryWrite(uri, NoReleaseSentinel);
                return null;
            }

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
