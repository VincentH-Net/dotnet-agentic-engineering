using System.Text.Json;

namespace Agentic.Check;

sealed class CheckWorkflow(
    ICommandRunner commandRunner,
    IUserPrompts prompts,
    IReporter reporter,
    IDirectiveSource? directiveSource = null)
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

        if (!string.IsNullOrWhiteSpace(options.SkillsDirectory) && !string.IsNullOrWhiteSpace(options.Agents))
        {
            reporter.Error("Specify no more than one of --skills-dir and --agents.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

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

        IReadOnlyList<string> skillsDirectories;
        bool manageClaudeFile;
        if (options.SkillsDirectory is { Length: > 0 })
        {
            string skillsDirectory = Path.GetFullPath(options.SkillsDirectory);
            skillsDirectories = [skillsDirectory];
            manageClaudeFile = IsClaudeSkillsDirectory(skillsDirectory);
        }
        else
        {
            var directoryResolution = AgentSkillRegistry.ResolveProjectDirectories(options.Agents, repoResolution.RepoRoot);
            if (!directoryResolution.Success)
            {
                reporter.Error(directoryResolution.Error ?? "Could not resolve agent skill directories.");
                await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
                return new CheckRunResult(2, report);
            }

            skillsDirectories = directoryResolution.Directories;
            manageClaudeFile = directoryResolution.ManageClaude;
        }

        DirectiveInstaller directiveInstaller = new(directiveSource ?? new GitHubDirectiveSource(), reporter);
        var directivePlan = await directiveInstaller
            .PlanAsync(repoResolution.RepoRoot, stack, manageClaudeFile, cancellationToken)
            .ConfigureAwait(false);
        report.AgentsFile = directivePlan.AgentsFile;
        report.ClaudeFile = directivePlan.ClaudeFile;
        report.Directives.AddRange(directivePlan.Directives.Select(directive => new DirectiveReportItem(directive.Name, directive.Status)));
        if (!directivePlan.Success)
        {
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        DirectiveSummary directiveSummary = new(
            directivePlan.CreateAgentsFile,
            directivePlan.CreateClaudeFile,
            directivePlan.RecommendedCount,
            directivePlan.MissingCount,
            directivePlan.OutdatedCount);
        report.DirectiveSummary = directiveSummary;

        string firstSkillsDirectory = skillsDirectories[0];
        report.SkillsDirectory = firstSkillsDirectory;
        report.SkillsDirectories.AddRange(skillsDirectories);

        var recommended = SkillPlanner.Plan(StaticSkillManifest.All, stack);
        report.RecommendedSkills.AddRange(recommended.Select(SkillReportItem.FromManifestEntry));

        var missing = SkillInstaller.FindMissing(recommended, skillsDirectories);
        report.MissingSkills.AddRange(missing.Select(SkillReportItem.FromManifestEntry));

        reporter.Summary(repoResolution.RepoRoot, stack.Technologies, skillsDirectories, directiveSummary, recommended.Count, missing.Count);

        if (stack.Technologies.Contains(TechnologyNames.Dotnet, StringComparer.OrdinalIgnoreCase))
        {
            const string advisory = "Recommended official .NET skills can be installed manually with: gh skill install dotnet/skills";
            report.Advisories.Add(advisory);
            reporter.Info(advisory);
        }

        var recommendedDirectives = directivePlan.SelectableDirectives;
        IReadOnlyList<DirectivePlanItem> selectedDirectives = [];
        IReadOnlyList<SkillManifestEntry> selectedSkills = [];
        if (recommendedDirectives.Count > 0 || missing.Count > 0)
        {
            RecommendationSelectionContext selectionContext = new(
                repoResolution.RepoRoot,
                stack.Technologies,
                skillsDirectories,
                options.SkillsDirectory is { Length: > 0 }
                    ? $"--skills-dir {options.SkillsDirectory}"
                    : $"--agents {(!string.IsNullOrWhiteSpace(options.Agents) ? options.Agents : AgentSkillRegistry.DefaultAgents)}");

            if (options.DryRun || options.Yes)
            {
                selectedDirectives = recommendedDirectives;
                selectedSkills = missing;
            }
            else
            {
                var selection = await prompts
                    .SelectRecommendationsAsync(recommendedDirectives, missing, selectionContext, cancellationToken)
                    .ConfigureAwait(false);
                selectedDirectives = selection.SelectedDirectives;
                selectedSkills = selection.SelectedSkills;
            }
        }

        var directiveResult = await directiveInstaller
            .ApplyAsync(directivePlan, selectedDirectives.Select(directive => directive.Name), options.DryRun, cancellationToken)
            .ConfigureAwait(false);
        report.Actions.AddRange(directiveResult.Actions);
        if (!directiveResult.Success)
        {
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        if (options.DryRun)
        {
            foreach (var skill in selectedSkills)
            {
                foreach (string skillsDirectory in skillsDirectories.Where(directory => SkillInstaller.IsMissing(skill, directory)))
                {
                    report.Actions.Add($"Would install {skill.SourceRepo} {skill.InstallArg} into {skillsDirectory}.");
                }
            }

            foreach (string skillsDirectory in skillsDirectories)
            {
                report.Actions.Add($"Would run gh skill update --dir {skillsDirectory} --dry-run.");
            }

            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(0, report);
        }

        if (selectedSkills.Count > 0)
        {
            SkillInstaller skillInstaller = new(commandRunner, reporter);
            IReadOnlyList<SkillManifestEntry> firstDirectoryMissingSkills = [.. selectedSkills.Where(skill => SkillInstaller.IsMissing(skill, firstSkillsDirectory))];
            var installResults = await skillInstaller
                .InstallAsync(firstDirectoryMissingSkills, firstSkillsDirectory, repoResolution.RepoRoot, cancellationToken)
                .ConfigureAwait(false);
            report.InstallResults.AddRange(installResults);

            if (skillsDirectories.Count > 1)
            {
                report.SkillCopyResults.AddRange(skillInstaller.CopyInstalledSkills(
                    selectedSkills,
                    firstSkillsDirectory,
                    [.. skillsDirectories.Skip(1)]));
            }
        }

        await RunSkillUpdateAsync(options, skillsDirectories, repoResolution.RepoRoot, report, cancellationToken).ConfigureAwait(false);

        int exitCode = report.InstallResults.Any(result => !result.Success) || report.SkillCopyResults.Any(result => !result.Success) ? 1 : 0;
        await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
        return new CheckRunResult(exitCode, report);
    }

    async Task RunSkillUpdateAsync(
        AgenticCheckOptions options,
        IReadOnlyList<string> skillsDirectories,
        string repoRoot,
        AgenticCheckReport report,
        CancellationToken cancellationToken)
    {
        foreach (string skillsDirectory in skillsDirectories)
        {
            var dryRunResult = await commandRunner.RunAsync(
                "gh",
                ["skill", "update", "--dir", skillsDirectory, "--dry-run"],
                repoRoot,
                cancellationToken).ConfigureAwait(false);

            report.Actions.Add($"Ran gh skill update --dir {skillsDirectory} --dry-run.");
            var dryRunReport = CommandReport.FromCommandResult(dryRunResult);
            report.SkillUpdateDryRuns.Add(dryRunReport);
            report.SkillUpdateDryRun ??= dryRunReport;

            if (!dryRunResult.Success)
            {
                reporter.Warning($"Could not check repo-local skills for updates in {skillsDirectory}.");
            }
        }

        if (report.SkillUpdateDryRuns.All(result => !result.Success))
        {
            return;
        }

        bool update = options.Yes || await prompts.ConfirmAsync("Update repo-local skills in target directories with gh skill update --all?", false, cancellationToken)
            .ConfigureAwait(false);

        if (!update)
        {
            report.Actions.Add("Skipped gh skill update --all.");
            return;
        }

        foreach (string skillsDirectory in skillsDirectories)
        {
            var updateResult = await commandRunner.RunAsync(
                "gh",
                ["skill", "update", "--dir", skillsDirectory, "--all"],
                repoRoot,
                cancellationToken).ConfigureAwait(false);

            report.Actions.Add($"Ran gh skill update --dir {skillsDirectory} --all.");
            var updateReport = CommandReport.FromCommandResult(updateResult);
            report.SkillUpdates.Add(updateReport);
            report.SkillUpdate ??= updateReport;
        }
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

    static bool IsClaudeSkillsDirectory(string skillsDirectory)
    {
        string[] parts = skillsDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length >= 2
            && parts[^2].Equals(".claude", StringComparison.OrdinalIgnoreCase)
            && parts[^1].Equals("skills", StringComparison.OrdinalIgnoreCase);
    }
}
