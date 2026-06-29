using System.Diagnostics;

namespace Agentic.Check.LiveTests;

public sealed class ManifestGhSkillTests
{
    [Fact]
    [Trait("Category", "LiveGhSkill")]
    public async Task AllManifestSkillsCanBeFoundByGhSkillPreview()
    {
        List<string> failures = [];
        foreach (var skill in StaticSkillManifest.All)
        {
            var result = await RunGhSkillPreviewAsync(skill, CancellationToken.None);
            if (!result.Success)
            {
                failures.Add($"{skill.Display}: {result.StandardError}{result.StandardOutput}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    static async Task<CommandResult> RunGhSkillPreviewAsync(SkillManifestEntry skill, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("skill");
        startInfo.ArgumentList.Add("preview");
        startInfo.ArgumentList.Add(skill.SourceRepo);
        startInfo.ArgumentList.Add(skill.InstallArg);
        startInfo.Environment["GH_PAGER"] = "cat";

        using Process process = new()
        {
            StartInfo = startInfo
        };

        _ = process.Start();
        string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string standardError = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, standardOutput, standardError);
    }
}
