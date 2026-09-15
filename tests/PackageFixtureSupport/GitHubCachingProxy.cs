using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Agentic.PackageFixtures;

// A test-only HTTP gateway for gh's http_unix_socket transport. Upstream TLS is normal HTTPS.
sealed class GitHubCachingProxy : IAsyncDisposable
{
    internal const string BypassHeader = "X-Agentic-Test-Cache";
    static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Host", "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length", BypassHeader };
    readonly HttpClient upstream;
    readonly HttpMessageHandler upstreamHandler;
    readonly SemaphoreSlim mutex = new(1);
    readonly Dictionary<string, StoredResponse> cache = new(StringComparer.Ordinal);
    readonly Dictionary<string, SourceObservation> sources = new(StringComparer.Ordinal);
    readonly HashSet<string> moved = new(StringComparer.Ordinal);
    readonly string socketDirectory;
    WebApplication? application;
    int failures;
    bool completed;

    internal string SocketPath { get; }
    internal string Reports { get; }
    internal int UpstreamCount { get; private set; }
    internal int CacheHits { get; private set; }

    static readonly string[] RepresentationHeaders = ["Authorization", "Accept", "Accept-Encoding", "X-GitHub-Api-Version", "Content-Type"];

    internal GitHubCachingProxy(string reports, HttpMessageHandler? handler = null)
    {
        Reports = Directory.CreateDirectory(reports).FullName;
        // Keep the socket path below the macOS sockaddr_un length limit.
        socketDirectory = Directory.CreateTempSubdirectory("ag-gh-").FullName;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(socketDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SocketPath = Path.Combine(socketDirectory, "gh.sock");
        upstreamHandler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        upstream = new(upstreamHandler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
    }

    internal async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = Reports });
        _ = builder.Logging.ClearProviders();
        _ = builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(SocketPath, endpoint => endpoint.Protocols = HttpProtocols.Http1));
        application = builder.Build();
        application.Run(HandleAsync);
        await application.StartAsync().ConfigureAwait(false);
    }

    async Task HandleAsync(HttpContext context)
    {
        // Never send gh's credential to a destination supplied by a client.
        if (context.Request.Host.Host != "api.github.com")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        Dictionary<string, string[]> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (var header in context.Request.Headers)
        {
            if (!HopHeaders.Contains(header.Key))
                headers[header.Key] = [.. header.Value.Select(value => value ?? string.Empty)];
        }
        using MemoryStream body = new();
        await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
        string path = context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
        bool bypass = context.Request.Headers[BypassHeader] == "bypass" || context.Request.Path == "/rate_limit";
        try
        {
            var response = await SendAsync(context.Request.Method, path, headers, body.ToArray(), bypass, "client", context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = response.Status;
            foreach (var header in response.Headers)
            {
                if (!HopHeaders.Contains(header.Key))
                    context.Response.Headers[header.Key] = header.Value;
            }
            context.Response.ContentLength = response.Body.Length;
            await context.Response.Body.WriteAsync(response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            // Exception text/HTTP bodies can contain credentials or account details.
            await context.Response.WriteAsync("Test GitHub gateway upstream request failed; see sanitized run diagnostics.").ConfigureAwait(false);
        }
    }

    internal async Task<StoredResponse> SendAsync(string method, string path, Dictionary<string, string[]> headers, byte[] body,
        bool bypass = false, string phase = "client", CancellationToken cancellationToken = default)
    {
        bypass |= path.Split('?')[0] == "/rate_limit";
        FixtureFiles.Require(path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal), "Invalid GitHub API path.");
        await mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string key = Key(method, path, headers, body);
            bool cacheable = method is "GET" or "HEAD";
            if (!bypass && cacheable && cache.TryGetValue(key, out var existing))
            {
                CacheHits++;
                await RecordAsync(method, path, headers, existing, true, phase).ConfigureAwait(false);
                return existing;
            }
            var response = await FetchAsync(method, path, headers, body, bypass || phase == "start-validation", cancellationToken).ConfigureAwait(false);
            if (cacheable && response.Status is (>= 200 and < 300) or 404)
            {
                // Expected missing refs are stable within the run; auth/quota/transient errors are never cached.
                if (!bypass)
                    cache[key] = response;
                string? identity = method == "GET" ? SourceIdentity(path, response) : null;
                if (identity is not null && phase != "end-validation")
                {
                    string sourceKey = Key("GET", path, new(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = headers.GetValueOrDefault("Authorization") ?? [] }, []);
                    if (sources.TryGetValue(sourceKey, out var previous))
                    {
                        if (previous.Identity != identity)
                            _ = moved.Add(path);
                    }
                    else
                    {
                        sources.Add(sourceKey, new(path, headers, identity));
                    }
                }
            }
            await RecordAsync(method, path, headers, response, false, phase).ConfigureAwait(false);
            return response;
        }
        finally
        {
            _ = mutex.Release();
        }
    }

    async Task<StoredResponse> FetchAsync(string method, string path, Dictionary<string, string[]> headers, byte[] body, bool fresh, CancellationToken cancellationToken)
    {
        string upstreamPath = fresh ? path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "agentic_fixture_probe=" + Guid.NewGuid().ToString("N") : path;
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri("https://api.github.com" + upstreamPath));
        if (body.Length > 0)
            request.Content = new ByteArrayContent(body);
        foreach (var header in headers)
        {
            if (!HopHeaders.Contains(header.Key) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                _ = request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (fresh)
            request.Headers.CacheControl = new() { NoCache = true };
        UpstreamCount++;
        try
        {
            using var response = await upstream.SendAsync(request, cancellationToken).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var responseHeaders = response.Headers.Concat(response.Content.Headers)
                .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            return new((int)response.StatusCode, responseHeaders, bytes, upstreamPath);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            failures++;
            await File.AppendAllTextAsync(Path.Combine(Reports, "requests.jsonl"), JsonSerializer.Serialize(new
            {
                atUtc = DateTimeOffset.UtcNow,
                method,
                path,
                cacheHit = false,
                transportError = exception.GetType().Name,
                authorizationPresent = headers.ContainsKey("Authorization")
            }) + "\n", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    Task RecordAsync(string method, string path, Dictionary<string, string[]> headers, StoredResponse response, bool cacheHit, string phase)
        => File.AppendAllTextAsync(Path.Combine(Reports, "requests.jsonl"), JsonSerializer.Serialize(new
        {
            atUtc = DateTimeOffset.UtcNow,
            method,
            path,
            phase,
            upstreamPath = response.UpstreamPath,
            cacheHit,
            status = response.Status,
            authorizationPresent = headers.ContainsKey("Authorization"),
            authenticatedBudget = headers.ContainsKey("Authorization") && response.Status != 401
                && int.TryParse(response.Headers.GetValueOrDefault("X-RateLimit-Limit")?.FirstOrDefault(), out int limit) && limit > 60,
            bodySha256 = Convert.ToHexStringLower(SHA256.HashData(response.Body)),
            // These headers are historical on cache hits, never a live budget observation.
            quota = response.Headers.Where(header => header.Key.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-GitHub-Request-Id", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)).ToDictionary(header => header.Key, header => string.Join(',', header.Value))
        }) + "\n");

    static string Key(string method, string path, Dictionary<string, string[]> headers, byte[] body)
    {
        string representations = string.Join('\n', RepresentationHeaders.Select(name => name + ":" + string.Join(',', headers.GetValueOrDefault(name) ?? [])));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(method + "\n" + path + "\n" + representations + "\n" + Convert.ToHexStringLower(SHA256.HashData(body)))));
    }

    internal static string? SourceIdentity(string path, StoredResponse response)
    {
        string[] parts = Uri.UnescapeDataString(path.Split('?')[0]).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "repos")
            return null;
        bool repository = parts.Length == 3;
        bool reference = parts.Length >= 5 && parts[3] is "branches" or "commits" && !IsCommit(parts[4]);
        bool gitReference = parts.Length >= 6 && parts[3] == "git" && parts[4] is "ref" or "refs" && !IsCommit(parts[^1]);
        bool releases = parts.Length >= 4 && parts[3] == "releases" && (parts.Length == 4 || parts.Length == 5 && parts[4] == "latest" || parts.Length == 6 && parts[4] == "tags");
        if (!repository && !reference && !gitReference && !releases)
            return null;
        if (response.Status == 404)
            return "404";
        // gh normally requests JSON without compression; decode other representations before validation.
        using var document = JsonDocument.Parse(response.JsonBody());
        var json = document.RootElement;
        if (repository)
            return json.GetProperty("default_branch").GetString();
        return reference
            ? (parts[3] == "branches" ? json.GetProperty("commit") : json).GetProperty("sha").GetString()
            : gitReference
            ? json.GetProperty("object").GetProperty("sha").GetString()
            : json.ValueKind == JsonValueKind.Array
            ? string.Join('\n', json.EnumerateArray().Where(release => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean())
                .Select(release => release.GetProperty("id").GetRawText() + ":" + release.GetProperty("tag_name").GetString()))
            : json.GetProperty("id").GetRawText() + ":" + json.GetProperty("tag_name").GetString();
    }

    static bool IsCommit(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    internal async Task CompleteAsync()
    {
        if (completed)
            return;
        completed = true;
        List<string> changed = [.. moved];
        List<string> unavailable = [];
        List<SourceCheck> checks = [];
        foreach (var source in sources.Values.ToArray())
        {
            try
            {
                var current = await SendAsync("GET", source.Path, source.Headers, [], true, "end-validation").ConfigureAwait(false);
                string? identity = current.Status is 200 or 404 ? SourceIdentity(source.Path, current) : null;
                checks.Add(new(source.Path, source.Identity, identity, current.Status));
                if (current.Status is not (200 or 404))
                    unavailable.Add(source.Path);
                else if (identity != source.Identity)
                    changed.Add(source.Path);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
            {
                unavailable.Add(source.Path);
            }
        }
        string outcome = changed.Count > 0 ? "inconclusive-source-movement" : unavailable.Count > 0 || failures > 0 ? "incomplete-network-verification" : "validated";
        FixtureFiles.WriteJson(Path.Combine(Reports, "summary.json"), new
        {
            outcome,
            upstreamCount = UpstreamCount,
            cacheHits = CacheHits,
            cacheEntries = cache.Count,
            transportFailures = failures,
            sourceChecks = sources.Count,
            checks,
            changed,
            unavailable
        });
        FixtureFiles.Require(changed.Count == 0 && unavailable.Count == 0 && failures == 0,
            $"SOURCE VALIDATION: {outcome}; transient infrastructure issue, rerun required. See {Reports}/summary.json. Test assertions and recordings remain separate.");
    }

    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.StopAsync().ConfigureAwait(false);
            await application.DisposeAsync().ConfigureAwait(false);
        }
        upstream.Dispose();
        upstreamHandler.Dispose();
        mutex.Dispose();
        Directory.Delete(socketDirectory, true);
    }

    sealed record SourceCheck(string Path, string Expected, string? Actual, int Status);
    sealed record SourceObservation(string Path, Dictionary<string, string[]> Headers, string Identity);
}

sealed record StoredResponse(int Status, Dictionary<string, string[]> Headers, byte[] Body, string? UpstreamPath = null)
{
    internal byte[] JsonBody()
    {
        if (!Headers.TryGetValue("Content-Encoding", out string[]? encodings))
            return Body;
        using MemoryStream input = new(Body);
        using Stream decoder = string.Join(',', encodings) switch
        {
            "gzip" => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress),
            "br" => new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress),
            _ => throw new InvalidDataException("Unsupported source response encoding.")
        };
        using MemoryStream output = new();
        decoder.CopyTo(output);
        return output.ToArray();
    }
}
