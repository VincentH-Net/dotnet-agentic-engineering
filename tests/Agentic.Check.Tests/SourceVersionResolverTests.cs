namespace Agentic.Check.Tests;

public sealed class SourceVersionResolverTests
{
    [Fact]
    public async Task StableNoReleaseResultIsCachedUntilCacheExpires()
    {
        using TempDirectory tempDirectory = new();
        using RecordingHttpMessageHandler handler = new();
        handler.SetStatus(
            "https://api.github.com/repos/owner/repo/releases/latest",
            System.Net.HttpStatusCode.NotFound);
        handler.SetJson(
            "https://api.github.com/repos/owner/repo",
            /*lang=json,strict*/
                                 """
            { "default_branch": "main" }
            """);
        handler.SetJson(
            "https://api.github.com/repos/owner/repo/branches/main",
            /*lang=json,strict*/
                                 """
            { "commit": { "commit": { "committer": { "date": "2026-06-30T09:12:00Z" } } } }
            """);
        using HttpClient httpClient = new(handler, disposeHandler: false);
        GitHubSourceVersionResolver resolver = new(httpClient, new NullReporter());
        DirectiveCacheSettings cacheSettings = new(1800, tempDirectory.CreateDirectory("cache"), []);

        var first = await resolver.ResolveVersionsAsync(["owner/repo"], SourceVersionMode.Stable, cacheSettings, CancellationToken.None);
        var cached = await resolver.ResolveVersionsAsync(["owner/repo"], SourceVersionMode.Stable, cacheSettings, CancellationToken.None);

        Assert.True(first["owner/repo"].IsDefaultBranch);
        Assert.Equal("main", first["owner/repo"].Ref);
        Assert.Equal(first["owner/repo"], cached["owner/repo"]);

        Assert.Equal(
            1,
            handler.Requests.Count(request => request == "https://api.github.com/repos/owner/repo/releases/latest"));
        Assert.Equal(
            1,
            handler.Requests.Count(request => request == "https://api.github.com/repos/owner/repo"));
        Assert.Equal(
            1,
            handler.Requests.Count(request => request == "https://api.github.com/repos/owner/repo/branches/main"));
    }

    [Fact]
    public async Task StableReleaseNamedMainIsNotClassifiedAsDefaultBranch()
    {
        using TempDirectory temp = new();
        using RecordingHttpMessageHandler handler = new();
        handler.SetJson("https://api.github.com/repos/owner/repo/releases/latest",
            /*lang=json,strict*/ """{ "tag_name": "main", "published_at": "2026-09-01T00:00:00Z" }""");
        using HttpClient client = new(handler);
        GitHubSourceVersionResolver resolver = new(client);

        var versions = await resolver.ResolveVersionsAsync(["owner/repo"], SourceVersionMode.Stable,
            new(0, temp.CreateDirectory("cache"), []), CancellationToken.None);

        Assert.False(versions["owner/repo"].IsDefaultBranch);
        Assert.Equal("main", versions["owner/repo"].Ref);
        _ = Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("feature/package-fixtures", true)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("../main", false)]
    [InlineData("main?ref=other", false)]
    [InlineData("refs//heads/main", false)]
    [InlineData("feature/.hidden", false)]
    [InlineData("feature/branch.lock", false)]
    public void ExplicitRefValidation(string reference, bool expected)
        => Assert.Equal(expected, GitHubSourceVersionResolver.IsValidPreviewRef(reference));

    [Fact]
    public async Task ExplicitPreviewResolvesOnceAndLeavesExternalSelectionAlone()
    {
        using TempDirectory temp = new();
        using RecordingHttpMessageHandler handler = new();
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        const string url = "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/branches/feature%2Ffixtures";
        handler.SetJson(url, $$"""{ "commit": { "sha": "{{sha}}", "commit": { "committer": { "date": "2026-09-14T12:00:00Z" } } } }""");
        handler.SetJson("https://api.github.com/repos/external/skills", /*lang=json,strict*/ """{ "default_branch": "main" }""");
        handler.SetJson("https://api.github.com/repos/external/skills/branches/main", /*lang=json,strict*/ """{ "commit": { "sha": "external-sha", "commit": { "committer": { "date": "2026-09-13T12:00:00Z" } } } }""");
        using HttpClient client = new(handler);
        GitHubSourceVersionResolver resolver = new(client, previewSourceRef: "feature/fixtures");
        var versions = await resolver.ResolveVersionsAsync([CompanionDependency.SourceRepo, "external/skills", CompanionDependency.SourceRepo], SourceVersionMode.Preview, new(0, temp.CreateDirectory("cache"), []), CancellationToken.None);
        Assert.Equal(sha, versions[CompanionDependency.SourceRepo].ContentRef);
        Assert.Equal("feature/fixtures", versions[CompanionDependency.SourceRepo].Ref);
        Assert.Equal("external-sha", versions["external/skills"].ContentRef);
        _ = Assert.Single(handler.Requests, request => request == url);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("releases", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task ExplicitUnavailableRefNeverFallsBack(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        using TempDirectory temp = new();
        using RecordingHttpMessageHandler handler = new();
        handler.SetStatus($"https://api.github.com/repos/{CompanionDependency.SourceRepo}/{(reference.Length == 40 ? "commits" : "branches")}/{reference}", System.Net.HttpStatusCode.NotFound);
        using HttpClient client = new(handler);
        GitHubSourceVersionResolver resolver = new(client, previewSourceRef: reference);
        var exception = await Assert.ThrowsAsync<DirectiveException>(() => resolver.ResolveVersionsAsync([CompanionDependency.SourceRepo], SourceVersionMode.Preview, new(0, temp.CreateDirectory("cache"), []), CancellationToken.None));
        Assert.Contains("--preview-source-ref", exception.Message, StringComparison.Ordinal);
        _ = Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StableOverrideFailsBeforeProcessesOrWrites()
    {
        using TempDirectory temp = new();
        FakeCommandRunner runner = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), new NullReporter());
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false, PreviewSourceRef: "feature/fixtures"), CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(temp.Path));
        Assert.Null(AgenticCheckCli.FindUnknownOption(["--preview", "--preview-source-ref", "feature/fixtures"]));
        Assert.Null(AgenticCheckCli.FindUnknownOption(["--preview-source-ref=feature/fixtures"]));
    }

    [Fact]
    public async Task EnvironmentPinsPreviewContentAndShowsItsOrigin()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        using TempDirectory temp = new();
        _ = Directory.CreateDirectory(temp.Path);
        RecordingReporter reporter = new();
        FakeSourceVersionResolver resolver = new();
        CheckWorkflow workflow = new(new ToolRunner(), new FakePrompts(), reporter, new FakeDirectiveSource(), resolver,
            readEnvironment: name => name == CheckWorkflow.PreviewSourceRefVariable ? sha : null);
        var result = await workflow.RunAsync(new(temp.Path, true, false, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SourceVersionMode.Preview, resolver.LastMode);
        Assert.Equal(sha, result.Report.PreviewSourceRef);
        Assert.Equal(CheckWorkflow.PreviewSourceRefVariable, result.Report.PreviewSourceRefOrigin);
        Assert.Equal($"{sha} ({CheckWorkflow.PreviewSourceRefVariable})", reporter.SourcePin);
    }

    [Fact]
    public async Task ExplicitOptionOutranksEnvironmentAndIsShownAsSuch()
    {
        using TempDirectory temp = new();
        _ = Directory.CreateDirectory(temp.Path);
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(new ToolRunner(), new FakePrompts(), reporter, new FakeDirectiveSource(), new FakeSourceVersionResolver(),
            readEnvironment: name => name == CheckWorkflow.PreviewSourceRefVariable ? "other/branch" : null);
        var result = await workflow.RunAsync(new(temp.Path, true, false, null, null, "codex", false, true, "feature/fixtures"), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("feature/fixtures", result.Report.PreviewSourceRef);
        Assert.Equal("--preview-source-ref", result.Report.PreviewSourceRefOrigin);
        Assert.Equal("feature/fixtures (--preview-source-ref)", reporter.SourcePin);
    }

    [Fact]
    public async Task InvalidEnvironmentPinFailsBeforeProcessesOrWrites()
    {
        using TempDirectory temp = new();
        FakeCommandRunner runner = new();
        RecordingReporter reporter = new();
        CheckWorkflow workflow = new(runner, new FakePrompts(), reporter, readEnvironment: name => name == CheckWorkflow.PreviewSourceRefVariable ? "bad..ref" : null);
        var result = await workflow.RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(temp.Path));
        Assert.Contains(CheckWorkflow.PreviewSourceRefVariable, Assert.Single(reporter.Errors), StringComparison.Ordinal);
        Assert.Null(result.Report.PreviewSourceRef);
    }

    [Fact]
    public async Task ExplicitShaIsSharedByDirectiveListingContentAndPrerequisiteReader()
    {
        using TempDirectory temp = new();
        using RecordingHttpMessageHandler handler = new();
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        string commitUrl = $"https://api.github.com/repos/{CompanionDependency.SourceRepo}/commits/{sha}";
        handler.SetJson(commitUrl, $$"""{ "sha": "{{sha}}", "commit": { "committer": { "date": "2026-09-14T12:00:00Z" } } }""");
        string listing = DirectiveInstallerUrl.Listing(sha);
        string directive = $"https://raw.githubusercontent.com/{CompanionDependency.SourceRepo}/{sha}/directives/foundation-prompt-log.md";
        string project = $"https://raw.githubusercontent.com/{CompanionDependency.SourceRepo}/{sha}/src/Agentic/Agentic.csproj";
        handler.SetJson(listing, $$"""[{"name":"foundation-prompt-log.md","type":"file","download_url":"{{directive}}"}]""");
        handler.SetJson(directive, "fixture directive");
        handler.SetJson(project, "<Project><PropertyGroup><Version>2.3.0</Version></PropertyGroup></Project>");
        using HttpClient client = new(handler);
        DirectiveCacheSettings cache = new(0, temp.CreateDirectory("cache"), []);
        GitHubSourceVersionResolver resolver = new(client, previewSourceRef: sha);
        var versions = await resolver.ResolveVersionsAsync([CompanionDependency.SourceRepo], SourceVersionMode.Preview, cache, CancellationToken.None);
        var selected = versions[CompanionDependency.SourceRepo];
        GitHubDirectiveSource source = new(client, cache, sourceVersionMode: SourceVersionMode.Preview, resolvedVersion: selected);
        var files = await source.ListAsync(CancellationToken.None);
        var file = Assert.Single(files);
        Assert.Equal(sha, file.SourceRef);
        Assert.Equal("fixture directive", await source.FetchAsync(file, CancellationToken.None));
        CompanionSourceVersionReader reader = new(source);
        Assert.Equal("2.3", (await reader.ReadAsync(file.SourceRef, CancellationToken.None)).Minimum);
        Assert.Equal(new[] { commitUrl, listing, directive, project }, handler.Requests);
    }

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("{\"sha\":\"bad\"}")]
    [InlineData("{\"sha\":\"ffffffffffffffffffffffffffffffffffffffff\",\"commit\":{\"committer\":{\"date\":\"2026-09-14T12:00:00Z\"}}}")]
    public async Task InvalidExplicitCommitMetadataNeverFallsBack(string response)
    {
        using TempDirectory temp = new();
        using RecordingHttpMessageHandler handler = new();
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        handler.SetJson($"https://api.github.com/repos/{CompanionDependency.SourceRepo}/commits/{sha}", response);
        using HttpClient client = new(handler);
        GitHubSourceVersionResolver resolver = new(client, previewSourceRef: sha);
        _ = await Assert.ThrowsAsync<DirectiveException>(() => resolver.ResolveVersionsAsync([CompanionDependency.SourceRepo], SourceVersionMode.Preview, new(0, temp.CreateDirectory("cache"), []), CancellationToken.None));
        _ = Assert.Single(handler.Requests);
    }

    sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        readonly Dictionary<string, Queue<ResponseSpec>> responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public void SetJson(string url, string json)
            => Enqueue(url, new ResponseSpec(System.Net.HttpStatusCode.OK, json));

        public void SetStatus(string url, System.Net.HttpStatusCode statusCode)
            => Enqueue(url, new ResponseSpec(statusCode, string.Empty));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            Requests.Add(url);
            if (!responses.TryGetValue(url, out var queue) || queue.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent($"Unexpected request: {url}")
                });
            }

            var spec = queue.Peek();
            HttpResponseMessage response = new(spec.StatusCode);
            if (!string.IsNullOrWhiteSpace(spec.Json))
            {
                response.Content = new StringContent(spec.Json, System.Text.Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }

        void Enqueue(string url, ResponseSpec response)
        {
            if (!responses.TryGetValue(url, out var queue))
            {
                queue = new Queue<ResponseSpec>();
                responses[url] = queue;
            }

            queue.Enqueue(response);
        }

        sealed record ResponseSpec(System.Net.HttpStatusCode StatusCode, string Json);
    }
}
