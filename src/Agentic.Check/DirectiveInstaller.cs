using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Agentic.Check;

interface IDirectiveSource
{
    Task<IReadOnlyList<DirectiveSourceFile>> ListAsync(CancellationToken cancellationToken);

    Task<string> FetchAsync(DirectiveSourceFile sourceFile, CancellationToken cancellationToken);
}

sealed record DirectiveSourceFile(string FileName, string DownloadUrl);

sealed record DirectiveBlock(string Name, string Content);

sealed record DirectiveResult(
    bool Success,
    string AgentsFile,
    string ClaudeFile,
    IReadOnlyList<DirectiveReportItem> Directives,
    IReadOnlyList<string> Actions,
    string? Error);

sealed record DirectiveReportItem(string Name, string Status);

sealed record DirectivePlanResult(
    bool Success,
    string AgentsFile,
    string ClaudeFile,
    bool ManageClaudeFile,
    bool CreateAgentsFile,
    bool CreateClaudeFile,
    IReadOnlyList<DirectivePlanItem> Directives,
    string AgentsContent,
    string ClaudeContent,
    string? Error)
{
    public int RecommendedCount => Directives.Count;

    public int MissingCount => Directives.Count(directive => directive.Status == DirectiveStatuses.Missing);

    public int OutdatedCount => Directives.Count(directive => directive.Status == DirectiveStatuses.Outdated);

    public IReadOnlyList<DirectivePlanItem> SelectableDirectives
        => [.. Directives.Where(directive => directive.Status is DirectiveStatuses.Missing or DirectiveStatuses.Outdated)];
}

sealed record DirectivePlanItem(string Name, string Status, string Content);

static class DirectiveStatuses
{
    public const string Missing = "missing";
    public const string Outdated = "outdated";
    public const string Current = "current";
}

sealed partial class DirectiveInstaller(IDirectiveSource source, IReporter reporter)
{
    public async Task<DirectiveResult> EnsureAsync(
        string repoRoot,
        StackDetectionResult stack,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(repoRoot, stack, cancellationToken).ConfigureAwait(false);
        return !plan.Success
            ? new DirectiveResult(false, string.Empty, string.Empty, [], [], plan.Error)
            : await ApplyAsync(plan, plan.SelectableDirectives.Select(directive => directive.Name), dryRun, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<DirectivePlanResult> PlanAsync(
        string repoRoot,
        StackDetectionResult stack,
        CancellationToken cancellationToken)
        => await PlanAsync(repoRoot, stack, true, cancellationToken).ConfigureAwait(false);

    public async Task<DirectivePlanResult> PlanAsync(
        string repoRoot,
        StackDetectionResult stack,
        bool manageClaudeFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceFiles = await source.ListAsync(cancellationToken).ConfigureAwait(false);
            var selectedSourceFiles = FilterByStack(sourceFiles, stack);
            List<DirectiveBlock> directiveBlocks = [];
            foreach (var sourceFile in selectedSourceFiles)
            {
                string content = await source.FetchAsync(sourceFile, cancellationToken).ConfigureAwait(false);
                directiveBlocks.Add(ExtractDirectiveBlock(sourceFile, content));
            }

            string agentsFile = ResolvePreferredFile(repoRoot, "AGENTS.md", "AGENTS.MD");
            string claudeFile = manageClaudeFile ? ResolvePreferredFile(repoRoot, "CLAUDE.md", "CLAUDE.MD") : string.Empty;
            string agentsContent = File.Exists(agentsFile)
                ? await File.ReadAllTextAsync(agentsFile, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            string claudeContent = manageClaudeFile && File.Exists(claudeFile)
                ? await File.ReadAllTextAsync(claudeFile, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            List<DirectivePlanItem> directivePlanItems = [];
            foreach (var directive in directiveBlocks)
            {
                directivePlanItems.Add(PlanDirective(agentsContent, directive));
            }

            return new DirectivePlanResult(
                true,
                agentsFile,
                claudeFile,
                manageClaudeFile,
                !File.Exists(agentsFile),
                manageClaudeFile && !File.Exists(claudeFile),
                directivePlanItems,
                agentsContent,
                claudeContent,
                null);
        }
        catch (DirectiveException exception)
        {
            reporter.Error(exception.Message);
            return new DirectivePlanResult(false, string.Empty, string.Empty, false, false, false, [], string.Empty, string.Empty, exception.Message);
        }
    }

    public async Task<DirectiveResult> ApplyAsync(
        DirectivePlanResult plan,
        IEnumerable<string> selectedDirectiveNames,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (!plan.Success)
        {
            return new DirectiveResult(false, plan.AgentsFile, plan.ClaudeFile, [], [], plan.Error);
        }

        try
        {
            var selectedNames = selectedDirectiveNames.ToHashSet(StringComparer.Ordinal);
            List<string> actions = [];
            List<DirectiveReportItem> directiveReports = [.. plan.Directives.Select(directive => new DirectiveReportItem(directive.Name, directive.Status))];
            IReadOnlyList<DirectiveBlock> selectedBlocks = [.. plan.Directives
                .Where(directive => selectedNames.Contains(directive.Name))
                .Select(directive => new DirectiveBlock(directive.Name, directive.Content))];
            string updatedAgentsContent = ApplyDirectiveBlocks(plan.AgentsContent, selectedBlocks, actions, dryRun);
            string updatedClaudeContent = plan.ManageClaudeFile
                ? EnsureClaudeImport(
                    plan.ClaudeContent,
                    Path.GetFileName(plan.AgentsFile),
                    plan.ClaudeFile,
                    dryRun,
                    actions)
                : plan.ClaudeContent;

            if (dryRun)
            {
                if (plan.CreateAgentsFile)
                {
                    actions.Add($"Would create {plan.AgentsFile}.");
                }

                if (plan.CreateClaudeFile)
                {
                    actions.Add($"Would create {plan.ClaudeFile}.");
                }
            }
            else
            {
                await WriteIfChangedAsync(plan.AgentsFile, updatedAgentsContent, cancellationToken).ConfigureAwait(false);
                if (plan.ManageClaudeFile)
                {
                    await WriteIfChangedAsync(plan.ClaudeFile, updatedClaudeContent, cancellationToken).ConfigureAwait(false);
                }

                await ValidateAsync(plan.AgentsFile, plan.ClaudeFile, Path.GetFileName(plan.AgentsFile), plan.ManageClaudeFile, selectedBlocks, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new DirectiveResult(true, plan.AgentsFile, plan.ClaudeFile, directiveReports, actions, null);
        }
        catch (DirectiveException exception)
        {
            reporter.Error(exception.Message);
            return new DirectiveResult(false, plan.AgentsFile, plan.ClaudeFile, [], [], exception.Message);
        }
    }

    static IReadOnlyList<DirectiveSourceFile> FilterByStack(
        IReadOnlyList<DirectiveSourceFile> sourceFiles,
        StackDetectionResult stack)
        => [.. sourceFiles
            .Where(file => file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(file => stack.Technologies.Contains(TechnologyNames.Dotnet, StringComparer.OrdinalIgnoreCase)
                || !file.FileName.StartsWith("dotnet-", StringComparison.OrdinalIgnoreCase))
            .Where(file => stack.Technologies.Contains(TechnologyNames.Uno, StringComparer.OrdinalIgnoreCase)
                || !file.FileName.StartsWith("uno-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)];

    internal static string FormatDirectiveStatus(string status)
        => status switch
        {
            DirectiveStatuses.Current => "up to date",
            DirectiveStatuses.Outdated => "update(s) available",
            _ => status
        };

    static DirectiveBlock ExtractDirectiveBlock(DirectiveSourceFile sourceFile, string content)
    {
        string directiveName = Path.GetFileNameWithoutExtension(sourceFile.FileName);
        var match = DirectiveFenceRegex().Match(content);
        if (!match.Success)
        {
            throw new DirectiveException($"Could not extract fenced markdown directive block from {sourceFile.DownloadUrl}.");
        }

        string block = NormalizeNewlines(match.Groups["content"].Value.Trim());
        string startMarker = StartMarker(directiveName);
        string endMarker = EndMarker(directiveName);
        if (!block.Contains(startMarker, StringComparison.Ordinal) || !block.Contains(endMarker, StringComparison.Ordinal))
        {
            throw new DirectiveException($"Directive block from {sourceFile.DownloadUrl} is missing expected stable markers for {directiveName}.");
        }

        return new DirectiveBlock(directiveName, block);
    }

    static DirectivePlanItem PlanDirective(string agentsContent, DirectiveBlock directive)
    {
        string content = NormalizeNewlines(agentsContent);
        string startMarker = StartMarker(directive.Name);
        string endMarker = EndMarker(directive.Name);
        int startCount = CountOccurrences(content, startMarker);
        int endCount = CountOccurrences(content, endMarker);
        if (startCount != endCount || startCount > 1)
        {
            throw new DirectiveException($"Directive marker is inconsistent for {directive.Name}.");
        }

        if (startCount == 0)
        {
            return new DirectivePlanItem(directive.Name, DirectiveStatuses.Missing, directive.Content);
        }

        int startIndex = content.IndexOf(startMarker, StringComparison.Ordinal);
        int endIndex = content.IndexOf(endMarker, StringComparison.Ordinal);
        string existingBlock = content[startIndex..(endIndex + endMarker.Length)];
        string status = existingBlock.Equals(directive.Content, StringComparison.Ordinal)
            ? DirectiveStatuses.Current
            : DirectiveStatuses.Outdated;
        return new DirectivePlanItem(directive.Name, status, directive.Content);
    }

    static string ApplyDirectiveBlocks(
        string agentsContent,
        IReadOnlyList<DirectiveBlock> directiveBlocks,
        List<string> actions,
        bool dryRun)
    {
        string content = NormalizeNewlines(agentsContent);
        foreach (var directive in directiveBlocks)
        {
            string startMarker = StartMarker(directive.Name);
            string endMarker = EndMarker(directive.Name);
            int startCount = CountOccurrences(content, startMarker);
            int endCount = CountOccurrences(content, endMarker);
            if (startCount != endCount || startCount > 1)
            {
                throw new DirectiveException($"Directive marker is inconsistent for {directive.Name}.");
            }

            if (startCount == 1)
            {
                int startIndex = content.IndexOf(startMarker, StringComparison.Ordinal);
                int endIndex = content.IndexOf(endMarker, StringComparison.Ordinal);
                string existingBlock = content[startIndex..(endIndex + endMarker.Length)];
                if (existingBlock.Equals(directive.Content, StringComparison.Ordinal))
                {
                    continue;
                }

                content = content[..startIndex] + directive.Content + content[(endIndex + endMarker.Length)..];
                actions.Add(dryRun
                    ? $"Would update directive {directive.Name} in AGENTS."
                    : $"Updated directive {directive.Name} in AGENTS.");
                continue;
            }

            content = AppendBlock(content, directive.Content);
            actions.Add(dryRun
                ? $"Would add directive {directive.Name} to AGENTS."
                : $"Added directive {directive.Name} to AGENTS.");
        }

        return EnsureTrailingNewline(content);
    }

    static string EnsureClaudeImport(
        string claudeContent,
        string agentsFileName,
        string claudeFile,
        bool dryRun,
        List<string> actions)
    {
        string importLine = $"@{agentsFileName}";
        string[] lines = NormalizeNewlines(claudeContent).Split('\n');
        List<string> updatedLines = [];
        bool imported = false;
        bool changed = false;

        foreach (string line in lines)
        {
            if (AgentsImportRegex().IsMatch(line))
            {
                if (!imported)
                {
                    updatedLines.Add(importLine);
                    imported = true;
                    changed |= !line.Equals(importLine, StringComparison.Ordinal);
                }
                else
                {
                    changed = true;
                }

                continue;
            }

            updatedLines.Add(line);
        }

        if (!imported)
        {
            if (updatedLines is ["" ])
            {
                updatedLines.Clear();
            }

            updatedLines.Add(importLine);
            changed = true;
        }

        string updated = EnsureTrailingNewline(string.Join('\n', updatedLines).TrimEnd('\n'));
        if (changed)
        {
            actions.Add(dryRun
                ? $"Would update {claudeFile} to import {agentsFileName}."
                : $"Updated {claudeFile} to import {agentsFileName}.");
        }

        return updated;
    }

    static async Task ValidateAsync(
        string agentsFile,
        string claudeFile,
        string agentsFileName,
        bool manageClaudeFile,
        IReadOnlyList<DirectiveBlock> directiveBlocks,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(agentsFile))
        {
            throw new DirectiveException($"Validation failed: {agentsFile} was not created.");
        }

        string agentsContent = await File.ReadAllTextAsync(agentsFile, cancellationToken).ConfigureAwait(false);
        foreach (var directive in directiveBlocks)
        {
            bool hasBlock = agentsContent.Contains(StartMarker(directive.Name), StringComparison.Ordinal)
                && agentsContent.Contains(EndMarker(directive.Name), StringComparison.Ordinal);
            if (!hasBlock)
            {
                throw new DirectiveException($"Validation failed: AGENTS does not contain directive {directive.Name}.");
            }
        }

        if (!manageClaudeFile)
        {
            return;
        }

        if (!File.Exists(claudeFile))
        {
            throw new DirectiveException($"Validation failed: {claudeFile} was not created.");
        }

        bool importsAgents = (await File.ReadAllLinesAsync(claudeFile, cancellationToken).ConfigureAwait(false))
            .Any(line => line.Equals($"@{agentsFileName}", StringComparison.Ordinal));
        if (!importsAgents)
        {
            throw new DirectiveException($"Validation failed: {claudeFile} does not import {agentsFileName}.");
        }
    }

    static async Task WriteIfChangedAsync(string path, string content, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string? existingContent = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
        if (existingContent is null || !existingContent.Equals(content, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        }
    }

    static string ResolvePreferredFile(string repoRoot, string preferredName, string alternateName)
    {
        string? preferredPath = Directory.EnumerateFiles(repoRoot)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(preferredName, StringComparison.Ordinal));
        if (preferredPath is not null)
        {
            return preferredPath;
        }

        string? alternatePath = Directory.EnumerateFiles(repoRoot)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(alternateName, StringComparison.Ordinal));
        return alternatePath ?? Path.Combine(repoRoot, preferredName);
    }

    static string AppendBlock(string content, string block)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return block;
        }

        return EnsureTrailingNewline(content).TrimEnd('\n') + "\n\n" + block;
    }

    static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    static string EnsureTrailingNewline(string value)
        => value.EndsWith('\n') ? value : value + "\n";

    static int CountOccurrences(string value, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    static string StartMarker(string directiveName)
        => $"<!-- dotnet-agentic-engineering:{directiveName}:start -->";

    static string EndMarker(string directiveName)
        => $"<!-- dotnet-agentic-engineering:{directiveName}:end -->";

    [GeneratedRegex(@"(?ms)^~~~md\s*$\n(?<content>.*?)^~~~\s*$|^```md\s*$\n(?<content>.*?)^```\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveFenceRegex();

    [GeneratedRegex(@"^\s*@AGENTS\.MD\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgentsImportRegex();
}

sealed class GitHubDirectiveSource(
    HttpClient? httpClient = null,
    DirectiveCacheSettings? cacheSettings = null,
    IReporter? reporter = null) : IDirectiveSource
{
    static readonly Uri ListingUri = new(DirectiveInstallerListingUrl.Value);
    readonly HttpClient httpClient = httpClient ?? CreateHttpClient();
    readonly DirectiveHttpCache cache = new(cacheSettings ?? DirectiveCacheSettings.FromEnvironment(), reporter);

    public async Task<IReadOnlyList<DirectiveSourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        string content = await GetStringAsync(ListingUri, DirectiveInstallerListingUrl.Value, "directives listing", cancellationToken).ConfigureAwait(false);
        var items = JsonSerializer.Deserialize<IReadOnlyList<GitHubContentItem>>(content)
            ?? throw new DirectiveException($"Could not parse directives listing from {DirectiveInstallerListingUrl.Value}.");

        return [.. items
            .Where(item => item.Type.Equals("file", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl))
            .Select(item => new DirectiveSourceFile(item.Name, item.DownloadUrl))];
    }

    public async Task<string> FetchAsync(DirectiveSourceFile sourceFile, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceFile.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            throw new DirectiveException($"Directive download URL is invalid: {sourceFile.DownloadUrl}.");
        }

        return await GetStringAsync(downloadUri, sourceFile.DownloadUrl, $"directive from {sourceFile.DownloadUrl}", cancellationToken).ConfigureAwait(false);
    }

    async Task<string> GetStringAsync(Uri uri, string displayUrl, string description, CancellationToken cancellationToken)
    {
        if (cache.TryReadFresh(uri, out string? cachedContent))
        {
            return cachedContent;
        }

        HttpResponseMessage response;
        try
        {
            response = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            if (cache.TryReadStale(uri, displayUrl, exception.Message, out cachedContent))
            {
                return cachedContent;
            }

            throw new DirectiveException($"Could not fetch {displayUrl}: {exception.Message}", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                int statusCode = (int)response.StatusCode;
                if (statusCode is 403 or 429
                    && cache.TryReadStale(uri, displayUrl, $"HTTP {statusCode}", out cachedContent))
                {
                    return cachedContent;
                }

                throw new DirectiveException($"Could not fetch {description}: HTTP {statusCode}.");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            cache.TryWrite(uri, content);
            return content;
        }
    }

    async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
        => await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

    static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Agentic.Check", "0.1"));
        return client;
    }
}

sealed record DirectiveCacheSettings(
    int DurationSeconds,
    string CacheDirectory,
    IReadOnlyList<string> ConfigurationWarnings)
{
    public const int DefaultDurationSeconds = 1800;
    public const string DurationEnvironmentVariable = "AGENTIC_CHECK_CACHE_SECONDS";
    public const string DirectoryEnvironmentVariable = "AGENTIC_CHECK_CACHE_DIR";

    public bool ReadFreshCache => DurationSeconds > 0;

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

    public string DurationDescription
    {
        get
        {
            if (DurationSeconds == 0)
            {
                return "0 minutes (fresh fetch; successful responses still refresh cache)";
            }

            if (DurationSeconds % 60 == 0)
            {
                int minutes = DurationSeconds / 60;
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes} minute(s)");
            }

            double minutesValue = DurationSeconds / 60d;
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutesValue:0.##} minute(s)");
        }
    }

    public static DirectiveCacheSettings FromEnvironment()
    {
        List<string> warnings = [];
        int durationSeconds = DefaultDurationSeconds;
        string? durationValue = Environment.GetEnvironmentVariable(DurationEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(durationValue)
            && (!int.TryParse(
                durationValue,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out durationSeconds)
                || durationSeconds < 0))
        {
            warnings.Add($"Configuration warning: {DurationEnvironmentVariable} value '{durationValue}' is invalid; using {DefaultDurationSeconds} seconds.");
            durationSeconds = DefaultDurationSeconds;
        }

        string cacheDirectory = ResolveCacheDirectory(warnings);
        return new DirectiveCacheSettings(durationSeconds, cacheDirectory, warnings);
    }

    static string ResolveCacheDirectory(List<string> warnings)
    {
        string? configuredDirectory = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            try
            {
                return Path.GetFullPath(configuredDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings.Add($"Configuration warning: {DirectoryEnvironmentVariable} value '{configuredDirectory}' is invalid; using default cache directory.");
            }
        }

        return DefaultCacheDirectory();
    }

    static string DefaultCacheDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(home))
        {
            return Path.Combine(home, "Library", "Caches", "Agentic.Check");
        }

        if (OperatingSystem.IsWindows())
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localApplicationData))
            {
                return Path.Combine(localApplicationData, "Agentic.Check", "Cache");
            }
        }

        string? xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCacheHome))
        {
            return Path.Combine(xdgCacheHome, "Agentic.Check");
        }

        return !string.IsNullOrWhiteSpace(home)
            ? Path.Combine(home, ".cache", "Agentic.Check")
            : Path.Combine(Path.GetTempPath(), "Agentic.Check", "Cache");
    }
}

sealed class DirectiveHttpCache(DirectiveCacheSettings settings, IReporter? reporter)
{
    static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool TryReadFresh(Uri uri, [NotNullWhen(true)] out string? content)
    {
        content = null;
        if (!settings.ReadFreshCache)
        {
            return false;
        }

        if (!TryReadEntry(uri, out var entry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - entry.FetchedAtUtc > settings.Duration)
        {
            return false;
        }

        content = entry.Content;
        return true;
    }

    public bool TryReadStale(Uri uri, string displayUrl, string reason, [NotNullWhen(true)] out string? content)
    {
        content = null;
        if (!TryReadEntry(uri, out var entry))
        {
            return false;
        }

        reporter?.Warning($"Using stale cached directive response for {displayUrl} because the fresh fetch failed: {reason}.");
        content = entry.Content;
        return true;
    }

    public void TryWrite(Uri uri, string content)
    {
        try
        {
            _ = Directory.CreateDirectory(settings.CacheDirectory);
            string cachePath = CachePath(uri);
            string tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            string json = JsonSerializer.Serialize(
                new DirectiveHttpCacheEntry(uri.AbsoluteUri, DateTimeOffset.UtcNow, content),
                SerializerOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            reporter?.Warning($"Configuration warning: could not write directive cache in {settings.CacheDirectory}: {exception.Message}");
        }
    }

    bool TryReadEntry(Uri uri, out DirectiveHttpCacheEntry entry)
    {
        entry = new DirectiveHttpCacheEntry(string.Empty, DateTimeOffset.MinValue, string.Empty);
        try
        {
            string cachePath = CachePath(uri);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            string json = File.ReadAllText(cachePath);
            var cached = JsonSerializer.Deserialize<DirectiveHttpCacheEntry>(json, SerializerOptions);
            if (cached is null
                || !cached.Url.Equals(uri.AbsoluteUri, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(cached.Content))
            {
                return false;
            }

            entry = cached;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            reporter?.Warning($"Configuration warning: could not read directive cache in {settings.CacheDirectory}: {exception.Message}");
            return false;
        }
    }

    string CachePath(Uri uri)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        string fileName = Convert.ToHexString(hash) + ".json";
        return Path.Combine(settings.CacheDirectory, fileName);
    }
}

sealed record DirectiveHttpCacheEntry(string Url, DateTimeOffset FetchedAtUtc, string Content);

static class DirectiveInstallerListingUrl
{
    public const string Value = "https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=main";
}

sealed record GitHubContentItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("type")] string Type);

sealed class DirectiveException : Exception
{
    public DirectiveException()
    {
    }

    public DirectiveException(string message)
        : base(message)
    {
    }

    public DirectiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
