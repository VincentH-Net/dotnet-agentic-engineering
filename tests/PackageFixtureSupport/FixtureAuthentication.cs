using System.Text.Json;

namespace Agentic.PackageFixtures;

sealed record GitHubBudget(int Limit, int Remaining, long Reset);

sealed class FixtureAuthentication
{
    internal static FixtureAuthentication Shared { get; } = new(Environment.GetEnvironmentVariable,
        () => new RealProcess().RunAsync("gh", ["auth", "token", "--hostname", "github.com"], FixtureFiles.Checkout));
    readonly Lazy<Task<string>> token;
    readonly Lazy<Task<GitHubBudget>> verified;

    internal FixtureAuthentication(Func<string, string?> readEnvironment, Func<Task<ProcessResult>> readLogin)
    {
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
    }

    internal Task<GitHubBudget> RequireAsync() => verified.Value;

    async Task<GitHubBudget> VerifyIsolatedAsync()
    {
        using FixtureWorkspace workspace = new();
        await ApplyAsync(workspace.Environment).ConfigureAwait(false);
        return await VerifyAsync(workspace.Environment["GH_TOKEN"], args => workspace.Process.RunAsync("gh", args, workspace.Root)).ConfigureAwait(false);
    }

    internal static async Task<GitHubBudget> VerifyAsync(string credential, Func<IReadOnlyList<string>, Task<ProcessResult>> run)
    {
        const string guidance = "Run gh auth login --hostname github.com or supply a valid GH_TOKEN/GITHUB_TOKEN, then start a new test run.";
        FixtureFiles.Require(credential.Length > 0, "Fixture GitHub authentication is missing. " + guidance);
        var status = await run(["auth", "status", "--active", "--hostname", "github.com"]).ConfigureAwait(false);
        RequireSuccess(status, guidance);
        var budget = await run(["api", "--hostname", "github.com", "rate_limit", "--jq", ".resources.core"]).ConfigureAwait(false);
        RequireSuccess(budget, guidance);
        using var document = JsonDocument.Parse(budget.Output);
        var core = document.RootElement;
        return new(core.GetProperty("limit").GetInt32(), core.GetProperty("remaining").GetInt32(), core.GetProperty("reset").GetInt64());
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
