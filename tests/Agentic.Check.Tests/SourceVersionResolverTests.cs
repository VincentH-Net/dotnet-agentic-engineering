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
            """
            { "default_branch": "main" }
            """);
        handler.SetJson(
            "https://api.github.com/repos/owner/repo/branches/main",
            """
            { "commit": { "commit": { "committer": { "date": "2026-06-30T09:12:00Z" } } } }
            """);
        using HttpClient httpClient = new(handler, disposeHandler: false);
        GitHubSourceVersionResolver resolver = new(httpClient, new NullReporter());
        DirectiveCacheSettings cacheSettings = new(1800, tempDirectory.CreateDirectory("cache"), []);

        _ = await resolver.ResolveVersionsAsync(["owner/repo"], SourceVersionMode.Stable, cacheSettings, CancellationToken.None);
        _ = await resolver.ResolveVersionsAsync(["owner/repo"], SourceVersionMode.Stable, cacheSettings, CancellationToken.None);

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
