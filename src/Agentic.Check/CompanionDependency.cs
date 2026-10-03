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

    // -m states the lowest minor of a major that content was written for, so content installed at
    // different minors of one major is compatible and the tool must satisfy the highest. Content
    // written for different majors cannot be served by one tool; the message names what disagrees.
    internal static ToolVersion ReadLocalRequirement(IEnumerable<(string Source, string Content)> installed)
    {
        List<(string Source, ToolVersion Minimum)> requirements = [];
        foreach (var (source, content) in installed)
        {
            foreach (var (_, minimum) in Invocations(content))
            {
                requirements.Add((source, ToolVersion.ParseMinimum(minimum
                    ?? throw new FormatException($"{source} calls dotnet agentic without a literal -m / --minver."))));
            }
        }

        if (requirements.Count == 0)
        {
            throw new FormatException("The installed directives and skills that use dotnet agentic state no -m / --minver. Run `dna check` and apply their pending updates.");
        }

        if (requirements.Select(requirement => requirement.Minimum.Major).Distinct().Count() > 1)
        {
            string asked = string.Join("; ", requirements.GroupBy(requirement => requirement.Source, StringComparer.Ordinal)
                .Select(group => $"{group.Key} {string.Join(", ", group.Select(requirement => requirement.Minimum.Minimum).Distinct(StringComparer.Ordinal))}"));
            throw new FormatException($"The installed directives and skills ask for different major versions of {PackageId}: {asked}. Run `dna check` and apply the pending updates for these items so they ask for the same major.");
        }

        return requirements.Max(requirement => requirement.Minimum)!;
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
