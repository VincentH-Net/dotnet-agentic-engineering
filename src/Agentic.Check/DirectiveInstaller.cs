using System.Net.Http.Headers;
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

sealed partial class DirectiveInstaller(IDirectiveSource source, IReporter reporter)
{
    public async Task<DirectiveResult> EnsureAsync(
        string repoRoot,
        StackDetectionResult stack,
        bool dryRun,
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
            string claudeFile = ResolvePreferredFile(repoRoot, "CLAUDE.md", "CLAUDE.MD");
            string agentsContent = File.Exists(agentsFile)
                ? await File.ReadAllTextAsync(agentsFile, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            string claudeContent = File.Exists(claudeFile)
                ? await File.ReadAllTextAsync(claudeFile, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            List<string> actions = [];
            List<DirectiveReportItem> directiveReports = [];
            string updatedAgentsContent = ApplyDirectiveBlocks(agentsContent, directiveBlocks, directiveReports, actions);
            string updatedClaudeContent = EnsureClaudeImport(
                claudeContent,
                Path.GetFileName(agentsFile),
                claudeFile,
                actions);

            if (dryRun)
            {
                if (!File.Exists(agentsFile))
                {
                    actions.Add($"Would create {agentsFile}.");
                }

                if (!File.Exists(claudeFile))
                {
                    actions.Add($"Would create {claudeFile}.");
                }
            }
            else
            {
                await WriteIfChangedAsync(agentsFile, updatedAgentsContent, cancellationToken).ConfigureAwait(false);
                await WriteIfChangedAsync(claudeFile, updatedClaudeContent, cancellationToken).ConfigureAwait(false);
                await ValidateAsync(agentsFile, claudeFile, Path.GetFileName(agentsFile), directiveBlocks, cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var directive in directiveReports)
            {
                reporter.Info($"Directive {directive.Name}: {directive.Status}");
            }

            return new DirectiveResult(true, agentsFile, claudeFile, directiveReports, actions, null);
        }
        catch (DirectiveException exception)
        {
            reporter.Error(exception.Message);
            return new DirectiveResult(false, string.Empty, string.Empty, [], [], exception.Message);
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

    static string ApplyDirectiveBlocks(
        string agentsContent,
        IReadOnlyList<DirectiveBlock> directiveBlocks,
        List<DirectiveReportItem> directiveReports,
        List<string> actions)
    {
        string content = NormalizeNewlines(agentsContent);
        foreach (var directive in directiveBlocks)
        {
            if (content.Contains(SkipMarker(directive.Name), StringComparison.Ordinal))
            {
                directiveReports.Add(new DirectiveReportItem(directive.Name, "skipped"));
                actions.Add($"Skipped directive {directive.Name} because AGENTS contains a skip marker.");
                continue;
            }

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
                    directiveReports.Add(new DirectiveReportItem(directive.Name, "unchanged"));
                    continue;
                }

                content = content[..startIndex] + directive.Content + content[(endIndex + endMarker.Length)..];
                directiveReports.Add(new DirectiveReportItem(directive.Name, "updated"));
                actions.Add($"Updated directive {directive.Name} in AGENTS.");
                continue;
            }

            content = AppendBlock(content, directive.Content);
            directiveReports.Add(new DirectiveReportItem(directive.Name, "added"));
            actions.Add($"Added directive {directive.Name} to AGENTS.");
        }

        return EnsureTrailingNewline(content);
    }

    static string EnsureClaudeImport(
        string claudeContent,
        string agentsFileName,
        string claudeFile,
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
            actions.Add($"Updated {claudeFile} to import {agentsFileName}.");
        }

        return updated;
    }

    static async Task ValidateAsync(
        string agentsFile,
        string claudeFile,
        string agentsFileName,
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
            bool hasSkip = agentsContent.Contains(SkipMarker(directive.Name), StringComparison.Ordinal);
            if (!hasBlock && !hasSkip)
            {
                throw new DirectiveException($"Validation failed: AGENTS does not contain directive {directive.Name} or its skip marker.");
            }
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

    static string SkipMarker(string directiveName)
        => $"<!-- dotnet-agentic-engineering:{directiveName}:skip -->";

    [GeneratedRegex(@"(?ms)^~~~md\s*$\n(?<content>.*?)^~~~\s*$|^```md\s*$\n(?<content>.*?)^```\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveFenceRegex();

    [GeneratedRegex(@"^\s*@AGENTS\.MD\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgentsImportRegex();
}

sealed class GitHubDirectiveSource(HttpClient? httpClient = null) : IDirectiveSource
{
    static readonly Uri ListingUri = new(DirectiveInstallerListingUrl.Value);
    readonly HttpClient httpClient = httpClient ?? CreateHttpClient();

    public async Task<IReadOnlyList<DirectiveSourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await GetAsync(ListingUri, DirectiveInstallerListingUrl.Value, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectiveException($"Could not fetch directives listing from {DirectiveInstallerListingUrl.Value}: HTTP {(int)response.StatusCode}.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var items = await JsonSerializer.DeserializeAsync<IReadOnlyList<GitHubContentItem>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false)
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

        using var response = await GetAsync(downloadUri, sourceFile.DownloadUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectiveException($"Could not fetch directive from {sourceFile.DownloadUrl}: HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<HttpResponseMessage> GetAsync(Uri uri, string displayUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new DirectiveException($"Could not fetch {displayUrl}: {exception.Message}", exception);
        }
    }

    static HttpClient CreateHttpClient()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Agentic.Check", "0.1"));
        return client;
    }
}

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
