using System.Net;

namespace Agentic.Check;

// Distinct from recoverable source errors: callers must not swallow a quota failure and keep fetching.
sealed class GitHubRateLimitException : Exception
{
    public GitHubRateLimitException() { }

    public GitHubRateLimitException(string message) : base(message) { }

    public GitHubRateLimitException(string message, Exception innerException) : base(message, innerException) { }
}

static class GitHubRateLimits
{
    internal const string SecondaryMessage = "GitHub temporarily limited requests. Wait before retrying; signing in again will not resolve this limit.";

    internal static bool IsSecondary(string text)
        => text.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("abuse detection", StringComparison.OrdinalIgnoreCase);

    internal static async Task<string?> DescribeAsync(HttpResponseMessage response, bool authenticated, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return null;
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            return PrimaryMessage(authenticated);
        return response.StatusCode == HttpStatusCode.TooManyRequests || response.Headers.RetryAfter is not null
            || IsSecondary(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) ? SecondaryMessage : null;
    }

    internal static string PrimaryMessage(bool authenticated)
        => authenticated
            ? "GitHub’s API rate limit is exhausted. Retry after the limit resets."
            : "GitHub’s anonymous API rate limit has been reached. Please run `gh auth login`, then rerun Agentic.Check, or retry after the limit resets.";
}

sealed class GitHubRateLimitRunner(ICommandRunner runner, bool authenticated) : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var result = await runner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
        if (result.Success || fileName != "gh" || arguments is not ["skill", "install" or "update", ..])
            return result;
        string output = result.StandardError + "\n" + result.StandardOutput;
        if (GitHubRateLimits.IsSecondary(output))
            throw new GitHubRateLimitException(GitHubRateLimits.SecondaryMessage);
        if ((output.Contains("HTTP 403:", StringComparison.OrdinalIgnoreCase) || output.Contains("HTTP 429:", StringComparison.OrdinalIgnoreCase))
            && output.Contains("API rate limit exceeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubRateLimitException(GitHubRateLimits.PrimaryMessage(authenticated));
        }
        return result;
    }
}
