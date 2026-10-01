using System.Globalization;
using System.Text.RegularExpressions;

namespace Agentic;

sealed partial record ToolVersion(int Major, int Minor, int Patch, string Suffix) : IComparable<ToolVersion>
{
    public bool IsPrerelease => Suffix.StartsWith('-');

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}{Suffix}");

    // Semantic Versioning 2.0.0 precedence: a release outranks its prereleases, prerelease
    // identifiers compare numerically or ordinally, and build metadata does not take part.
    public int CompareTo(ToolVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (result != 0)
        {
            return result;
        }

        string[] mine = PrereleaseIdentifiers();
        string[] theirs = other.PrereleaseIdentifiers();
        if (mine.Length == 0 || theirs.Length == 0)
        {
            return theirs.Length.CompareTo(mine.Length);
        }

        for (int index = 0; index < Math.Min(mine.Length, theirs.Length); index++)
        {
            bool mineNumeric = mine[index].All(char.IsAsciiDigit);
            bool theirsNumeric = theirs[index].All(char.IsAsciiDigit);
            result = mineNumeric && theirsNumeric ? CompareNumeric(mine[index], theirs[index])
                : mineNumeric != theirsNumeric ? (mineNumeric ? -1 : 1)
                : string.CompareOrdinal(mine[index], theirs[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    string[] PrereleaseIdentifiers()
    {
        string prerelease = Suffix.Split('+')[0];
        return prerelease.Length == 0 ? [] : prerelease[1..].Split('.');
    }

    static int CompareNumeric(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }

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
