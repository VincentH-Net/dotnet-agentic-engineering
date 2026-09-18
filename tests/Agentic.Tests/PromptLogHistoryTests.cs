using System.Globalization;

namespace Agentic.Tests;

public sealed class PromptLogHistoryTests
{
    [Theory]
    [InlineData("", 20, true)]
    [InlineData("show", 20, true)]
    [InlineData("--limit 1", 1, true)]
    [InlineData("show --limit 1", 1, true)]
    [InlineData("--limit 1 show", 1, true)]
    [InlineData("--limit 25", 25, false)]
    [InlineData("show --limit 50", 25, false)]
    [InlineData("--limit 2147483647", 25, false)]
    [InlineData("--all", 25, false)]
    [InlineData("show --all", 25, false)]
    [InlineData("--all show", 25, false)]
    public async Task ShowsNewestPromptLogsChronologicallyWithOneGitInvocation(string options, int count, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(options);
        using Workspace workspace = new();
        _ = await workspace.GitAsync("init").ConfigureAwait(true);
        for (int index = 1; index <= 25; index++)
        {
            await CommitAsync(workspace, "subject\n\n" + PromptBlock.Format(Entry(index))).ConfigureAwait(true);
            await CommitAsync(workspace, "ordinary: mention prompt-log: inline\n\n prompt-log:\nprompt-log: extra text").ConfigureAwait(true);
        }

        // --all removes the display limit; it must not include unreachable branches.
        _ = await workspace.GitAsync("checkout", "-b", "other").ConfigureAwait(true);
        await CommitAsync(workspace, PromptBlock.Format("unreachable log")).ConfigureAwait(true);
        _ = await workspace.GitAsync("checkout", "--detach", "HEAD~1").ConfigureAwait(true);
        _ = await workspace.GitAsync("config", "log.showSignature", "true").ConfigureAwait(true);
        _ = await workspace.GitAsync("config", "format.pretty", "oneline").ConfigureAwait(true);

        CountingGit git = new();
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["-m", "2.3", "prompt-log", .. options.Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            output: output, error: error, git: git, directory: workspace.Path).ConfigureAwait(true));
        string text = output.ToString();
        Assert.Equal(count, text.Split('\n').Count(line => line.Contains(": valid prompt log", StringComparison.Ordinal)));
        int previous = -1;
        for (int index = 1; index <= 25; index++)
        {
            int position = text.IndexOf(Entry(index), StringComparison.Ordinal);
            if (index <= 25 - count)
            {
                Assert.Equal(-1, position);
            }
            else
            {
                Assert.True(position > previous, text);
                previous = position;
            }
        }

        Assert.DoesNotContain("ordinary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable", text, StringComparison.Ordinal);
        Assert.Equal(truncated ? $"Showing the latest {count} prompt logs; older entries omitted. Use --limit N or --all to show more.{Environment.NewLine}" : string.Empty, error.ToString());
        var call = Assert.Single(git.Calls);
        Assert.Equal("log", call[0]);
        Assert.DoesNotContain("--all", call);
        if (options.Length == 0 || options == "show")
            Assert.Contains("--max-count=21", call);

        static string Entry(int index) => string.Create(CultureInfo.InvariantCulture, $"entry-{index:D2}");
    }

    [Theory]
    [InlineData("--limit 0")]
    [InlineData("show --limit -1")]
    [InlineData("--limit nope")]
    [InlineData("--limit 2147483648")]
    [InlineData("--limit")]
    [InlineData("--limit 2 --all")]
    [InlineData("show --all --limit 20")]
    [InlineData("--all=invalid")]
    [InlineData("unknown")]
    [InlineData("--unknown")]
    public async Task InvalidOptionsDoNotReadHistory(string options)
    {
        ArgumentNullException.ThrowIfNull(options);
        FakeGit git = new();
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(2, await AgenticCli.InvokeAsync(["prompt-log", .. options.Split(' ')],
            git: git, error: error, output: TextWriter.Null).ConfigureAwait(true));
        Assert.NotEmpty(error.ToString());
        Assert.Empty(git.Calls);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("show --help")]
    public async Task HelpDoesNotReadHistoryOrEnforceVersion(string options)
    {
        ArgumentNullException.ThrowIfNull(options);
        FakeGit git = new();
        using StringWriter output = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", .. options.Split(' '), "-m", "99.0"],
            git: git, output: output).ConfigureAwait(true));
        Assert.Contains("--limit", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--all", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public async Task DefaultShowRetainsVersionGuard()
    {
        FakeGit git = new();
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "-m", "99.0"], git: git,
            output: TextWriter.Null, error: TextWriter.Null).ConfigureAwait(true));
        Assert.Empty(git.Calls);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task FilterRetainsLegacyAndMalformedLogsAndArbitraryBodyText(string newline)
    {
        using Workspace workspace = new();
        _ = await workspace.GitAsync("init").ConfigureAwait(true);
        await CommitAsync(workspace, "prompt-log:\n\n1. \"legacy\"").ConfigureAwait(true);
        const string body = "Unicode 漢字 😀\nrecord separators \u001e \u001f\nprompt-log-end:\n\\literal";
        await CommitAsync(workspace, ("subject\n\n" + PromptBlock.Format(body) + "\nReviewed-by: Fixture\n").Replace("\n", newline, StringComparison.Ordinal)).ConfigureAwait(true);
        await CommitAsync(workspace, "broken\n\nprompt-log-end:\n").ConfigureAwait(true);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "--all"], directory: workspace.Path, output: output, error: error).ConfigureAwait(true));
        Assert.Contains("legacy prompt log", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(body, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Reviewed-by", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("malformed prompt log", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("older entries omitted", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitAndDatesDoNotValidateExcludedLogs()
    {
        using Workspace workspace = new();
        _ = await workspace.GitAsync("init").ConfigureAwait(true);
        await CommitAsync(workspace, "broken\n\nprompt-log:\n").ConfigureAwait(true);
        await CommitAsync(workspace, PromptBlock.Format("newest")).ConfigureAwait(true);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "--limit", "1"], directory: workspace.Path, output: output, error: error).ConfigureAwait(true));
        Assert.Contains("newest", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("older entries omitted", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("malformed", error.ToString(), StringComparison.Ordinal);
        _ = output.GetStringBuilder().Clear();
        _ = error.GetStringBuilder().Clear();
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "--all", "--since", "1999-01-01", "--until", "2000-01-01"],
            directory: workspace.Path, output: output, error: error).ConfigureAwait(true));
        Assert.Empty(output.ToString());
        Assert.Empty(error.ToString());
    }

    static async Task CommitAsync(Workspace workspace, string message)
        => _ = await workspace.GitAsync("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "--allow-empty", "--cleanup=verbatim", "-m", message).ConfigureAwait(false);

    sealed class CountingGit : IGitCommandRunner
    {
        internal List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return new GitCommandRunner().RunAsync(arguments, directory, cancellationToken);
        }
    }
}
