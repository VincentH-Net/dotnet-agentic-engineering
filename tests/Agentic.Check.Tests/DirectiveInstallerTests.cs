namespace Agentic.Check.Tests;

public sealed class DirectiveInstallerTests
{
    [Fact]
    public async Task CreatesAgentsAndClaudeWithRelevantDirectives()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, false, CancellationToken.None);

        Assert.True(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        string claude = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "CLAUDE.md"), CancellationToken.None);
        Assert.Contains("dotnet-agentic-engineering:foundation-prompt-log:start", agents, StringComparison.Ordinal);
        Assert.Contains("dotnet-agentic-engineering:dotnet-cli-run:start", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-agentic-engineering:uno-build-and-run:start", agents, StringComparison.Ordinal);
        Assert.Contains("@AGENTS.md", claude, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesUserContentAndRefreshesExistingDirectiveBlock()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "AGENTS.md",
            """
            # User notes

            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            stale
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->

            Keep this.
            """);
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, false, CancellationToken.None);

        Assert.True(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        Assert.Contains("# User notes", agents, StringComparison.Ordinal);
        Assert.Contains("Keep this.", agents, StringComparison.Ordinal);
        Assert.Contains("Body for foundation-prompt-log.", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", agents, StringComparison.Ordinal);
        Assert.Contains(result.Directives, directive => directive.Name == "foundation-prompt-log" && directive.Status == DirectiveStatuses.Outdated);
    }

    [Fact]
    public async Task SkipMarkerPreventsDirectiveBlockChange()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:skip -->
            User owns this directive.
            """);
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, false, CancellationToken.None);

        Assert.True(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        Assert.Contains("User owns this directive.", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("Body for foundation-prompt-log.", agents, StringComparison.Ordinal);
        Assert.Contains(result.Directives, directive => directive.Name == "foundation-prompt-log" && directive.Status == "skipped");
    }

    [Fact]
    public async Task UpdatesExistingClaudeImportAndRemovesDuplicates()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("AGENTS.MD", "# Existing agents");
        tempDirectory.Write(
            "CLAUDE.MD",
            """
            # Claude notes
            @AGENTS.md
            @agents.md
            """);
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, false, CancellationToken.None);

        Assert.True(result.Success);
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(tempDirectory.Path, "CLAUDE.MD"), CancellationToken.None);
        _ = Assert.Single(lines, line => line.Equals("@AGENTS.MD", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Equals("@AGENTS.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DryRunReportsActionsWithoutWritingFiles()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(tempDirectory.Path, "CLAUDE.md")));
        Assert.Contains(result.Actions, action => action.Contains("Would create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanReportsDirectiveSummaryCounts()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:skip -->

            <!-- dotnet-agentic-engineering:dotnet-cli-run:start -->
            stale
            <!-- dotnet-agentic-engineering:dotnet-cli-run:end -->
            """);
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var plan = await installer.PlanAsync(tempDirectory.Path, stack, CancellationToken.None);

        Assert.True(plan.Success);
        Assert.Equal(2, plan.RecommendedCount);
        Assert.Equal(0, plan.MissingCount);
        Assert.Equal(1, plan.OutdatedCount);
        Assert.Equal(1, plan.SkippedCount);
    }

    [Fact]
    public async Task ApplyOnlyInstallsSelectedDirectives()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());
        var plan = await installer.PlanAsync(tempDirectory.Path, stack, CancellationToken.None);

        var result = await installer.ApplyAsync(plan, ["foundation-prompt-log"], false, CancellationToken.None);

        Assert.True(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        Assert.Contains("dotnet-agentic-engineering:foundation-prompt-log:start", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-agentic-engineering:dotnet-cli-run:start", agents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InconsistentDirectiveMarkersStopWithoutWriting()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            Broken marker.
            """);
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation
            },
            [],
            []);
        DirectiveInstaller installer = new(new FakeDirectiveSource(), new NullReporter());

        var result = await installer.EnsureAsync(tempDirectory.Path, stack, false, CancellationToken.None);

        Assert.False(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None);
        Assert.Contains("Broken marker.", agents, StringComparison.Ordinal);
    }
}
