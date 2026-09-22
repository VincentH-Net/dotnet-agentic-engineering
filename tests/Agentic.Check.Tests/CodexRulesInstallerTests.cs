namespace Agentic.Check.Tests;

public sealed class CodexRulesInstallerTests
{
    [Fact]
    public void PlanInstallsAtTheTargetWhenNoFileExists()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");

        var plan = CodexRulesInstaller.Plan(tempDirectory.Path);

        Assert.False(plan.IsCurrent);
        Assert.False(plan.FileExists);
        Assert.Equal("install", plan.Action);
        Assert.Equal(Path.Combine(tempDirectory.Path, ".codex", "rules", "dna-dotnet.rules"), plan.File);
        Assert.Equal(Path.Combine(".codex", "rules", "dna-dotnet.rules"), plan.Display);
        Assert.Equal("Codex rules: run dotnet outside the sandbox (install)", RecommendationSelectionPrompt.FormatSkillListItem(CodexRulesInstaller.Action(plan)));
    }

    [Fact]
    public async Task EnsureWritesEveryRuleAndASecondPlanIsCurrent()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path);

        var result = await CodexRulesInstaller.EnsureAsync(plan, false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["dotnet", "dnx", "dna", "pwsh ./New-View.ps1"], result.Rules);
        string content = await File.ReadAllTextAsync(plan.File, CancellationToken.None);
        Assert.StartsWith("# Installed by dna check", content, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"dotnet\"],\n    decision = \"allow\"", content, StringComparison.Ordinal);
        Assert.Contains("pattern = [\"pwsh\", \"./New-View.ps1\"]", content, StringComparison.Ordinal);
        var replan = CodexRulesInstaller.Plan(tempDirectory.Path);
        Assert.True(replan.IsCurrent);
        Assert.Equal("up to date", replan.Status);
    }

    [Fact]
    public async Task EditedFileIsUpdatedInPlace()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".codex/rules/dna-dotnet.rules", "prefix_rule(pattern = [\"dotnet\"])\n");
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path);

        Assert.True(plan.FileExists);
        Assert.False(plan.IsCurrent);
        Assert.Equal("update", plan.Action);

        var result = await CodexRulesInstaller.EnsureAsync(plan, false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(CodexRulesInstaller.Content, await File.ReadAllTextAsync(plan.File, CancellationToken.None));
    }

    [Fact]
    public void FileAtTheGitRootCoversASpecializedSubfolder()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".git");
        tempDirectory.Write(".codex/rules/dna-dotnet.rules", CodexRulesInstaller.Content);
        string subfolder = tempDirectory.CreateDirectory("backend");

        var plan = CodexRulesInstaller.Plan(subfolder);

        Assert.True(plan.IsCurrent);
        Assert.Equal(Path.Combine(tempDirectory.Path, ".codex", "rules", "dna-dotnet.rules"), plan.File);
        Assert.Equal(Path.Combine("..", ".codex", "rules", "dna-dotnet.rules"), plan.Display);
    }

    [Fact]
    public void OutdatedFileAtTheGitRootIsUpdatedThereNotCopiedBelow()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".git");
        tempDirectory.Write(".codex/rules/dna-dotnet.rules", "stale\n");
        string subfolder = tempDirectory.CreateDirectory("backend");

        var plan = CodexRulesInstaller.Plan(subfolder);

        Assert.Equal("update", plan.Action);
        Assert.Equal(Path.Combine(tempDirectory.Path, ".codex", "rules", "dna-dotnet.rules"), plan.File);
    }

    [Fact]
    public void FileBelowTheTargetOrAboveTheGitRootDoesNotCount()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".codex/rules/dna-dotnet.rules", CodexRulesInstaller.Content);
        string repo = tempDirectory.CreateDirectory("repo");
        _ = tempDirectory.CreateDirectory("repo/.git");
        tempDirectory.Write("repo/backend/.codex/rules/dna-dotnet.rules", CodexRulesInstaller.Content);

        var plan = CodexRulesInstaller.Plan(repo);

        Assert.False(plan.FileExists);
        Assert.Equal(Path.Combine(repo, ".codex", "rules", "dna-dotnet.rules"), plan.File);
    }

    [Fact]
    public void WithoutAGitRootOnlyTheTargetCounts()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".codex/rules/dna-dotnet.rules", CodexRulesInstaller.Content);
        string subfolder = tempDirectory.CreateDirectory("loose");

        var plan = CodexRulesInstaller.Plan(subfolder);

        Assert.False(plan.FileExists);
        Assert.Equal(Path.Combine(subfolder, ".codex", "rules", "dna-dotnet.rules"), plan.File);
    }

    [Fact]
    public async Task DryRunReportsWithoutWriting()
    {
        using TempDirectory tempDirectory = new();
        _ = tempDirectory.CreateDirectory(".");
        var plan = CodexRulesInstaller.Plan(tempDirectory.Path);

        var result = await CodexRulesInstaller.EnsureAsync(plan, true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("install", result.Action);
        Assert.False(Directory.Exists(Path.Combine(tempDirectory.Path, ".codex")));
    }
}
