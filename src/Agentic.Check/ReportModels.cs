namespace Agentic.Check;

sealed class AgenticCheckReport
{
    public string TargetDirectory { get; set; } = string.Empty;

    public bool DryRun { get; set; }

    public string? RepoRoot { get; set; }

    public string? SkillsDirectory { get; set; }

    public List<string> SkillsDirectories { get; } = [];

    public List<PrerequisiteCheck> Prerequisites { get; } = [];

    public List<string> Technologies { get; } = [];

    public List<InstallGateReport> InstallGates { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Advisories { get; } = [];

    public string AgentsFile { get; set; } = string.Empty;

    public string ClaudeFile { get; set; } = string.Empty;

    public DirectiveSummary? DirectiveSummary { get; set; }

    public List<DirectiveReportItem> Directives { get; } = [];

    public List<SkillReportItem> RecommendedSkills { get; } = [];

    public List<SkillReportItem> MissingSkills { get; } = [];

    public int OutdatedSkills { get; set; }

    public List<SkillInstallResult> InstallResults { get; } = [];

    public List<string> Actions { get; } = [];

    public CommandReport? SkillUpdateDryRun { get; set; }

    public CommandReport? SkillUpdate { get; set; }

    public List<SkillCopyResult> SkillCopyResults { get; } = [];

    public List<CommandReport> SkillUpdateDryRuns { get; } = [];

    public List<CommandReport> SkillUpdates { get; } = [];
}

sealed record CheckRunResult(int ExitCode, AgenticCheckReport Report);

sealed record DirectiveSummary(
    bool CreateAgentsFile,
    bool CreateClaudeFile,
    int RecommendedCount,
    int MissingCount,
    int OutdatedCount);

sealed record SkillReportItem(
    string SourceRepo,
    string InstallArg,
    string LocalFolder,
    string Plugin,
    IReadOnlyList<SkillDependency> Dependencies)
{
    internal static SkillReportItem FromManifestEntry(SkillManifestEntry entry)
        => new(entry.SourceRepo, entry.InstallArg, entry.LocalFolder, entry.Plugin, entry.Dependencies);
}

sealed record CommandReport(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    internal static CommandReport FromCommandResult(CommandResult result)
        => new(result.ExitCode, result.StandardOutput, result.StandardError);
}

sealed record SkillCopyResult(
    string SourceDirectory,
    string TargetDirectory,
    string LocalFolder,
    bool Success,
    string? Error);
