namespace Agentic.PackageFixtures;

sealed record GitHubBudget(int Limit, int Remaining, long Reset);

sealed class FixtureAuthentication
{
    internal static FixtureAuthentication Shared { get; } = new(Environment.GetEnvironmentVariable,
        () => new RealProcess().RunAsync("gh", ["auth", "token", "--hostname", "github.com"], FixtureFiles.Checkout), useProxy: true);
    readonly Lazy<Task<string>> token;
    readonly Lazy<Task<GitHubBudget>> verified;
    readonly bool useProxy;

    internal FixtureAuthentication(Func<string, string?> readEnvironment, Func<Task<ProcessResult>> readLogin, bool useProxy = false)
    {
        this.useProxy = useProxy;
        token = new(() => ResolveAsync(readEnvironment, readLogin));
        verified = new(VerifyIsolatedAsync);
    }

    static async Task<string> ResolveAsync(Func<string, string?> readEnvironment, Func<Task<ProcessResult>> readLogin)
    {
        foreach (string name in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
        {
            if (readEnvironment(name) is { Length: > 0 } value)
                return value;
        }

        var login = await readLogin().ConfigureAwait(false);
        return login.ExitCode == 0 ? login.Output.Trim() : string.Empty;
    }

    internal async Task ApplyAsync(Dictionary<string, string> environment)
    {
        // Explicit values make both redirected and PTY children independent of ambient token inheritance.
        environment["GH_TOKEN"] = await token.Value.ConfigureAwait(false);
        environment["GITHUB_TOKEN"] = string.Empty;
        if (useProxy)
            await GitHubFixtureRun.Shared.ConfigureAsync(environment).ConfigureAwait(false);
    }

    internal Task<GitHubBudget> RequireAsync() => verified.Value;

    async Task<GitHubBudget> VerifyIsolatedAsync()
    {
        using FixtureWorkspace workspace = new();
        await ApplyAsync(workspace.Environment).ConfigureAwait(false);
        var gateway = useProxy ? await GitHubFixtureRun.Shared.ProxyAsync.ConfigureAwait(false) : null;
        int requestsBefore = gateway?.UpstreamCount ?? 0;
        var budget = await VerifyAsync(workspace.Environment["GH_TOKEN"], args => workspace.Process.RunAsync("gh", args, workspace.Root)).ConfigureAwait(false);
        if (gateway is not null)
        {
            FixtureFiles.Require(gateway.UpstreamCount > requestsBefore, "gh authentication preflight bypassed the test gateway. Verify gh http_unix_socket support before running fixtures.");
            await GitHubFixtureRun.Shared.PrimeAsync(workspace.Environment["GH_TOKEN"]).ConfigureAwait(false);
        }
        return budget;
    }

    internal static async Task<GitHubBudget> VerifyAsync(string credential, Func<IReadOnlyList<string>, Task<ProcessResult>> run)
    {
        const string guidance = "Run gh auth login --hostname github.com or supply a valid GH_TOKEN/GITHUB_TOKEN, then start a new test run.";
        FixtureFiles.Require(credential.Length > 0, "Fixture GitHub authentication is missing. " + guidance);
        var status = await run(["auth", "status", "--active", "--hostname", "github.com"]).ConfigureAwait(false);
        RequireSuccess(status, guidance);
        var budget = await run(["api", "--hostname", "github.com", "--include", "--header", GitHubCachingProxy.BypassHeader + ": bypass", "repos/" + SourceOracle.OwnRepository]).ConfigureAwait(false);
        RequireSuccess(budget, guidance);
        var measured = ReadBudget(budget.Output);
        FixtureFiles.Require(measured.Limit > 60, "GitHub returned an anonymous-sized budget despite supplied credentials. " + guidance);
        return measured;
    }

    internal static GitHubBudget ReadBudget(string includedResponse)
    {
        var headers = includedResponse.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n")[0].Split('\n')
            .Select(line => line.TrimEnd('\r').Split(':', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return new(int.Parse(headers["X-RateLimit-Limit"], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(headers["X-RateLimit-Remaining"], System.Globalization.CultureInfo.InvariantCulture),
            long.Parse(headers["X-RateLimit-Reset"], System.Globalization.CultureInfo.InvariantCulture));
    }

    static void RequireSuccess(ProcessResult result, string guidance)
    {
        // Never include authentication command output: it may contain account information or credentials.
        bool connectionFailure = (result.Error + result.Output).Contains("error connecting", StringComparison.OrdinalIgnoreCase)
            || (result.Error + result.Output).Contains("timeout", StringComparison.OrdinalIgnoreCase);
        FixtureFiles.Require(result.ExitCode == 0, connectionFailure
            ? "Fixture GitHub authentication preflight could not reach GitHub. Check connectivity and retry."
            : "Fixture GitHub authentication/access preflight failed. " + guidance);
    }
}
