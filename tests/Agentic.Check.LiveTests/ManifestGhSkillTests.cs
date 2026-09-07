using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class ManifestGhSkillTests(ITestOutputHelper output)
{
    [SkillMaintenanceFact]
    [Trait("Category", "LiveGhSkill")]
    [Trait("Category", "SkillMaintenance")]
    public async Task AllManifestSkillsCanBeFoundByGhSkillPreview()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(30));
        MaintenanceGh gh = new();
        List<string> failures = [];
        StringBuilder report = new("# gh skill preview manifest validation\n\n");
        _ = report.AppendLine("Stable uses unversioned install arguments; preview uses the resolved default-branch commit SHA.");
        _ = report.AppendLine("gh api metadata is cached; gh skill preview manages its own fetching.").AppendLine();
        foreach (string repo in StaticSkillManifest.All.Concat(StaticSkillManifest.Preview).Select(skill => skill.SourceRepo).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"## {repo}").AppendLine();
            foreach (var skill in StaticSkillManifest.All.Where(skill => skill.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase)))
            {
                await ValidateAsync(skill, "stable", "", report, failures, cancellation.Token).ConfigureAwait(true);
            }

            try
            {
                var source = await gh.DefaultBranchAsync(repo, cancellation.Token).ConfigureAwait(true);
                _ = report.AppendLine(CultureInfo.InvariantCulture, $"Preview: `{source.Ref}` at `{source.CommitSha}`").AppendLine();
                foreach (var skill in StaticSkillManifest.Preview.Where(skill => skill.SourceRepo.Equals(repo, StringComparison.OrdinalIgnoreCase)))
                {
                    await ValidateAsync(skill, "preview", source.CommitSha, report, failures, cancellation.Token).ConfigureAwait(true);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                failures.Add($"{repo}: could not validate preview: {exception.Message}");
                _ = report.AppendLine(SkillDiscovery.Escape(failures[^1])).AppendLine();
            }
        }

        string path = await MaintenanceReport.WriteAsync("manifest-validation.md", report.ToString()).ConfigureAwait(true);
        output.WriteLine($"Report: {path}");
        Assert.True(failures.Count == 0, $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}Report: {path}");
    }

    async Task ValidateAsync(SkillManifestEntry skill, string mode, string reference, StringBuilder report, List<string> failures, CancellationToken cancellationToken)
    {
        string argument = PreviewArgument(skill, reference);
        output.WriteLine($"{mode}: {skill.SourceRepo} {argument}");
        _ = report.AppendLine(CultureInfo.InvariantCulture, $"### {mode}: {skill.InstallArg}").AppendLine();
        try
        {
            var result = await MaintenanceGh.RunAsync(["skill", "preview", skill.SourceRepo, argument], cancellationToken).ConfigureAwait(true);
            _ = report.AppendLine(result.Success ? "PASS" : "FAIL").AppendLine();
            // Keep the selected stable ref from stderr without copying the skill body into the report.
            _ = report.AppendLine(SkillDiscovery.Escape(result.StandardError.Trim())).AppendLine();
            if (!result.Success)
            {
                failures.Add($"{mode}: {skill.SourceRepo} {argument}: {result.StandardError}{result.StandardOutput}");
                _ = report.AppendLine(SkillDiscovery.Escape(result.StandardOutput.Trim())).AppendLine();
            }
        }
        catch (IOException exception)
        {
            failures.Add($"{mode}: {skill.Display}: {exception.Message}");
            _ = report.AppendLine(CultureInfo.InvariantCulture, $"ERROR: {SkillDiscovery.Escape(exception.Message)}").AppendLine();
        }
    }

    internal static string PreviewArgument(SkillManifestEntry skill, string reference)
        => string.IsNullOrEmpty(reference) ? skill.InstallArg : $"{skill.InstallArg.Split('@')[0]}@{reference}";
}
