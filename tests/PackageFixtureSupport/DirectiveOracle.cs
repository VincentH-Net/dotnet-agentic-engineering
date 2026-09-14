using System.Text.RegularExpressions;

namespace Agentic.PackageFixtures;

static partial class DirectiveOracle
{
    internal static IEnumerable<(string Name, string Block)> Expected(string sourceDirectory, IReadOnlyCollection<string> technologies)
    {
        foreach (string file in Directory.GetFiles(Path.Combine(sourceDirectory, "directives"), "*.md").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if ((name.StartsWith("dotnet-", StringComparison.Ordinal) && !technologies.Contains("dotnet", StringComparer.Ordinal))
                || (name.StartsWith("uno-", StringComparison.Ordinal) && !technologies.Contains("uno", StringComparer.Ordinal)))
            {
                continue;
            }
            var match = BlockRegex().Match(File.ReadAllText(file));
            FixtureFiles.Require(match.Success, $"Missing directive fence: {file}");
            yield return (name, match.Groups["content"].Value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    [GeneratedRegex(@"(?ms)^~~~md\s*$\n(?<content>.*?)^~~~\s*$|^```md\s*$\n(?<content>.*?)^```\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockRegex();
}
