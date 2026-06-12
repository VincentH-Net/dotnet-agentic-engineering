using System.Text.RegularExpressions;

namespace Agentic.Check;

sealed partial class PrerequisiteChecker(ICommandRunner commandRunner)
{
    static readonly Version MinimumGitVersion = new(2, 39, 0);
    static readonly Version MinimumGhVersion = new(2, 93, 0);

    public async Task<PrerequisiteResult> CheckAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        List<PrerequisiteCheck> checks = [];

        var gitVersion = await commandRunner.RunAsync("git", ["--version"], workingDirectory, cancellationToken).ConfigureAwait(false);
        checks.Add(CheckVersion("git", gitVersion, MinimumGitVersion));

        var ghVersion = await commandRunner.RunAsync("gh", ["--version"], workingDirectory, cancellationToken).ConfigureAwait(false);
        checks.Add(CheckVersion("gh", ghVersion, MinimumGhVersion));

        var ghSkillHelp = await commandRunner.RunAsync("gh", ["skill", "--help"], workingDirectory, cancellationToken).ConfigureAwait(false);
        checks.Add(new PrerequisiteCheck("gh skill", ghSkillHelp.Success, null, null, ghSkillHelp.StandardOutput, ghSkillHelp.StandardError));

        return new PrerequisiteResult(checks);
    }

    static PrerequisiteCheck CheckVersion(string name, CommandResult result, Version minimumVersion)
    {
        var version = result.Success ? ParseVersion(result.StandardOutput) : null;
        bool success = result.Success && version is not null && version >= minimumVersion;
        return new PrerequisiteCheck(name, success, version?.ToString(), minimumVersion.ToString(), result.StandardOutput, result.StandardError);
    }

    static Version? ParseVersion(string value)
    {
        var match = VersionRegex().Match(value);
        return match.Success
            ? new Version(
                int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    [GeneratedRegex(@"(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}

sealed record PrerequisiteResult(IReadOnlyList<PrerequisiteCheck> Checks)
{
    public bool Success => Checks.All(check => check.Success);
}

sealed record PrerequisiteCheck(
    string Name,
    bool Success,
    string? Version,
    string? MinimumVersion,
    string StandardOutput,
    string StandardError);
