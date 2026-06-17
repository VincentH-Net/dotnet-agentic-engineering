namespace Agentic.Check;

sealed record AgenticCheckOptions(
    string TargetDirectory,
    bool DryRun,
    bool Yes,
    string? ReportPath,
    string? SkillsDirectory,
    string? Agents,
    bool Verbose);
