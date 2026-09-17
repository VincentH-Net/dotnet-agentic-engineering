using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Agentic.Check;

// This object deliberately is not a record: generated ToString output must never expose credentials.
sealed class GitHubAuthentication(ICommandRunner runner, string token)
{
    internal const string RequiredMessage = "Agentic.Check requires GitHub authentication because GitHub’s anonymous API rate limits are insufficient for the required `gh skill` usage.\n\nPlease run `gh auth login`, then retry.";
    internal const string ProbePath = "repos/" + CompanionDependency.SourceRepo;
    static readonly string[] TokenVariables = ["GH_TOKEN", "GITHUB_TOKEN"];
    static readonly IReadOnlyDictionary<string, string?> QuietEnvironment = new Dictionary<string, string?>
    {
        ["GH_DEBUG"] = null,
        ["DEBUG"] = null,
        ["GH_PROMPT_DISABLED"] = "1",
        ["GH_HOST"] = "github.com"
    };
    readonly string token = token;

    internal static async Task<(GitHubAuthentication? Authentication, string? Error)> ConnectAsync(
        ICommandRunner runner, string directory, CancellationToken cancellationToken,
        Func<string, string?>? readEnvironment = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await ConnectCoreAsync(runner, directory, readEnvironment, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "GitHub authentication verification timed out. Check connectivity and retry.");
        }
    }

    static async Task<(GitHubAuthentication? Authentication, string? Error)> ConnectCoreAsync(
        ICommandRunner runner, string directory, Func<string, string?>? readEnvironment, CancellationToken cancellationToken)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        string? tokenVariable = TokenVariables.FirstOrDefault(name => !string.IsNullOrEmpty(readEnvironment(name)));
        // Let gh resolve environment precedence, the active account, custom configuration and the OS credential store.
        var credential = await runner.RunAsync("gh", ["auth", "token", "--hostname", "github.com"], directory,
            cancellationToken, QuietEnvironment).ConfigureAwait(false);
        string token = credential.StandardOutput.Trim();
        if (!credential.Success || token.Length == 0)
            return (null, tokenVariable is null ? RequiredMessage : InvalidCredential(tokenVariable));
        if (token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            return (null, InvalidCredential(tokenVariable));

        GitHubAuthentication authentication = new(runner, token);
        // Validate the public repository access the tool actually needs, including installation/CI tokens.
        // A user-profile or GraphQL viewer probe would unnecessarily restrict supported credentials.
        var probe = await authentication.CommandRunner.RunAsync("gh",
            ["api", "--hostname", "github.com", "--include", "--method", "GET", ProbePath], directory, cancellationToken).ConfigureAwait(false);
        using var response = ReadResponse(probe.StandardOutput);
        if (response is null)
            return (null, "Could not verify GitHub authentication because GitHub could not be reached or returned an invalid response. Check connectivity and retry.");
        if (probe.Success && response.IsSuccessStatusCode)
        {
            // Public data can also return 200 anonymously. Check GitHub's actual REST budget, not just token presence.
            if (!response.Headers.TryGetValues("X-RateLimit-Limit", out var limits)
                || !int.TryParse(limits.FirstOrDefault(), CultureInfo.InvariantCulture, out int limit) || limit <= 60)
            {
                return (null, "GitHub did not confirm authenticated API access. Check the active GitHub credential and retry.");
            }

            return (authentication, null);
        }
        return (null, response.StatusCode == HttpStatusCode.Unauthorized
            ? InvalidCredential(tokenVariable)
            : await DescribeFailureAsync(response, cancellationToken).ConfigureAwait(false));
    }

    internal ICommandRunner CommandRunner => new AuthenticatedRunner(runner, token);

    internal HttpClient CreateHttpClient(HttpMessageHandler? transport = null)
    {
        GitHubAuthenticationHandler? handler = new(token, transport);
        try
        {
            HttpClient client = new(handler, disposeHandler: true);
            handler = null; // HttpClient now owns and disposes the complete handler chain.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Agentic.Check", "0.1"));
            return client;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    static string InvalidCredential(string? tokenVariable)
        => tokenVariable is null
            ? "GitHub rejected the active login credential. Please run `gh auth login`, then retry."
            : $"GitHub rejected the credential supplied by {tokenVariable}. Update or unset {tokenVariable}, then retry; it overrides the stored gh login.";

    internal static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return "GitHub rejected the authentication credential. Check GH_TOKEN/GITHUB_TOKEN or run `gh auth login`, then retry.";
        if (status is 403 or 429)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            {
                string retry = "Wait for the quota to reset, then retry.";
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resets)
                    && long.TryParse(resets.FirstOrDefault(), CultureInfo.InvariantCulture, out long reset)
                    && reset is >= 0 and <= 253402300799)
                {
                    retry = $"Retry after {DateTimeOffset.FromUnixTimeSeconds(reset).ToLocalTime():g}.";
                }

                return $"GitHub’s API rate limit is exhausted. {retry}";
            }
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return status == 429 || response.Headers.RetryAfter is not null
                || body.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase)
                || body.Contains("abuse detection", StringComparison.OrdinalIgnoreCase)
                ? "GitHub temporarily limited requests. Wait before retrying; signing in again will not resolve this limit."
                : "GitHub denied access (HTTP 403). Check the credential’s repository permissions and organization access requirements.";
        }
        return status >= 500
            ? $"GitHub is temporarily unavailable (HTTP {status}). Please retry later."
            : $"GitHub returned HTTP {status}. Check access to the requested source.";
    }

    static HttpResponseMessage? ReadResponse(string output)
    {
        string[] parts = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", 2, StringSplitOptions.None);
        string[] lines = parts[0].Split('\n');
        string[] status = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (status.Length < 2 || !status[0].StartsWith("HTTP/", StringComparison.Ordinal)
            || !int.TryParse(status[1], CultureInfo.InvariantCulture, out int code) || code is < 100 or > 599)
        {
            return null;
        }

        HttpResponseMessage response = new((HttpStatusCode)code) { Content = new StringContent(parts.Length == 2 ? parts[1] : string.Empty) };
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
                _ = response.Headers.TryAddWithoutValidation(line[..colon], line[(colon + 1)..].Trim());
        }
        return response;
    }

    sealed class AuthenticatedRunner(ICommandRunner runner, string token) : ICommandRunner
    {
        public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
            CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
        {
            Dictionary<string, string?> childEnvironment = environment is null ? [] : new(environment);
            if (fileName == "gh")
            {
                foreach (var (name, value) in QuietEnvironment)
                    childEnvironment[name] = value;
                childEnvironment["GH_TOKEN"] = token;
                childEnvironment["GITHUB_TOKEN"] = null;
            }
            var result = await runner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, childEnvironment).ConfigureAwait(false);
            // Never persist a credential accidentally echoed by a child process in reports or recordings.
            return result with
            {
                StandardOutput = result.StandardOutput.Replace(token, "[REDACTED]", StringComparison.Ordinal),
                StandardError = result.StandardError.Replace(token, "[REDACTED]", StringComparison.Ordinal)
            };
        }
    }
}

sealed class GitHubAuthenticationHandler : DelegatingHandler
{
    readonly string token;

    internal GitHubAuthenticationHandler(string token, HttpMessageHandler? transport = null)
    {
        this.token = token;
        InnerHandler = transport ?? new HttpClientHandler { AllowAutoRedirect = false };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A request URI is required.");
        for (int redirects = 0; ; redirects++)
        {
            // Source reads are GETs. Rebuild every hop so credentials cannot follow a cross-host redirect.
            using HttpRequestMessage hop = new(request.Method, uri);
            foreach (var header in request.Headers.Where(header => !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
                _ = hop.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort)
                hop.Headers.Authorization = new("Bearer", token);
            var response = await base.SendAsync(hop, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                || response.Headers.Location is not { } location)
            {
                return response;
            }

            response.Dispose();
            Uri next = new(uri, location);
            if (redirects >= 5 || next.Scheme != Uri.UriSchemeHttps)
                throw new HttpRequestException("GitHub source redirect was unsafe or exceeded the redirect limit.");
            uri = next;
        }
    }
}
