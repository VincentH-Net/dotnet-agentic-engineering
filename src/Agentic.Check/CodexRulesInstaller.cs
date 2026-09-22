namespace Agentic.Check;

sealed record CodexRulesPlan(string File, bool FileExists, bool IsCurrent, string Display)
{
    public string Action => FileExists ? "update" : "install";

    public string Status => IsCurrent ? "up to date" : Action;
}

sealed record CodexRulesReport(string File, string Action, IReadOnlyList<string> Rules, bool Success, string? Error);

// Codex runs a command that matches a project allow rule outside its sandbox, so dotnet gets network
// access and its home-directory caches from the first attempt without an approval prompt. For Codex
// targets these rules are offered as a tool item, independent of the dotnet-cli-run directive text.
// The file is owned by dna check and compared as a whole; Codex resolves several matching rules to the
// strictest one, so a user's own rules in another file are never overridden and never duplicated in effect.
// Codex loads every .codex folder from its working directory up to the git root, so a file above the
// target already covers it, while a file below it does not.
static class CodexRulesInstaller
{
    internal const string FileName = "dotnet-agentic-engineering.rules";
    internal static readonly string RelativePath = Path.Combine(".codex", "rules", FileName);
    internal static SkillDependency Identity { get; } = new(string.Empty, "codex-rules");

    static readonly (string Command, string Justification)[] Rules =
    [
        ("dotnet", "dotnet needs network access and writes outside the workspace (NuGet caches), so it must run outside the sandbox from the first attempt."),
        ("dnx", "dnx is the SDK shorthand for dotnet tool exec; same needs as dotnet."),
        ("dna", "dna launches dotnet tools that fetch directives and skills; same needs as dotnet."),
        ("pwsh ./New-View.ps1", "The Uno C# Markup 2 New-View.ps1 script wraps dotnet new; same needs as dotnet.")
    ];

    internal static IReadOnlyList<string> Commands { get; } = [.. Rules.Select(rule => rule.Command)];

    internal static string Content { get; } = Render();

    internal static CodexRulesPlan Plan(string targetDirectory)
    {
        string? file = null;
        foreach (string directory in DirectoriesUpToGitRoot(targetDirectory))
        {
            string candidate = Path.Combine(directory, RelativePath);
            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }
        }

        bool exists = file is not null;
        file ??= Path.Combine(targetDirectory, RelativePath);
        bool current = exists && NormalizeNewlines(File.ReadAllText(file)) == Content;
        return new(file, exists, current, Path.GetRelativePath(targetDirectory, file));
    }

    internal static SkillManifestEntry Action(CodexRulesPlan plan)
        => new(string.Empty, Identity.InstallArg, "Codex rules: run dotnet outside the sandbox", string.Empty, [],
            recommendationAction: plan.Action,
            version: $"{plan.Display}: {string.Join(", ", Commands)}");

    internal static async Task<CodexRulesReport> EnsureAsync(CodexRulesPlan plan, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return new(plan.File, plan.Action, Commands, true, null);
        }

        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(plan.File)!);
            await File.WriteAllTextAsync(plan.File, Content, cancellationToken).ConfigureAwait(false);
            string written = await File.ReadAllTextAsync(plan.File, cancellationToken).ConfigureAwait(false);
            return NormalizeNewlines(written) == Content
                ? new(plan.File, plan.Action, Commands, true, null)
                : new(plan.File, plan.Action, Commands, false, $"Validation failed: {plan.File} does not contain the expected rules.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(plan.File, plan.Action, Commands, false, exception.Message);
        }
    }

    // The target itself, then each parent up to and including the nearest git root. Without a git
    // root only the target counts, so a user-level .codex folder is never mistaken for a project one.
    static List<string> DirectoriesUpToGitRoot(string targetDirectory)
    {
        List<string> directories = [];
        for (string? current = Path.GetFullPath(targetDirectory); current is not null; current = Path.GetDirectoryName(current))
        {
            directories.Add(current);
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
            {
                return directories;
            }
        }

        return [directories[0]];
    }

    static string Render()
    {
        string rules = string.Join("\n", Rules.Select(rule =>
            $"prefix_rule(\n    pattern = [{string.Join(", ", rule.Command.Split(' ').Select(token => $"\"{token}\""))}],\n    decision = \"allow\",\n    justification = \"{rule.Justification}\",\n)\n"));
        return "# Installed by dna check; it overwrites this file on updates, so keep your own rules in another file.\n"
            + "# Codex runs commands that match these rules outside its sandbox, with network access.\n\n"
            + rules;
    }

    static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
