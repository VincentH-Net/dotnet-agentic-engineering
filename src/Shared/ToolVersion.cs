using System.Globalization;
using System.Text.RegularExpressions;

namespace Agentic;

sealed partial record ToolVersion(int Major, int Minor, int Patch, string Suffix)
{
    public bool IsPrerelease => Suffix.StartsWith('-');

    public string Minimum => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");

    public bool Satisfies(ToolVersion required) => Major == required.Major && Minor >= required.Minor;

    public string Pattern(bool preview) => string.Create(CultureInfo.InvariantCulture, $"{Major}.*{(preview ? "-*" : "")}");

    public static ToolVersion Parse(string value)
    {
        var match = VersionRegex().Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out int patch))
        {
            throw new FormatException($"Invalid literal package version: {value}.");
        }

        return new(major, minor, patch, match.Groups[4].Value);
    }

    public static ToolVersion ParseMinimum(string value)
    {
        if (!MinimumRegex().IsMatch(value))
        {
            throw new FormatException("--minver / -m requires exactly major.minor (for example 2.3).");
        }

        return Parse(value + ".0");
    }

    [GeneratedRegex(@"\A([0-9]+)\.([0-9]+)\.([0-9]+)((?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\A[0-9]+\.[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumRegex();
}

sealed record CompatibilityContext(ToolVersion Running, ToolVersion Required, bool Explicit);
