namespace Agentic.Check.Tests;

public sealed class DirectiveInstallerTests
{
    [Fact]
    public async Task GitHubDirectiveSourceListsDirectivesFromLatestReleaseTag()
    {
        using RecordingHttpMessageHandler handler = new();
        handler.SetJson(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/releases/latest",
            """
            { "tag_name": "v1.2.3", "published_at": "2026-06-29T18:18:00Z" }
            """);
        handler.SetJson(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=v1.2.3",
            """
            [
              {
                "name": "foundation-prompt-log.md",
                "download_url": "https://raw.example.test/v1.2.3/foundation-prompt-log.md",
                "type": "file"
              }
            ]
            """);
        using HttpClient httpClient = new(handler, disposeHandler: false);
        GitHubDirectiveSource source = new(httpClient, NoDirectiveCache(), new NullReporter());

        var files = await source.ListAsync(CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal("foundation-prompt-log.md", file.FileName);
        string expectedVersion = new SourceVersionInfo(
            "VincentH-Net/dotnet-agentic-engineering",
            "v1.2.3",
            new DateTimeOffset(2026, 6, 29, 18, 18, 0, TimeSpan.Zero)).Display;
        Assert.Equal(expectedVersion, file.Version);
        Assert.Contains(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=v1.2.3",
            handler.Requests);
        Assert.DoesNotContain(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering",
            handler.Requests);
    }

    [Fact]
    public async Task GitHubDirectiveSourceFallsBackToDefaultBranchWhenNoLatestReleaseExists()
    {
        using RecordingHttpMessageHandler handler = new();
        handler.SetStatus(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/releases/latest",
            System.Net.HttpStatusCode.NotFound);
        handler.SetJson(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering",
            """
            { "default_branch": "trunk" }
            """);
        handler.SetJson(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=trunk",
            """
            [
              {
                "name": "foundation-prompt-log.md",
                "download_url": "https://raw.example.test/trunk/foundation-prompt-log.md",
                "type": "file"
              }
            ]
            """);
        using HttpClient httpClient = new(handler, disposeHandler: false);
        GitHubDirectiveSource source = new(httpClient, NoDirectiveCache(), new NullReporter());

        var files = await source.ListAsync(CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal("foundation-prompt-log.md", file.FileName);
        Assert.Contains(
            "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=trunk",
            handler.Requests);
    }

    [Fact]
    public void CurrentDirectiveStatusDisplaysAsUpToDate()
        => Assert.Equal("up to date", DirectiveInstaller.FormatDirectiveStatus(DirectiveStatuses.Current));

    [Fact]
    public void OutdatedDirectiveStatusDisplaysAsUpdateAvailable()
        => Assert.Equal("update(s) available", DirectiveInstaller.FormatDirectiveStatus(DirectiveStatuses.Outdated));

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
    public async Task SkipMarkerDoesNotPreventDirectiveBlockChange()
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
        Assert.Contains("Body for foundation-prompt-log.", agents, StringComparison.Ordinal);
        Assert.Contains(result.Directives, directive => directive.Name == "foundation-prompt-log" && directive.Status == DirectiveStatuses.Missing);
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
        Assert.Equal(1, plan.MissingCount);
        Assert.Equal(1, plan.OutdatedCount);
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

    static DirectiveCacheSettings NoDirectiveCache()
        => new(0, Path.Combine(Path.GetTempPath(), $"agentic-check-directives-{Guid.NewGuid():N}"), []);

    sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        readonly Dictionary<string, HttpResponseMessage> responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public void SetJson(string url, string json)
            => responses[url] = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

        public void SetStatus(string url, System.Net.HttpStatusCode statusCode)
            => responses[url] = new HttpResponseMessage(statusCode);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            Requests.Add(url);
            if (!responses.Remove(url, out var response))
            {
                response = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent($"Unexpected request: {url}")
                };
            }

            return Task.FromResult(response);
        }
    }
}
