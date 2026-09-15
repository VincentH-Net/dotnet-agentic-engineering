using Agentic.PackageFixtures;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class PackageRunTests(ITestOutputHelper output)
{
    [Fact]
    public void CacheSurvivesTargetDisposalButChannelsRunsAndPackagesStaySeparate()
    {
        using FixtureWorkspace storage = new();
        PackageTestRun run = new(storage.Root);
        string preview;
        using (FixtureWorkspace first = new())
        {
            run.UseCache(first, "package-a", true, output.WriteLine);
            preview = first.Environment["AGENTIC_CHECK_CACHE_DIR"];
            Assert.Empty(Directory.EnumerateFileSystemEntries(preview));
            // Support-test data only; package scenarios never seed responses.
            DirectiveHttpCache cache = new(new(3600, preview, []), null);
            cache.TryWrite(new("https://example.invalid/source"), "authentic-response-placeholder");
        }
        using FixtureWorkspace next = new();
        run.UseCache(next, "package-a", true, output.WriteLine);
        Assert.Equal(preview, next.Environment["AGENTIC_CHECK_CACHE_DIR"]);
        DirectiveHttpCache reused = new(new(3600, preview, []), null);
        Assert.True(reused.TryReadFresh(new("https://example.invalid/source"), out string? content));
        Assert.Equal("authentic-response-placeholder", content);
        run.UseCache(next, "package-a", false, output.WriteLine);
        Assert.NotEqual(preview, next.Environment["AGENTIC_CHECK_CACHE_DIR"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(next.Environment["AGENTIC_CHECK_CACHE_DIR"]));
        _ = Assert.Throws<InvalidDataException>(() => run.UseCache(next, "package-b", true, output.WriteLine));
        PackageTestRun anotherRun = new(storage.Root);
        anotherRun.UseCache(next, "package-a", true, output.WriteLine);
        Assert.NotEqual(preview, next.Environment["AGENTIC_CHECK_CACHE_DIR"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(next.Environment["AGENTIC_CHECK_CACHE_DIR"]));
    }

    [Theory]
    [InlineData("preferred", "secondary", "preferred", 0)]
    [InlineData(null, "secondary", "secondary", 0)]
    [InlineData(null, null, "login", 1)]
    public async Task CredentialsAreResolvedOnceAndExplicitlyForwarded(string? ghToken, string? githubToken, string expected, int expectedLoginReads)
    {
        int loginReads = 0;
        FixtureAuthentication credentials = new(name => name == "GH_TOKEN" ? ghToken : githubToken, () =>
        {
            loginReads++;
            return Task.FromResult(new ProcessResult(0, "login\n", string.Empty));
        });
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Dictionary<string, string> environment = [];
            await credentials.ApplyAsync(environment).ConfigureAwait(true);
            Assert.Equal(expected, environment["GH_TOKEN"]);
            Assert.Empty(environment["GITHUB_TOKEN"]);
        }
        Assert.Equal(expectedLoginReads, loginReads);
    }

    [Theory]
    [InlineData("", "", "missing", 0)]
    [InlineData("secret", "invalid secret", "authentication/access", 1)]
    [InlineData("secret", "error connecting to api.github.com", "connectivity", 1)]
    public async Task PreflightFailsEarlyWithoutLeakingCommandOutput(string credential, string error, string expected, int expectedCalls)
    {
        int calls = 0;
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => FixtureAuthentication.VerifyAsync(credential, _ =>
        {
            calls++;
            return Task.FromResult(new ProcessResult(1, string.Empty, error));
        })).ConfigureAwait(true);
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public async Task PreflightChecksActiveAuthenticationBeforeReadingLiveBudget()
    {
        List<string> commands = [];
        var budget = await FixtureAuthentication.VerifyAsync("test-credential", arguments =>
        {
            commands.Add(string.Join(' ', arguments));
            return Task.FromResult(new ProcessResult(0, commands.Count == 1 ? string.Empty : /*lang=json,strict*/ "{\"limit\":5000,\"remaining\":4500,\"reset\":1234}", string.Empty));
        }).ConfigureAwait(true);
        Assert.Equal(["auth status --active --hostname github.com", "api --hostname github.com rate_limit --jq .resources.core"], commands);
        Assert.Equal(new GitHubBudget(5000, 4500, 1234), budget);
    }

    [SkippableFact]
    public async Task ForwardedCredentialsReachBothRedirectedAndTerminalChildren()
    {
        Skip.If(!RecordedTerminal.Supported, "Credential PTY regression requires Bash on macOS/Linux.");
        using FixtureWorkspace workspace = new();
        FixtureAuthentication credentials = new(_ => "test-only-marker", () => throw new InvalidOperationException("Unexpected login read."));
        await credentials.ApplyAsync(workspace.Environment).ConfigureAwait(true);
        workspace.Environment["FIXTURE_EXPECTED_TOKEN"] = "test-only-marker";
        string[] arguments = ["-c", "test \"$GH_TOKEN\" = \"$FIXTURE_EXPECTED_TOKEN\" && test -z \"$GITHUB_TOKEN\""];
        _ = await workspace.Process.SuccessAsync("/bin/bash", arguments, workspace.Target).ConfigureAwait(true);
        string recording = Path.Combine(FixtureFiles.Reports, "recordings", $"credential-forwarding-{Guid.NewGuid():N}.cast");
        output.WriteLine("asciinema play " + RecordedTerminal.Quote(recording));
        string cast = await RecordedTerminal.RunAsync(workspace, "/bin/bash", arguments, recording, _ => Task.CompletedTask).ConfigureAwait(true);
        Assert.DoesNotContain("test-only-marker", cast, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Trait("Category", "BaselineNetwork")]
    public async Task RealAuthenticationWorksInIsolatedRedirectedAndTerminalProcesses()
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1", "Opt in with AGENTIC_E2E_NETWORK=1 for authentication verification.");
        Skip.If(!RecordedTerminal.Supported, "Credential PTY verification requires Bash on macOS/Linux.");
        var budget = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(true);
        output.WriteLine($"Verified authenticated core budget: {budget.Remaining}/{budget.Limit}, reset {DateTimeOffset.FromUnixTimeSeconds(budget.Reset):O}");
        using FixtureWorkspace workspace = new();
        await workspace.InitializeAsync().ConfigureAwait(true);
        string recording = Path.Combine(FixtureFiles.Reports, "recordings", $"authenticated-gh-{Guid.NewGuid():N}.cast");
        output.WriteLine("asciinema play " + RecordedTerminal.Quote(recording));
        string cast = await RecordedTerminal.RunAsync(workspace, "gh", ["api", "--hostname", "github.com", "rate_limit", "--jq", ".resources.core.limit"], recording, _ => Task.CompletedTask).ConfigureAwait(true);
        Assert.Contains(budget.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture), cast, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Environment["GH_TOKEN"], cast, StringComparison.Ordinal);
    }
}
