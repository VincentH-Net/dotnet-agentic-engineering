using System.Net;

namespace Agentic.Check.Tests;

public sealed class GitHubRateLimitTests
{
    const string AnonymousMessage = "GitHub’s anonymous API rate limit has been reached. Please run `gh auth login`, then rerun Agentic.Check, or retry after the limit resets.";
    const string AuthenticatedMessage = "GitHub’s API rate limit is exhausted. Retry after the limit resets.";
    const string PrimaryError = "could not resolve version: HTTP 403: API rate limit exceeded. (https://api.github.com/repos/owner/repo/releases/latest)";

    [Theory]
    [InlineData(false, 403)]
    [InlineData(false, 429)]
    [InlineData(true, 403)]
    [InlineData(true, 429)]
    public async Task DirectQuotaErrorsStopWithAppropriateGuidanceWithoutAdditionalRequests(bool authenticated, int status)
    {
        using Transport transport = new(request =>
        {
            Assert.Equal(authenticated ? "Bearer fixture-secret" : null, request.Headers.Authorization?.ToString());
            return QuotaResponse(status);
        });
        using var client = new GitHubAuthentication(new MappedCommandRunner(), authenticated ? "fixture-secret" : "").CreateHttpClient(transport);
        var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => client.GetStringAsync(new Uri("https://api.github.com/repos/owner/repo")));
        Assert.Equal(authenticated ? AuthenticatedMessage : AnonymousMessage, error.Message);
        Assert.Equal(1, transport.Requests);
    }

    [Theory]
    [InlineData(401, "Bad credentials", false)]
    [InlineData(403, "Resource not accessible", false)]
    [InlineData(403, "You have exceeded a secondary rate limit", true)]
    [InlineData(403, "abuse detection", true)]
    [InlineData(429, "Slow down", true)]
    public async Task OtherHttpFailuresNeverSuggestAnonymousLogin(int status, string body, bool limited)
    {
        using HttpResponseMessage response = new((HttpStatusCode)status) { Content = new StringContent(body) };
        string? message = await GitHubRateLimits.DescribeAsync(response, false, CancellationToken.None);
        Assert.Equal(limited ? GitHubRateLimits.SecondaryMessage : null, message);
    }

    [Theory]
    [InlineData(false, "install", 403)]
    [InlineData(false, "install", 429)]
    [InlineData(false, "update", 403)]
    [InlineData(false, "update", 429)]
    [InlineData(true, "install", 403)]
    [InlineData(true, "install", 429)]
    [InlineData(true, "update", 403)]
    [InlineData(true, "update", 429)]
    public async Task GhQuotaFailureStopsWithoutLookupRetryOrDebugLogging(bool authenticated, string operation, int status)
    {
        string[] arguments = operation == "install" ? ["skill", "install", "owner/repo", "sample"] : ["skill", "update", "--all"];
        MappedCommandRunner commands = new();
        commands.Set("gh", arguments, new(1, "", $"HTTP {status}: API rate limit exceeded."));
        GitHubAuthentication authentication = new(commands, authenticated ? "fixture-secret" : "");
        var runner = authentication.CreateSkillRunner();

        var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => runner.RunAsync("gh", arguments, ".", CancellationToken.None));

        Assert.Equal(authenticated ? AuthenticatedMessage : AnonymousMessage, error.Message);
        var call = Assert.Single(commands.Calls);
        Assert.Equal(arguments, call.Arguments);
        Assert.Equal(authenticated ? "fixture-secret" : null, call.Environment!["GH_TOKEN"]);
        Assert.Null(call.Environment["GITHUB_TOKEN"]);
        Assert.Null(call.Environment["GH_DEBUG"]);
    }

    [Theory]
    [InlineData("HTTP 403: Resource not accessible", false)]
    [InlineData("HTTP 401: Bad credentials", false)]
    [InlineData("error connecting to api.github.com", false)]
    [InlineData("HTTP 403: You have exceeded a secondary rate limit", true)]
    public async Task GhDistinguishesOtherFailuresWithoutAQuotaLookup(string message, bool limited)
    {
        MappedCommandRunner commands = new();
        CommandResult failure = new(1, "", message);
        commands.Set("gh", ["skill", "update", "--all", "--dry-run"], failure);
        GitHubRateLimitRunner runner = new(commands, false);
        if (limited)
        {
            var error = await Assert.ThrowsAsync<GitHubRateLimitException>(() => runner.RunAsync("gh", ["skill", "update", "--all", "--dry-run"], ".", CancellationToken.None));
            Assert.Equal(GitHubRateLimits.SecondaryMessage, error.Message);
        }
        else
        {
            Assert.Equal(failure, await runner.RunAsync("gh", ["skill", "update", "--all", "--dry-run"], ".", CancellationToken.None));
        }
        _ = Assert.Single(commands.Calls);
    }

    [Fact]
    public async Task SourceVersionResolutionDoesNotSwallowQuotaFailureOrFetchNextRepository()
    {
        using TempDirectory temp = new();
        using Transport transport = new(_ => QuotaResponse(403));
        using var client = new GitHubAuthentication(new MappedCommandRunner(), "").CreateHttpClient(transport);
        GitHubSourceVersionResolver resolver = new(client, new NullReporter());
        _ = await Assert.ThrowsAsync<GitHubRateLimitException>(() => resolver.ResolveVersionsAsync(["owner/first", "owner/second"], SourceVersionMode.Stable,
            new(0, temp.CreateDirectory("cache"), []), CancellationToken.None));
        Assert.Equal(1, transport.Requests);
    }

    [Fact]
    public async Task DirectiveResolutionDoesNotSwallowQuotaFailure()
    {
        using TempDirectory temp = new();
        using Transport transport = new(_ => QuotaResponse(403));
        using var client = new GitHubAuthentication(new MappedCommandRunner(), "").CreateHttpClient(transport);
        GitHubDirectiveSource source = new(client, new(0, temp.CreateDirectory("cache"), []));
        _ = await Assert.ThrowsAsync<GitHubRateLimitException>(() => source.ListAsync(CancellationToken.None));
        Assert.Equal(1, transport.Requests);
    }

    [Theory]
    [InlineData(PrimaryError, AnonymousMessage)]
    [InlineData("HTTP 403: secondary rate limit", GitHubRateLimits.SecondaryMessage)]
    public async Task WorkflowStopsInstallingAndReportsCompletedWorkBeforeQuotaFailure(string failure, string expectedMessage)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        temp.Write("AGENTS.md", "Keep these instructions.\n");
        string skills = Path.Combine(temp.Path, ".agents", "skills");
        MappedCommandRunner commands = new()
        {
            OnRun = call =>
            {
                if (call.Arguments is ["skill", "install", "owner/repo", "first", ..])
                    temp.Write(".agents/skills/first/SKILL.md", "Completed installation");
            }
        };
        ConfigureAnonymousPrerequisites(commands);
        commands.Set("gh", ["skill", "update", "--dir", skills, "--all", "--dry-run"], new(0, "All skills are up to date.", ""));
        commands.Set("gh", ["skill", "install", "owner/repo", "first", "--dir", skills], new(0, "installed", ""));
        commands.Set("gh", ["skill", "install", "owner/repo", "second", "--dir", skills], new(1, "", failure));
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commands, new FakePrompts(), reporter, new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(),
            [new("owner/repo", "first", "first", TechnologyNames.Dotnet, []), new("owner/repo", "second", "second", TechnologyNames.Dotnet, []),
             new("owner/repo", "third", "third", TechnologyNames.Dotnet, [])], readEnvironment: _ => null);
        string reportPath = Path.Combine(temp.Path, "report.json");

        var result = await workflow.RunAsync(new(temp.Path, false, true, reportPath, null, "codex", false), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedMessage, reporter.Errors);
        var completed = Assert.Single(result.Report.InstallResults);
        Assert.True(completed.Success);
        Assert.Equal("first", completed.InstallArg);
        Assert.Equal("Completed installation", await File.ReadAllTextAsync(Path.Combine(skills, "first", "SKILL.md")));
        Assert.Equal("Keep these instructions.\n", await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
        Assert.DoesNotContain(commands.Calls, call => call.Arguments.Contains("third"));
        Assert.DoesNotContain(commands.Calls, call => call.Arguments is ["skill", "update", ..] && !call.Arguments.Contains("--dry-run"));
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Equal("first", json.RootElement.GetProperty("installResults")[0].GetProperty("installArg").GetString());
        Assert.Contains(result.Report.Warnings, warning => warning.Contains("run is incomplete", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, PrimaryError, AnonymousMessage)]
    [InlineData(true, PrimaryError, AnonymousMessage)]
    [InlineData(false, "HTTP 403: secondary rate limit", GitHubRateLimits.SecondaryMessage)]
    [InlineData(true, "HTTP 403: secondary rate limit", GitHubRateLimits.SecondaryMessage)]
    public async Task WorkflowStopsAtFirstLimitedUpdateDirectory(bool duringScan, string failure, string expectedMessage)
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        temp.Write(".claude/skills/sample/SKILL.md", "Already installed");
        temp.Write(".agents/skills/sample/SKILL.md", "Already installed");
        MappedCommandRunner commands = new();
        ConfigureAnonymousPrerequisites(commands);
        foreach (string agent in new[] { ".claude", ".agents" })
        {
            string skills = Path.Combine(temp.Path, agent, "skills");
            commands.Set("gh", ["skill", "update", "--dir", skills, "--all", "--dry-run"], duringScan
                ? new(1, "", failure) : new(0, "Would update sample (owner/repo)", ""));
            commands.Set("gh", ["skill", "update", "--dir", skills, "--all"], new(1, "", failure));
        }
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(commands, new FakePrompts(), reporter, new FakeDirectiveSource(new Dictionary<string, string>()), new FakeSourceVersionResolver(),
            [new("owner/repo", "sample", "sample", TechnologyNames.Dotnet, [])], readEnvironment: _ => null);

        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, null, false), CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedMessage, reporter.Errors);
        Assert.Equal(duringScan ? 1 : 2, commands.Calls.Count(call => call.Arguments is ["skill", "update", ..] && call.Arguments.Contains("--dry-run")));
        Assert.Equal(duringScan ? 0 : 1, commands.Calls.Count(call => call.Arguments is ["skill", "update", ..] && !call.Arguments.Contains("--dry-run")));
        Assert.DoesNotContain(commands.Calls, call => call.Arguments is ["skill", "install", ..]);
    }

    static void ConfigureAnonymousPrerequisites(MappedCommandRunner commands)
    {
        commands.Set("gh", ["--version"], new(0, "gh version 2.101.0", ""));
        commands.Set("gh", ["skill", "--help"], new(0, "help", ""));
        commands.Set("gh", ["auth", "token", "--hostname", "github.com"], new(1, "", "no token"));
    }

    static HttpResponseMessage QuotaResponse(int status)
    {
        HttpResponseMessage response = new((HttpStatusCode)status);
        response.Headers.Add("X-RateLimit-Remaining", "0");
        return response;
    }

    sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }
}
