using System.Text.RegularExpressions;

namespace Agentic.Check;

// The skills a directive tells the agent to use, read from the directive's own text so a dependency
// cannot drift from what the agent reads.
static partial class DirectiveSkillDependency
{
    // A directive names a skill it needs as `name` skill. A name in backticks without the word after it
    // is only a mention: uno-build-and-run names `uno-testing` as a skill whose steps it overrides.
    internal static IReadOnlySet<string> NamedIn(string? content)
        => content is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : NamedSkillRegex().Matches(content).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"`([a-z0-9][a-z0-9-]*)` skill\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamedSkillRegex();
}
