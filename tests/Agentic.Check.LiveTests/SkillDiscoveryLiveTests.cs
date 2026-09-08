using System.Text.Json;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class SkillDiscoveryLiveTests(ITestOutputHelper output)
{
    static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    [SkillMaintenanceFact]
    [Trait("Category", "SkillMaintenance")]
    public async Task SourceRepositoriesHaveNoUnreviewedSkills()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(20));
        MaintenanceGh gh = new();
        var generatedAt = DateTimeOffset.Now;
        List<SkillRepositoryScan> scans = [];
        List<SkillScanError> errors = [];
        List<string> failures = [];
        foreach (var review in StaticSkillManifest.SourceReviews)
        {
            output.WriteLine($"Scanning {review.SourceRepo}");
            try
            {
                var preview = await gh.DefaultBranchAsync(review.SourceRepo, cancellation.Token).ConfigureAwait(true);
                var stable = await gh.StableAsync(review.SourceRepo, preview, cancellation.Token).ConfigureAwait(true);
                var baselineFiles = await gh.InventoryAsync(review.SourceRepo, review.CommitSha, cancellation.Token).ConfigureAwait(true);
                var stableFiles = await gh.InventoryAsync(review.SourceRepo, stable.CommitSha, cancellation.Token).ConfigureAwait(true);
                var previewFiles = await gh.InventoryAsync(review.SourceRepo, preview.CommitSha, cancellation.Token).ConfigureAwait(true);
                var baselinePaths = baselineFiles.Select(skill => skill.Path).ToHashSet(StringComparer.Ordinal);
                var previewPaths = previewFiles.Select(skill => skill.Path).ToHashSet(StringComparer.Ordinal);
                baselineFiles = await DescribeAsync(gh, review.SourceRepo, baselineFiles, skill => !previewPaths.Contains(skill.Path), cancellation.Token).ConfigureAwait(true);
                stableFiles = await DescribeAsync(gh, review.SourceRepo, stableFiles, skill => !baselinePaths.Contains(skill.Path), cancellation.Token, identifyAll: true).ConfigureAwait(true);
                previewFiles = await DescribeAsync(gh, review.SourceRepo, previewFiles, skill => !baselinePaths.Contains(skill.Path), cancellation.Token, identifyAll: true).ConfigureAwait(true);
                var items = SkillDiscovery.Compare(review.SourceRepo, baselineFiles, stableFiles, previewFiles,
                    StaticSkillManifest.All, StaticSkillManifest.Preview, stable.CommittedAt <= review.ReviewedAt);
                scans.Add(new(review, stable, preview, items));
                int count = items.Count(SkillDiscovery.NeedsReview);
                output.WriteLine($"{review.SourceRepo}: {count} item(s) require review.");
                if (count > 0)
                {
                    failures.Add($"{review.SourceRepo}: {count} item(s) require review.");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                failures.Add($"{review.SourceRepo}: scan error: {exception.Message}");
                errors.Add(new(review.SourceRepo, exception.Message));
            }
        }

        string metadata = JsonSerializer.Serialize(new
        {
            GeneratedAt = generatedAt,
            gh.CacheDuration,
            Cache = "gh api cache; separate from agentic-check. Cache hits are not tracked; no stale fallback.",
            Sources = scans.Select(scan => new { scan.Review, scan.Stable, scan.Preview }),
            Errors = errors
        }, ReportJsonOptions);
        _ = await MaintenanceReport.WriteAsync("skill-discovery-sources.json", metadata).ConfigureAwait(true);
        string path = await MaintenanceReport.WriteAsync("skill-discovery.md", SkillDiscoveryReport.Render(scans, errors, generatedAt)).ConfigureAwait(true);
        output.WriteLine($"Report: {path}");
        Assert.True(failures.Count == 0, $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}Report: {path}");
    }

    static async Task<IReadOnlyList<SkillTreeFile>> DescribeAsync(MaintenanceGh gh, string repo, IReadOnlyList<SkillTreeFile> files,
        Func<SkillTreeFile, bool> shouldDescribe, CancellationToken cancellationToken, bool identifyAll = false)
    {
        List<SkillTreeFile> result = [];
        foreach (var file in files)
        {
            bool required = shouldDescribe(file);
            result.Add(required || identifyAll
                ? await gh.DescribeAsync(repo, file, cancellationToken, allowInvalidFrontmatter: !required).ConfigureAwait(false)
                : file);
        }

        return result;
    }
}

static class MaintenanceReport
{
    internal static async Task<string> WriteAsync(string name, string content)
    {
        string directory = Environment.GetEnvironmentVariable("AGENTIC_CHECK_MAINTENANCE_REPORT_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "skill-maintenance");
        directory = Path.GetFullPath(directory);
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        return path;
    }
}
