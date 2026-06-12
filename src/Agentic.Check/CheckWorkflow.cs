using System.Text.Json;

namespace Agentic.Check;

sealed class CheckWorkflow(ICommandRunner commandRunner, IUserPrompts prompts, IReporter reporter)
{
    static readonly JsonSerializerOptions ReportSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<CheckRunResult> RunAsync(AgenticCheckOptions options, CancellationToken cancellationToken)
    {
        AgenticCheckReport report = new()
        {
            TargetDirectory = Path.GetFullPath(options.TargetDirectory),
            DryRun = options.DryRun
        };

        var prerequisites = await new PrerequisiteChecker(commandRunner)
            .CheckAsync(report.TargetDirectory, cancellationToken)
            .ConfigureAwait(false);
        report.Prerequisites.AddRange(prerequisites.Checks);

        if (!prerequisites.Success)
        {
            reporter.Error("Required tools are missing or too old. Update git/GitHub CLI and confirm `gh skill --help` works.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var repoResolution = await new RepoResolver(commandRunner, prompts, reporter)
            .ResolveAsync(report.TargetDirectory, options.DryRun, cancellationToken)
            .ConfigureAwait(false);

        report.RepoRoot = repoResolution.RepoRoot;
        report.Actions.AddRange(repoResolution.Actions);

        if (!repoResolution.CanProceed)
        {
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var stack = StackDetector.Detect(repoResolution.RepoRoot);
        report.Technologies.AddRange(stack.Technologies.Order(StringComparer.OrdinalIgnoreCase));
        report.UnoGates.AddRange(stack.UnoGates);
        report.Warnings.AddRange(stack.Warnings);

        foreach (string warning in stack.Warnings)
        {
            reporter.Warning(warning);
        }

        string skillsDirectory = options.SkillsDirectory is { Length: > 0 }
            ? Path.GetFullPath(options.SkillsDirectory)
            : Path.Combine(repoResolution.RepoRoot, ".agents", "skills");
        report.SkillsDirectory = skillsDirectory;

        var recommended = SkillPlanner.Plan(StaticSkillManifest.All, stack);
        report.RecommendedSkills.AddRange(recommended.Select(SkillReportItem.FromManifestEntry));

        var missing = SkillInstaller.FindMissing(recommended, skillsDirectory);
        report.MissingSkills.AddRange(missing.Select(SkillReportItem.FromManifestEntry));

        reporter.Summary(repoResolution.RepoRoot, stack.Technologies, skillsDirectory, recommended.Count, missing.Count);

        if (stack.Technologies.Contains(TechnologyNames.Dotnet, StringComparer.OrdinalIgnoreCase))
        {
            const string advisory = "Recommended official .NET skills can be installed manually with: gh skill install dotnet/skills";
            report.Advisories.Add(advisory);
            reporter.Info(advisory);
        }

        IReadOnlyList<SkillManifestEntry> selectedSkills = [];
        if (missing.Count > 0)
        {
            selectedSkills = options.DryRun
                ? missing
                : options.Yes
                    ? missing
                    : await prompts.SelectSkillsAsync(missing, cancellationToken).ConfigureAwait(false);
        }

        if (options.DryRun)
        {
            foreach (var skill in selectedSkills)
            {
                report.Actions.Add($"Would install {skill.SourceRepo} {skill.InstallArg} into {skillsDirectory}.");
            }

            report.Actions.Add($"Would run gh skill update --dir {skillsDirectory} --dry-run.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(0, report);
        }

        if (selectedSkills.Count > 0)
        {
            var installResults = await new SkillInstaller(commandRunner, reporter)
                .InstallAsync(selectedSkills, skillsDirectory, repoResolution.RepoRoot, cancellationToken)
                .ConfigureAwait(false);
            report.InstallResults.AddRange(installResults);
        }

        await RunSkillUpdateAsync(options, skillsDirectory, repoResolution.RepoRoot, report, cancellationToken).ConfigureAwait(false);

        int exitCode = report.InstallResults.Any(result => !result.Success) ? 1 : 0;
        await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
        return new CheckRunResult(exitCode, report);
    }

    async Task RunSkillUpdateAsync(
        AgenticCheckOptions options,
        string skillsDirectory,
        string repoRoot,
        AgenticCheckReport report,
        CancellationToken cancellationToken)
    {
        var dryRunResult = await commandRunner.RunAsync(
            "gh",
            ["skill", "update", "--dir", skillsDirectory, "--dry-run"],
            repoRoot,
            cancellationToken).ConfigureAwait(false);

        report.Actions.Add("Ran gh skill update --dry-run.");
        report.SkillUpdateDryRun = CommandReport.FromCommandResult(dryRunResult);

        if (!dryRunResult.Success)
        {
            reporter.Warning("Could not check repo-local skills for updates.");
            return;
        }

        bool update = options.Yes || await prompts.ConfirmAsync("Update repo-local skills with gh skill update --all?", false, cancellationToken)
            .ConfigureAwait(false);

        if (!update)
        {
            report.Actions.Add("Skipped gh skill update --all.");
            return;
        }

        var updateResult = await commandRunner.RunAsync(
            "gh",
            ["skill", "update", "--dir", skillsDirectory, "--all"],
            repoRoot,
            cancellationToken).ConfigureAwait(false);

        report.Actions.Add("Ran gh skill update --all.");
        report.SkillUpdate = CommandReport.FromCommandResult(updateResult);
    }

    static async Task WriteReportAsync(string? reportPath, AgenticCheckReport report, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(reportPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(report, ReportSerializerOptions);
        await File.WriteAllTextAsync(fullPath, json, cancellationToken).ConfigureAwait(false);
    }
}
