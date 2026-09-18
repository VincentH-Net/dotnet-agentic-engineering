using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Agentic.Check;

static partial class CompanionDependency
{
    internal const string PackageId = "InnoWvate.Agentic";
    internal const string SourceRepo = "VincentH-Net/dotnet-agentic-engineering";
    internal static SkillDependency Identity { get; } = new(string.Empty, PackageId);

    internal static IReadOnlyList<SkillDependency> ForDirective(string? name, string? content)
        => name == "foundation-prompt-log" && content is not null && Invocations(content).Count > 0 ? [Identity] : [];

    internal static SkillManifestEntry Action(string description = "install")
        => new(string.Empty, PackageId, PackageId, string.Empty, [], recommendationAction: description);

    // Authored invocations occupy a shell command line; backslash-newline continuation is allowed.
    // A literal compatibility option can occur before or after the subcommand, in either spelling.
    internal static IReadOnlyList<(string Command, string? Minimum)> Invocations(string content)
        => [.. InvocationRegex().Matches(content.Replace("\\\r\n", " ", StringComparison.Ordinal).Replace("\\\n", " ", StringComparison.Ordinal))
            .Select(match => (match.Value, MinimumRegex().Matches(match.Value) is { Count: 1 } minimum ? minimum[0].Groups[1].Value : null))];

    internal static ToolVersion ReadLocalRequirement(IEnumerable<string> contents)
    {
        string[] requirements = [.. contents.SelectMany(Invocations).Select(invocation => invocation.Minimum ?? throw new FormatException("Installed companion invocation is missing literal -m / --minver.")).Distinct(StringComparer.Ordinal)];
        return requirements.Length == 1
            ? ToolVersion.ParseMinimum(requirements[0])
            : throw new FormatException("Installed companion requirements are missing or conflicting; repair consumers interactively.");
    }

    [GeneratedRegex(@"\bdotnet[ \t]+agentic\b[^\r\n;`]*", RegexOptions.CultureInvariant)]
    private static partial Regex InvocationRegex();

    [GeneratedRegex(@"(?:^|\s)(?:-m|--minver)(?:\s+|=)([0-9]+\.[0-9]+)(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumRegex();
}

sealed class CompanionSourceVersionReader(IDirectiveSource source)
{
    internal const string ProjectPath = "src/Agentic/Agentic.csproj";
    readonly Dictionary<string, Task<ToolVersion>> versions = new(StringComparer.Ordinal);

    internal Task<ToolVersion> ReadAsync(string sourceRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceRef))
        {
            throw new DirectiveException("Cannot resolve the companion's source revision.");
        }

        if (!versions.TryGetValue(sourceRef, out var version))
        {
            version = FetchAsync(sourceRef, cancellationToken);
            versions.Add(sourceRef, version);
        }

        return version;
    }

    async Task<ToolVersion> FetchAsync(string sourceRef, CancellationToken cancellationToken)
    {
        string url = $"https://raw.githubusercontent.com/{CompanionDependency.SourceRepo}/{Uri.EscapeDataString(sourceRef)}/{ProjectPath}";
        string content = await source.FetchAsync(new(ProjectPath, url, SourceRef: sourceRef), cancellationToken).ConfigureAwait(false);
        return Parse(content);
    }

    internal static ToolVersion Parse(string content)
    {
        var document = XDocument.Parse(content);
        var versions = document.Descendants().Where(element => element.Name.LocalName == "Version").ToArray();
        if (versions.Length != 1 || versions[0].HasElements || versions[0].HasAttributes
            || versions[0].Ancestors().Any(element => element.Attribute("Condition") is not null))
        {
            throw new FormatException($"{ProjectPath} must contain one explicit, unconditional literal <Version>.");
        }

        return ToolVersion.Parse(versions[0].Value);
    }
}
