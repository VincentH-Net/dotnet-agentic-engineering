using System.Net;
using System.Text.Json;

namespace Agentic.Check.Tests;

public sealed class GitHubAuthenticationTests
{
    static readonly string[] TokenArguments = ["auth", "token", "--hostname", "github.com"];
    static readonly string[] ProbeArguments = ["api", "--hostname", "github.com", "--include", "--method", "GET", GitHubAuthentication.ProbePath];

    [Fact]
    public async Task CredentialIsResolvedOnceAndPinnedToGhChildrenWithoutChangingOtherProcesses()
    {
        MappedCommandRunner runner = new();
        var (authentication, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, _ => null);
        Assert.Null(error);
        Assert.NotNull(authentication);
        string secret = AuthenticationTestCommands.Token;
        runner.Set("gh", ["skill", "list"], new(0, secret, "Bearer " + secret));
        var result = await authentication.CommandRunner.RunAsync("gh", ["skill", "list"], ".", CancellationToken.None);
        _ = await authentication.CommandRunner.RunAsync("dotnet", ["--version"], ".", CancellationToken.None);
        Assert.Equal("[REDACTED]", result.StandardOutput);
        Assert.Equal("Bearer [REDACTED]", result.StandardError);
        _ = Assert.Single(runner.Calls, call => call.Arguments.SequenceEqual(TokenArguments));
        foreach (var call in runner.Calls.Where(call => call.Arguments[0] is "api" or "skill"))
        {
            Assert.Equal(secret, call.Environment!["GH_TOKEN"]);
            Assert.Null(call.Environment["GITHUB_TOKEN"]);
            Assert.Null(call.Environment["GH_DEBUG"]);
            Assert.Equal("github.com", call.Environment["GH_HOST"]);
            Assert.DoesNotContain(secret, call.Arguments);
        }
        Assert.DoesNotContain("GH_TOKEN", runner.Calls.Last().Environment!.Keys);
        Assert.DoesNotContain(secret, authentication.ToString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200, "X-RateLimit-Limit: 1000", "", null)]
    [InlineData(200, "X-RateLimit-Limit: 60", "", "did not confirm")]
    [InlineData(200, "", "", "did not confirm")]
    [InlineData(401, "", "", "rejected the active login")]
    [InlineData(403, "X-RateLimit-Remaining: 0", "", "rate limit is exhausted")]
    [InlineData(403, "", "Resource not accessible", "repository permissions")]
    [InlineData(403, "Retry-After: 60", "", "temporarily limited")]
    [InlineData(403, "", "You have exceeded a secondary rate limit", "temporarily limited")]
    [InlineData(429, "", "", "temporarily limited")]
    [InlineData(503, "", "", "temporarily unavailable")]
    public async Task ValidationDistinguishesCredentialQuotaPermissionAndServiceFailures(int status, string headers, string body, string? expected)
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", ProbeArguments, new(status == 200 ? 0 : 1, $"HTTP/2.0 {status}\n{headers}\n\n{body}", "sensitive upstream diagnostics"));
        var (authentication, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, _ => null);
        if (expected is null)
        {
            Assert.NotNull(authentication);
            Assert.Null(error);
        }
        else
        {
            Assert.Null(authentication);
            Assert.Contains(expected, error!, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive", error!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("GH_TOKEN")]
    [InlineData("GITHUB_TOKEN")]
    public async Task InvalidEnvironmentCredentialGuidanceExplainsWhyLoginDoesNotFixIt(string variable)
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", ProbeArguments, new(1, "HTTP/1.1 401 Unauthorized\n\n{}", ""));
        var (_, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, name => name == variable ? "configured" : null);
        Assert.Contains("Update or unset " + variable, error!, StringComparison.Ordinal);
        Assert.Contains("overrides the stored gh login", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "  \n")]
    [InlineData(1, "")]
    public async Task UnusableEnvironmentCredentialDoesNotSuggestLoginInstead(int exitCode, string token)
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", TokenArguments, new(exitCode, token, "private credential diagnostics"));
        var (_, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None,
            name => name == "GH_TOKEN" ? "  " : null);
        Assert.Contains("Update or unset GH_TOKEN", error!, StringComparison.Ordinal);
        Assert.DoesNotContain("auth login", error!, StringComparison.Ordinal);
        Assert.DoesNotContain("private", error!, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("api"));
    }

    [Fact]
    public async Task ConnectionFailureDoesNotAskUserToLogInAgain()
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", ProbeArguments, new(1, "", "error connecting: sensitive-details"));
        var (_, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, _ => null);
        Assert.Contains("connectivity", error!, StringComparison.Ordinal);
        Assert.DoesNotContain("auth login", error!, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-details", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAuthenticationContinuesNormalAndDryRunsWithoutLoginGuidance(bool dryRun)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        temp.Write("AGENTS.md", "preserve me\n");
        string reportPath = Path.Combine(temp.Path, "report.json");
        string skills = Path.Combine(temp.Path, ".agents", "skills");
        MappedCommandRunner runner = new();
        runner.Set("gh", ["--version"], new(0, "gh version 2.101.0", ""));
        runner.Set("gh", ["skill", "--help"], new(0, "gh skill help", ""));
        runner.Set("gh", TokenArguments, new(1, "", "no oauth token found"));
        runner.Set("gh", ["skill", "update", "--dir", skills, "--all", "--dry-run"], new(0, "All skills are up to date.", ""));
        runner.Set("gh", ["skill", "install", "owner/repo", "sample", "--dir", skills], new(0, "installed", ""));
        FakeSourceVersionResolver resolver = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), reporter, new FakeDirectiveSource(new Dictionary<string, string>()), resolver,
            [new("owner/repo", "sample", "sample", TechnologyNames.Dotnet, [])], readEnvironment: _ => null);

        var result = await workflow.RunAsync(new(temp.Path, dryRun, true, reportPath, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("owner/repo", resolver.RequestedSourceRepos);
        Assert.Empty(reporter.Errors);
        Assert.DoesNotContain(reporter.Warnings, message => message.Contains("auth login", StringComparison.Ordinal));
        Assert.Equal("preserve me\n", await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
        Assert.Equal(dryRun ? 0 : 1, runner.Calls.Count(call => call.Arguments is ["skill", "install", ..]));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("api"));
        foreach (var call in runner.Calls.Where(call => call.Arguments is ["skill", "install" or "update", ..]))
        {
            Assert.Null(call.Environment!["GH_TOKEN"]);
            Assert.Null(call.Environment["GITHUB_TOKEN"]);
        }
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Contains(json.RootElement.GetProperty("prerequisites").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "GitHub access" && item.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    public async Task MissingCredentialCreatesAnAnonymousClientWithoutAValidationRequest(int exitCode, string token)
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", TokenArguments, new(exitCode, token, "no token"));
        var (authentication, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, _ => null);
        Assert.Null(error);
        Assert.NotNull(authentication);
        Assert.False(authentication.IsAuthenticated);
        _ = Assert.Single(runner.Calls);
        using Transport transport = new(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new StringContent("public content") };
        });
        using var client = authentication.CreateHttpClient(transport);
        Assert.Equal("public content", await client.GetStringAsync(new Uri("https://api.github.com/repos/owner/repo")));
    }

    [Fact]
    public async Task CredentialCommandFailureDoesNotSilentlyBecomeAnonymous()
    {
        MappedCommandRunner runner = new();
        runner.Set("gh", TokenArguments, new(127, "", "command not found"));
        var (authentication, error) = await GitHubAuthentication.ConnectAsync(runner, ".", CancellationToken.None, _ => null);
        Assert.Null(authentication);
        Assert.Contains("Could not read GitHub credentials", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://api.github.com/repos/example/repo", true)]
    [InlineData("https://raw.githubusercontent.com/example/repo/main/file", false)]
    [InlineData("https://api.github.com.evil.invalid/file", false)]
    [InlineData("http://api.github.com/file", false)]
    [InlineData("https://api.github.com:444/file", false)]
    public async Task DirectRequestsOnlyAuthenticateTheHttpsGitHubApi(string address, bool authenticated)
    {
        using Transport transport = new(request =>
        {
            Assert.Equal(authenticated ? "Bearer fixture-secret" : null, request.Headers.Authorization?.ToString());
            return new(HttpStatusCode.OK) { Content = new StringContent("public content") };
        });
        using GitHubAuthenticationHandler handler = new("fixture-secret", transport);
        using HttpClient client = new(handler, disposeHandler: false);
        Assert.Equal("public content", await client.GetStringAsync(new Uri(address)));
    }

    [Fact]
    public async Task RedirectsReevaluateAuthenticationForEveryHostAndPreserveRepresentationHeaders()
    {
        int requestCount = 0;
        using Transport transport = new(request =>
        {
            requestCount++;
            Assert.Equal("application/vnd.github+json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal(requestCount == 2 ? null : "Bearer fixture-secret", request.Headers.Authorization?.ToString());
            if (requestCount == 3)
                return new(HttpStatusCode.OK) { Content = new StringContent("done") };
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = new(requestCount == 1 ? "https://raw.githubusercontent.com/source" : "https://api.github.com/redirected");
            return response;
        });
        using GitHubAuthenticationHandler handler = new("fixture-secret", transport);
        using HttpClient client = new(handler, disposeHandler: false);
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        Assert.Equal("done", await client.GetStringAsync(new Uri("https://api.github.com/start")));
        Assert.Equal(3, requestCount);
    }

    [Fact]
    public async Task RedirectCannotDowngradeToHttp()
    {
        using Transport transport = new(_ => new(HttpStatusCode.Found) { Headers = { Location = new("http://api.github.com/file") } });
        using GitHubAuthenticationHandler handler = new("fixture-secret", transport);
        using HttpClient client = new(handler, disposeHandler: false);
        _ = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(new Uri("https://api.github.com/start")));
    }

    sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

}
