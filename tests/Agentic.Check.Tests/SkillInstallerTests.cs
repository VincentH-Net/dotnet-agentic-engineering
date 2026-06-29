namespace Agentic.Check.Tests;

public sealed class SkillInstallerTests
{
    [Fact]
    public void FindMissingUsesLocalSkillFolder()
    {
        using TempDirectory tempDirectory = new();
        string skillsDirectory = tempDirectory.CreateDirectory(".agents/skills");
        tempDirectory.Write(".agents/skills/present-skill/SKILL.md", "# Present");
        SkillManifestEntry present = new("owner/repo", "present-skill", "present-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry missing = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);

        var result = SkillInstaller.FindMissing([present, missing], skillsDirectory);

        var found = Assert.Single(result);
        Assert.Equal("missing-skill", found.InstallArg);
    }

    [Fact]
    public async Task InstallContinuesAfterFailuresAndReportsEachResult()
    {
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "not found"));
        commandRunner.Enqueue(new CommandResult(0, "installed", string.Empty));
        SkillInstaller installer = new(commandRunner, new NullReporter());
        SkillManifestEntry missing = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry valid = new("owner/repo", "valid-skill", "valid-skill", TechnologyNames.Dotnet, []);

        var results = await installer.InstallAsync(
            [missing, valid],
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            Environment.CurrentDirectory,
            CancellationToken.None);

        Assert.Collection(
            results,
            result =>
            {
                Assert.False(result.Success);
                Assert.Equal("missing-skill", result.InstallArg);
                Assert.Contains("not found", result.StandardError, StringComparison.Ordinal);
            },
            result =>
            {
                Assert.True(result.Success);
                Assert.Equal("valid-skill", result.InstallArg);
            });
    }
}
