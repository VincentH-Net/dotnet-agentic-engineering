using System.Net;
using System.Text.Json;
using Agentic.PackageFixtures;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

[Collection(GitHubNetworkScope.Name)]
public sealed class GitHubAuthenticationLiveTests(ITestOutputHelper output)
{
    static readonly JsonSerializerOptions EvidenceOptions = new() { WriteIndented = true };

    [SkippableFact]
    [Trait("Category", "AuthenticationNetwork")]
    public async Task ProductionCredentialIsAcceptedThroughRealGhAndDirectHttp()
    {
        Skip.If(Environment.GetEnvironmentVariable("AGENTIC_E2E_NETWORK") != "1", "Opt in with AGENTIC_E2E_NETWORK=1 for real GitHub authentication verification.");
        _ = await FixtureAuthentication.Shared.RequireAsync().ConfigureAwait(true);
        using FixtureWorkspace workspace = new();
        await FixtureAuthentication.Shared.ApplyAsync(workspace.Environment).ConfigureAwait(true);
        ConfiguredRunner runner = new(workspace.Environment);
        var (authentication, error) = await GitHubAuthentication.ConnectAsync(runner, workspace.Target,
            CancellationToken.None, name => workspace.Environment.GetValueOrDefault(name)).ConfigureAwait(true);
        Assert.True(authentication is not null, error);
        using var client = authentication.CreateHttpClient();
        using var response = await client.GetAsync(new Uri("https://api.github.com/" + GitHubAuthentication.ProbePath)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        int limit = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(limit > 60, "Direct HTTP received an anonymous-sized API budget.");
        string directory = Path.Combine(FixtureFiles.Checkout, "tests/Agentic.Check.LiveTests/TestResults/authentication");
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"direct-http-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            sampledAtUtc = DateTimeOffset.UtcNow,
            realGhPrerequisitePassed = true,
            directHttpStatus = (int)response.StatusCode,
            limit,
            remaining = response.Headers.GetValues("X-RateLimit-Remaining").Single(),
            reset = response.Headers.GetValues("X-RateLimit-Reset").Single(),
            requestId = response.Headers.GetValues("X-GitHub-Request-Id").Single()
        }, EvidenceOptions)).ConfigureAwait(true);
        output.WriteLine("Sanitized authentication evidence: " + path);
    }

    sealed class ConfiguredRunner(IReadOnlyDictionary<string, string> configured) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory,
            CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
        {
            var combined = configured.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                    combined[name] = value;
            }
            return new ProcessCommandRunner().RunAsync(fileName, arguments, workingDirectory, cancellationToken, combined);
        }
    }
}
