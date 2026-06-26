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

        checks.Add(await CheckGhSkillAsync(workingDirectory, cancellationToken).ConfigureAwait(false));

        return new PrerequisiteResult(checks);
    }

    async Task<PrerequisiteCheck> CheckGhSkillAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var singularHelp = await commandRunner.RunAsync("gh", ["skill", "--help"], workingDirectory, cancellationToken).ConfigureAwait(false);
        if (singularHelp.Success || LooksLikeGhSkillHelp(singularHelp))
        {
            return new PrerequisiteCheck("gh skill", true, null, null, singularHelp.StandardOutput, singularHelp.StandardError);
        }

        var pluralHelp = await commandRunner.RunAsync("gh", ["skills", "--help"], workingDirectory, cancellationToken).ConfigureAwait(false);
        if (pluralHelp.Success || LooksLikeGhSkillHelp(pluralHelp))
        {
            return new PrerequisiteCheck("gh skill", true, null, null, pluralHelp.StandardOutput, pluralHelp.StandardError);
        }

        return new PrerequisiteCheck(
            "gh skill",
            false,
            null,
            null,
            CombineCommandOutput(singularHelp.StandardOutput, pluralHelp.StandardOutput),
            CombineCommandOutput(singularHelp.StandardError, pluralHelp.StandardError));
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

    static bool LooksLikeGhSkillHelp(CommandResult result)
    {
        string output = result.StandardOutput + result.StandardError;
        return output.Contains("gh skill", StringComparison.OrdinalIgnoreCase)
            && output.Contains("AVAILABLE COMMANDS", StringComparison.OrdinalIgnoreCase)
            && output.Contains("install", StringComparison.OrdinalIgnoreCase)
            && output.Contains("update", StringComparison.OrdinalIgnoreCase);
    }

    static string CombineCommandOutput(string first, string second)
        => string.IsNullOrWhiteSpace(second)
            ? first
            : string.IsNullOrWhiteSpace(first)
                ? second
                : first.TrimEnd() + Environment.NewLine + second;

    [GeneratedRegex(@"(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}

sealed record PrerequisiteResult(IReadOnlyList<PrerequisiteCheck> Checks)
{
    public bool Success => Checks.All(check => check.Success);

    public bool IsSuccessful(bool dryRun)
        => dryRun
            ? Checks.Where(check => !check.Name.Equals("gh skill", StringComparison.OrdinalIgnoreCase)).All(check => check.Success)
            : Success;
}

sealed record PrerequisiteCheck(
    string Name,
    bool Success,
    string? Version,
    string? MinimumVersion,
    string StandardOutput,
    string StandardError);
