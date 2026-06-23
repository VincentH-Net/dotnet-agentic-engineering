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
        string targetAgents;
        if (options.SkillsDirectory is { Length: > 0 })
        {
            string skillsDirectory = Path.GetFullPath(options.SkillsDirectory);
            skillsDirectories = [skillsDirectory];
            manageClaudeFile = IsClaudeSkillsDirectory(skillsDirectory);
            targetAgents = "custom skills directory";
        }
        else
        {
            targetAgents = !string.IsNullOrWhiteSpace(options.Agents)
                ? options.Agents
                : AgentSkillRegistry.DefaultAgents;
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

        await RunSkillUpdateDryRunAsync(skillsDirectories, repoResolution.RepoRoot, report, cancellationToken).ConfigureAwait(false);
        report.OutdatedSkills = CountDistinctOutdatedSkills(report.SkillUpdateDryRuns);

        reporter.Summary(repoResolution.RepoRoot, stack.Technologies, targetAgents, skillsDirectories, directiveSummary, recommended.Count, missing.Count, report.OutdatedSkills);

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
            if (options.DryRun || options.Yes)
            {
                selectedDirectives = recommendedDirectives;
                selectedSkills = missing;
            }
            else
            {
                var selection = await prompts
                    .SelectRecommendationsAsync(recommendedDirectives, missing, cancellationToken)
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
            ReportDirectiveDryRunActions(selectedDirectives, report.AgentsFile);
            ReportSkillInstallDryRunActions(selectedSkills);

            foreach (var skill in selectedSkills)
            {
                foreach (string skillsDirectory in skillsDirectories.Where(directory => SkillInstaller.IsMissing(skill, directory)))
                {
                    report.Actions.Add($"Would install {skill.SourceRepo} {skill.InstallArg} into {skillsDirectory}.");
                }
            }

            ReportSkillUpdateDryRunActions(report.SkillUpdateDryRuns);

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
        ReportSkillUpdateDryRunOutputs(skillsDirectories, report.SkillUpdateDryRuns, SkillUpdateOutputKind.Preflight);

        if (report.SkillUpdateDryRuns.All(result => !result.Success))
        {
            return;
        }

        if (!report.SkillUpdateDryRuns.Any(ReportsAvailableSkillUpdates))
        {
            report.Actions.Add("No repo-local skill updates found.");
            return;
        }

        bool update = options.Yes || await prompts.ConfirmAsync("Update these skills?", false, cancellationToken)
            .ConfigureAwait(false);

        if (!update)
        {
            report.Actions.Add("Skipped repo-local skill updates.");
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
            ReportSkillUpdateOutput(skillsDirectory, updateReport, SkillUpdateOutputKind.Update);
        }
    }

    async Task RunSkillUpdateDryRunAsync(
        IReadOnlyList<string> skillsDirectories,
        string repoRoot,
        AgenticCheckReport report,
        CancellationToken cancellationToken)
    {
        foreach (string skillsDirectory in skillsDirectories)
        {
            var dryRunResult = await commandRunner.RunAsync(
                "gh",
                ["skill", "update", "--dir", skillsDirectory, "--all", "--dry-run"],
                repoRoot,
                cancellationToken).ConfigureAwait(false);

            report.Actions.Add($"Ran gh skill update --dir {skillsDirectory} --all --dry-run.");
            var dryRunReport = CommandReport.FromCommandResult(dryRunResult);
            report.SkillUpdateDryRuns.Add(dryRunReport);
            report.SkillUpdateDryRun ??= dryRunReport;

            if (!dryRunResult.Success)
            {
                reporter.Warning($"Could not check repo-local skills for updates in {skillsDirectory}.");
            }
        }
    }

    void ReportDirectiveDryRunActions(
        IReadOnlyList<DirectivePlanItem> selectedDirectives,
        string agentsFile)
    {
        string agentsFileName = Path.GetFileName(agentsFile);
        ReportDirectiveDryRunGroup(
            $"Would install directives into {agentsFileName}:",
            selectedDirectives.Where(directive => directive.Status == DirectiveStatuses.Missing));
        ReportDirectiveDryRunGroup(
            $"Would update directives in {agentsFileName}:",
            selectedDirectives.Where(directive => directive.Status == DirectiveStatuses.Outdated));
    }

    void ReportDirectiveDryRunGroup(string header, IEnumerable<DirectivePlanItem> directives)
    {
        string[] directiveNames = [.. directives.Select(directive => directive.Name)];
        if (directiveNames.Length == 0)
        {
            return;
        }

        reporter.Info(header);
        foreach (string directiveName in directiveNames)
        {
            reporter.Info($"  {directiveName}");
        }
    }

    void ReportSkillInstallDryRunActions(IReadOnlyList<SkillManifestEntry> selectedSkills)
    {
        if (selectedSkills.Count == 0)
        {
            return;
        }

        reporter.Info("Would install skills into repo skills directories:");
        foreach (var group in selectedSkills.GroupBy(skill => skill.SourceRepo, StringComparer.OrdinalIgnoreCase))
        {
            reporter.Info($"  {group.Key}:");
            foreach (var skill in group)
            {
                reporter.Info($"    {skill.InstallArg}");
            }
        }
    }

    void ReportSkillUpdateDryRunActions(IReadOnlyList<CommandReport> dryRunReports)
    {
        string[] skillNames = [.. dryRunReports
            .SelectMany(ExtractOutdatedSkillNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (skillNames.Length == 0)
        {
            return;
        }

        reporter.Info("Would update skills in repo skills directories:");
        foreach (string skillName in skillNames)
        {
            reporter.Info($"  {skillName}");
        }
    }

    void ReportSkillUpdateDryRunOutputs(
        IReadOnlyList<string> skillsDirectories,
        List<CommandReport> dryRunReports,
        SkillUpdateOutputKind outputKind)
    {
        for (int index = 0; index < Math.Min(skillsDirectories.Count, dryRunReports.Count); index++)
        {
            var dryRunReport = dryRunReports[index];
            if (dryRunReport.Success && CountOutdatedSkills(dryRunReport) == 0)
            {
                continue;
            }

            ReportSkillUpdateOutput(skillsDirectories[index], dryRunReport, outputKind);
        }
    }

    void ReportSkillUpdateOutput(string skillsDirectory, CommandReport updateReport, SkillUpdateOutputKind outputKind)
    {
        reporter.Info(outputKind switch
        {
            SkillUpdateOutputKind.Preflight => $"Skills in {skillsDirectory} with update available:",
            SkillUpdateOutputKind.Update => $"Updated repo-local skills in {skillsDirectory}:",
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind), outputKind, "Unsupported skill update output kind.")
        });
        if (string.IsNullOrWhiteSpace(updateReport.StandardOutput) && string.IsNullOrWhiteSpace(updateReport.StandardError))
        {
            reporter.Info("(no output)");
            return;
        }

        if (!string.IsNullOrWhiteSpace(updateReport.StandardOutput))
        {
            reporter.Info(updateReport.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(updateReport.StandardError))
        {
            reporter.Warning(updateReport.StandardError.TrimEnd());
        }
    }

    static bool ReportsAvailableSkillUpdates(CommandReport updateReport)
        => CountOutdatedSkills(updateReport) > 0;

    static int CountDistinctOutdatedSkills(IReadOnlyList<CommandReport> updateReports)
        => updateReports
            .SelectMany(ExtractOutdatedSkillNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    static int CountOutdatedSkills(CommandReport updateReport)
    {
        if (!updateReport.Success)
        {
            return 0;
        }

        string output = $"{updateReport.StandardOutput}\n{updateReport.StandardError}";
        if (string.IsNullOrWhiteSpace(output))
        {
            return 0;
        }

        string normalized = output.Trim();
        string[] noUpdateMarkers =
        [
            "No installed skills found.",
            "No updates",
            "No skill updates",
            "No skills need updating",
            "No updates available",
            "already up to date",
            "already up-to-date",
            "up to date",
            "up-to-date"
        ];
        if (noUpdateMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        return ExtractOutdatedSkillNames(updateReport).Count;
    }

    static IReadOnlyList<string> ExtractOutdatedSkillNames(CommandReport updateReport)
    {
        if (!updateReport.Success)
        {
            return [];
        }

        string output = $"{updateReport.StandardOutput}\n{updateReport.StandardError}";
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        string normalized = output.Trim();
        string[] noUpdateMarkers =
        [
            "No installed skills found.",
            "No updates",
            "No skill updates",
            "No skills need updating",
            "No updates available",
            "already up to date",
            "already up-to-date",
            "up to date",
            "up-to-date"
        ];
        if (noUpdateMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        string[] ignoredLineFragments =
        [
            "checking",
            "dry run",
            "dry-run"
        ];
        string[] updateLines = [.. normalized
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !IsIgnoredOutdatedSkillLine(line, ignoredLineFragments))
            .Select(NormalizeOutdatedSkillLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))];
        return updateLines.Length == 0 ? [normalized] : updateLines;
    }

    static bool IsIgnoredOutdatedSkillLine(string line, IReadOnlyList<string> ignoredLineFragments)
    {
        string normalized = TrimListMarker(line);
        if (ignoredLineFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return normalized.Length > 0
            && char.IsDigit(normalized[0])
            && (normalized.Contains("update(s) available", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("updates available", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("update available", StringComparison.OrdinalIgnoreCase));
    }

    static string NormalizeOutdatedSkillLine(string line)
    {
        string normalized = TrimListMarker(line);
        string[] prefixes =
        [
            "Would update skill ",
            "Would update ",
            "Update available for skill ",
            "Update available for ",
            "Outdated skill ",
            "Outdated "
        ];

        foreach (string prefix in prefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return ExtractSkillNameFromUpdateLine(normalized[prefix.Length..]);
            }
        }

        return ExtractSkillNameFromUpdateLine(normalized);
    }

    static string ExtractSkillNameFromUpdateLine(string line)
    {
        string normalized = TrimListMarker(line).TrimEnd('.');
        int metadataIndex = normalized.IndexOf(" (", StringComparison.Ordinal);
        return metadataIndex > 0
            ? normalized[..metadataIndex].Trim()
            : normalized;
    }

    static string TrimListMarker(string line)
    {
        string normalized = line.Trim();
        return normalized.Length > 0 && normalized[0] is '•' or '-' or '*'
            ? normalized[1..].Trim()
            : normalized;
    }

    enum SkillUpdateOutputKind
    {
        Preflight,
        Update
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
