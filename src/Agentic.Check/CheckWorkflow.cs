using System.Text.Json;

namespace Agentic.Check;

sealed record SkillUpdateCandidate(string Name, string SourceRepo);

sealed record SkillUpdateDisplayItem(string Name, string SourceRepo, string Plugin);

sealed record DirectoryValidationResult(
    bool Success,
    string Directory,
    IReadOnlyList<string> Actions,
    string? Error)
{
    public static DirectoryValidationResult Valid(string directory, IReadOnlyList<string> actions)
        => new(true, directory, actions, null);

    public static DirectoryValidationResult Invalid(string directory, string? error = null)
        => new(false, directory, [], error);
}

sealed class CheckWorkflow(
    ICommandRunner commandRunner,
    IUserPrompts prompts,
    IReporter reporter,
    IDirectiveSource? directiveSource = null,
    ISourceVersionResolver? sourceVersionResolver = null,
    IReadOnlyList<SkillManifestEntry>? skillManifest = null,
    DnaInstaller? dnaInstaller = null,
    Func<string, string?>? readEnvironment = null)
{
    static readonly JsonSerializerOptions ReportSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<CheckRunResult> RunAsync(AgenticCheckOptions options, CancellationToken cancellationToken)
    {
        AgenticCheckReport report = new()
        {
            DryRun = options.DryRun
        };

        try
        {
            return await RunCoreAsync(options, report, cancellationToken).ConfigureAwait(false);
        }
        catch (GitHubRateLimitException exception)
        {
            reporter.Error(exception.Message);
            const string incomplete = "The run is incomplete. Changes already applied have been kept; rerun Agentic.Check to finish.";
            reporter.Warning(incomplete);
            report.Warnings.Add(exception.Message);
            report.Warnings.Add(incomplete);
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(1, report);
        }
    }

    async Task<CheckRunResult> RunCoreAsync(AgenticCheckOptions options, AgenticCheckReport report, CancellationToken cancellationToken)
    {

        if (options.PreviewSourceRef is not null
            && (!options.Preview || !GitHubSourceVersionResolver.IsValidPreviewRef(options.PreviewSourceRef)))
        {
            reporter.Error("--preview-source-ref requires --preview and a valid branch reference or commit SHA.");
            return new CheckRunResult(2, report);
        }

        if (!string.IsNullOrWhiteSpace(options.SkillsDirectory) && !string.IsNullOrWhiteSpace(options.Agents))
        {
            reporter.Error("Specify no more than one of --skills-dir and --agents.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var agentValidation = AgentSkillRegistry.ValidateAgentsValue(options.Agents);
        if (!agentValidation.Success)
        {
            reporter.Error(agentValidation.Error ?? "Invalid --agents value.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var targetDirectoryResolution = ValidateTargetDirectory(options.TargetDirectory);
        report.TargetDirectory = targetDirectoryResolution.Directory;
        report.Actions.AddRange(targetDirectoryResolution.Actions);
        if (!targetDirectoryResolution.Success)
        {
            reporter.Error(targetDirectoryResolution.Error ?? "Invalid target directory.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var skillsDirectoryValidation = ValidateSkillsDirectory(options.SkillsDirectory, targetDirectoryResolution.Directory);
        if (!skillsDirectoryValidation.Success)
        {
            reporter.Error(skillsDirectoryValidation.Error ?? "Invalid --skills-dir value.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var prerequisites = await new PrerequisiteChecker(commandRunner)
            .CheckAsync(report.TargetDirectory, cancellationToken)
            .ConfigureAwait(false);
        report.Prerequisites.AddRange(prerequisites.Checks);

        if (!prerequisites.IsSuccessful(options.DryRun))
        {
            reporter.Error("GitHub CLI is missing or too old. Update GitHub CLI and confirm `gh skill --help` or `gh skills --help` works.");
            await prompts.WaitForHelpKeyAsync("https://cli.github.com/", "how to install GitHub CLI", cancellationToken).ConfigureAwait(false);
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        string targetDirectory = report.TargetDirectory;
        report.RepoRoot = targetDirectory;

        var (authentication, authenticationError) = await GitHubAuthentication.ConnectAsync(commandRunner, targetDirectory, cancellationToken, readEnvironment).ConfigureAwait(false);
        report.Prerequisites.Add(new("GitHub access", authentication is not null, null, null, string.Empty, authenticationError ?? string.Empty));
        if (authentication is null)
        {
            reporter.Error(authenticationError!);
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }
        using var githubClient = authentication.CreateHttpClient();
        var githubRunner = authentication.CreateSkillRunner();

        IReadOnlyList<string> skillsDirectories;
        bool manageClaudeFile;
        string targetAgents;
        if (!string.IsNullOrWhiteSpace(skillsDirectoryValidation.Directory))
        {
            string skillsDirectory = skillsDirectoryValidation.Directory;
            skillsDirectories = [skillsDirectory];
            manageClaudeFile = IsClaudeSkillsDirectory(skillsDirectory);
            targetAgents = "custom skills directory";
        }
        else
        {
            targetAgents = !string.IsNullOrWhiteSpace(options.Agents)
                ? options.Agents
                : AgentSkillRegistry.DefaultAgents;
            var directoryResolution = AgentSkillRegistry.ResolveProjectDirectories(options.Agents, targetDirectory);
            if (!directoryResolution.Success)
            {
                reporter.Error(directoryResolution.Error ?? "Could not resolve agent skill directories.");
                await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
                return new CheckRunResult(2, report);
            }

            skillsDirectories = directoryResolution.Directories;
            manageClaudeFile = directoryResolution.ManageClaude;
        }

        StackDetectionResult? stack = null;
        DirectivePlanResult? directivePlan = null;
        IReadOnlyList<SkillManifestEntry> recommended = [];
        IReadOnlyList<SkillManifestEntry> missing = [];
        IReadOnlyList<SkillUpdateCandidate> skillUpdates = [];
        var directiveCacheSettings = DirectiveCacheSettings.FromEnvironment();
        report.Warnings.AddRange(directiveCacheSettings.ConfigurationWarnings);
        var sourceMode = options.Preview ? SourceVersionMode.Preview : SourceVersionMode.Stable;

        string firstSkillsDirectory = skillsDirectories[0];
        report.SkillsDirectory = firstSkillsDirectory;
        report.SkillsDirectories.AddRange(skillsDirectories);
        IReadOnlyList<SkillManifestEntry> manifest;
        try
        {
            manifest = await AddSourceVersionInfoAsync(
                skillManifest ?? (options.Preview ? StaticSkillManifest.Preview : StaticSkillManifest.All),
                sourceMode,
                directiveCacheSettings,
                options.PreviewSourceRef,
                githubClient,
                cancellationToken).ConfigureAwait(false);
        }
        catch (DirectiveException exception) when (options.PreviewSourceRef is not null)
        {
            reporter.Error(exception.Message);
            return new CheckRunResult(2, report);
        }

        var sourceVersion = manifest.FirstOrDefault(skill => skill.SourceRepo == CompanionDependency.SourceRepo);
        var contentSource = directiveSource ?? new GitHubDirectiveSource(
            httpClient: githubClient, cacheSettings: directiveCacheSettings, reporter: reporter, sourceVersionMode: sourceMode,
            resolvedVersion: sourceVersion?.ResolvedSource);
        DirectiveInstaller directiveInstaller = new(contentSource, reporter);
        CompanionSourceVersionReader companionVersions = new(contentSource);
        CompanionInstaller companionInstaller = new(githubRunner);
        var shorthandInstaller = dnaInstaller ?? new DnaInstaller(githubRunner);
        Dictionary<string, bool> prerequisiteResults = new(StringComparer.Ordinal);

        async Task<bool> EnsureCompanionAsync(string sourceRef, ToolVersion? localRequirement, bool restoreOnly)
        {
            try
            {
                var requirement = localRequirement ?? await companionVersions.ReadAsync(sourceRef, cancellationToken).ConfigureAwait(false);
                string key = requirement.Minimum + (restoreOnly ? ":restore" : ":update");
                if (prerequisiteResults.TryGetValue(key, out bool previous))
                {
                    return previous;
                }

                CompanionReport? result = null;
                await reporter.RunProgressAsync(ActionOutputFormatter.ProgressIndent, 1, async advance =>
                {
                    result = await companionInstaller.EnsureAsync(targetDirectory, requirement, options.Preview, restoreOnly, options.DryRun, cancellationToken).ConfigureAwait(false);
                    advance();
                }, cancellationToken).ConfigureAwait(false);
                var completed = result!;
                report.Companion = completed;
                string description = $"{(options.DryRun ? "Would " : "")}{completed.Action} {CompanionDependency.PackageId}: installed {completed.InstalledVersion ?? "absent"}, required {requirement.Minimum}, pattern {completed.Pattern}";
                if (completed.ResolvedVersion is not null)
                {
                    description += $", resolved {completed.ResolvedVersion}";
                }

                report.Actions.Add(description);
                if (completed.Success)
                {
                    reporter.Success(ActionOutputFormatter.FormatLine(options.DryRun ? "Would prepare tool" : "Prepared tool", description));
                }
                else
                {
                    reporter.Error($"{description}: {completed.Error}. Dependent actions skipped.{(completed.Changed ? " The tool manifest changed before validation failed." : "")}");
                }

                prerequisiteResults[key] = completed.Success;
                return completed.Success;
            }
            catch (Exception exception) when (exception is DirectiveException or FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException or JsonException)
            {
                reporter.Error($"Cannot prepare {CompanionDependency.PackageId}: {exception.Message}. Dependent actions skipped.");
                report.Companion = new(CompanionInstaller.ManifestPath(targetDirectory), null, "unknown", "unknown", "resolve", false, null, false, exception.Message);
                return false;
            }
        }

        await reporter.RunProgressAsync(
            "Scanning target directory",
            3,
            async advance =>
            {
                var detectedStack = StackDetector.Detect(targetDirectory);
                stack = detectedStack;
                report.Technologies.AddRange(detectedStack.Technologies.Order(StringComparer.OrdinalIgnoreCase));
                report.InstallGates.AddRange(detectedStack.InstallGates);
                report.Warnings.AddRange(detectedStack.Warnings);
                advance();

                directivePlan = await directiveInstaller
                    .PlanAsync(targetDirectory, detectedStack, manageClaudeFile, cancellationToken)
                    .ConfigureAwait(false);
                advance();

                recommended = SkillPlanner.Plan(manifest, detectedStack);
                report.RecommendedSkills.AddRange(recommended.Select(SkillReportItem.FromManifestEntry));

                missing = SkillInstaller.FindMissing(recommended, skillsDirectories);
                report.MissingSkills.AddRange(missing.Select(SkillReportItem.FromManifestEntry));

                if (!options.Preview)
                {
                    await RunSkillUpdateDryRunAsync(githubRunner, skillsDirectories, targetDirectory, report, cancellationToken).ConfigureAwait(false);
                    skillUpdates = ExtractDistinctSkillUpdates(report.SkillUpdateDryRuns, recommended);
                    report.OutdatedSkills = skillUpdates.Count;
                }

                advance();
            },
            cancellationToken).ConfigureAwait(false);

        stack = stack ?? throw new InvalidOperationException("Target directory scan did not detect a stack.");
        directivePlan = directivePlan ?? throw new InvalidOperationException("Target directory scan did not plan directives.");

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

        reporter.Summary(targetDirectory, stack.Technologies, stack.InstallGates, targetAgents, skillsDirectories, directiveSummary, recommended.Count, missing.Count, report.OutdatedSkills, sourceMode);
        reporter.Info($"GitHub cache duration: {directiveCacheSettings.DurationDescription}");

        foreach (string warning in report.Warnings)
        {
            reporter.Warning(warning);
        }

        var recommendedDirectives = directivePlan.SelectableDirectives;
        var stableSwitchSkills = FindStableSwitchSkillActions(options.Preview, recommended, skillsDirectories);
        var recommendedSkillActions = options.Preview
            ? [.. recommended.Select(skill => skill with { RecommendationAction = missing.Contains(skill) ? "install" : "re-install" })]
            : BuildStableSkillActions(recommended, missing, stableSwitchSkills);
        ToolVersion? repairRequirement = null;
        string? installedCompanion = null;
        bool restoreOnly = false;
        string? repairError = null;
        try
        {
            installedCompanion = CompanionInstaller.InstalledVersion(targetDirectory);
            List<string> installedConsumers = [];
            foreach (var directive in directivePlan.Directives.Where(d => CompanionDependency.ForDirective(d.Name).Count > 0))
            {
                string start = $"<!-- dotnet-agentic-engineering:{directive.Name}:start -->";
                string end = $"<!-- dotnet-agentic-engineering:{directive.Name}:end -->";
                int from = directivePlan.AgentsContent.IndexOf(start, StringComparison.Ordinal);
                int to = directivePlan.AgentsContent.IndexOf(end, StringComparison.Ordinal);
                if (from >= 0 && to > from)
                {
                    string installed = directivePlan.AgentsContent[from..to];
                    if (CompanionDependency.Invocations(installed).Count > 0)
                    {
                        installedConsumers.Add(installed);
                    }
                }
            }

            foreach (var skill in manifest.Where(skill => skill.Dependencies.Contains(CompanionDependency.Identity)))
            {
                foreach (string directory in skillsDirectories)
                {
                    string path = Path.Combine(directory, skill.LocalFolder, "SKILL.md");
                    if (File.Exists(path))
                    {
                        installedConsumers.Add(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                    }
                }
            }

            if (installedConsumers.Count > 0)
            {
                repairRequirement = CompanionDependency.ReadLocalRequirement(installedConsumers);
                string? installed = installedCompanion;
                restoreOnly = installed is not null && ToolVersion.Parse(installed).Satisfies(repairRequirement);
                if (restoreOnly)
                {
                    var available = await githubRunner.RunAsync("dotnet", ["tool", "run", "agentic", "--", "--version"], targetDirectory, cancellationToken).ConfigureAwait(false);
                    if (available.Success && available.StandardOutput.Trim().Split('+')[0] == installed!.Split('+')[0])
                    {
                        repairRequirement = null;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
        {
            repairError = exception.Message;
        }

        bool hasDependentRecommendations = recommendedDirectives.Any(d => CompanionDependency.ForDirective(d.Name).Count > 0)
            || recommendedSkillActions.Any(skill => skill.Dependencies.Contains(CompanionDependency.Identity));
        if (hasDependentRecommendations || repairRequirement is not null || repairError is not null)
        {
            string status;
            try
            {
                var plannedRequirement = hasDependentRecommendations
                    ? await companionVersions.ReadAsync(recommendedDirectives.FirstOrDefault(d => CompanionDependency.ForDirective(d.Name).Count > 0)?.SourceRef
                        ?? recommendedSkillActions.First(skill => skill.Dependencies.Contains(CompanionDependency.Identity)).ResolvedSourceRef, cancellationToken).ConfigureAwait(false)
                    : repairRequirement;
                status = $"required {plannedRequirement?.Minimum ?? "unknown"}";
                if (installedCompanion is not null)
                    status = $"currently {installedCompanion}; {status}";
            }
            catch (Exception exception) when (exception is DirectiveException or FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
            {
                // Selection may exclude this action. Report the actionable failure only if it runs.
                status = "version unavailable: " + exception.Message;
            }

            string action = !hasDependentRecommendations && repairError is not null ? "repair"
                : !hasDependentRecommendations && restoreOnly ? "restore"
                : installedCompanion is null ? "install" : "update";
            recommendedSkillActions = [.. recommendedSkillActions, CompanionDependency.Action(action) with
            {
                Version = status,
                IsRequiredToolRepair = repairRequirement is not null || repairError is not null
            }];
        }

        DnaInstallation? dnaInstallation = null;
        if (recommendedSkillActions.Any(skill => skill.IsCompanion) || installedCompanion is not null)
        {
            // An already installed companion satisfies this dependency even when there is
            // no companion action. Existing repos can still opt into or update the shorthand.
            dnaInstallation = await shorthandInstaller.InspectAsync(targetDirectory, cancellationToken).ConfigureAwait(false);
            recommendedSkillActions = [.. recommendedSkillActions, DnaInstaller.Action(dnaInstallation)];
        }

        IReadOnlyList<DirectivePlanItem> selectedDirectives = [];
        IReadOnlyList<SkillManifestEntry> selectedSkills = [];
        if (!options.DryRun && !options.Preview)
        {
            ReportUpToDateItems(directivePlan.Directives, recommended, missing, skillUpdates, stableSwitchSkills);
        }

        if (recommendedDirectives.Count > 0 || recommendedSkillActions.Count > 0)
        {
            if (options.DryRun || options.Yes)
            {
                selectedDirectives = recommendedDirectives;
                selectedSkills = recommendedSkillActions;
            }
            else
            {
                var selection = await prompts
                    .SelectRecommendationsAsync(recommendedDirectives, recommendedSkillActions, targetDirectory, skillsDirectories, cancellationToken)
                    .ConfigureAwait(false);
                selectedDirectives = selection.SelectedDirectives;
                selectedSkills = selection.SelectedSkills;
            }
        }

        // The same selection graph closes dependencies for interactive, --yes, dry-run, and updates.
        var closedSelection = CloseDependencies(selectedDirectives, selectedSkills, recommendedSkillActions);
        selectedSkills = closedSelection.SelectedSkills;

        if (!options.DryRun && (recommendedDirectives.Count > 0 || recommendedSkillActions.Count > 0))
        {
            ReportSelectedActions(selectedDirectives.Count + selectedSkills.Count);
        }

        if (selectedSkills.Any(skill => skill.IsCompanion))
        {
            bool selectedConsumer = selectedDirectives.Any(d => CompanionDependency.ForDirective(d.Name).Count > 0)
                || selectedSkills.Any(skill => !skill.IsDna && skill.Dependencies.Contains(CompanionDependency.Identity));
            string selectedRef = selectedDirectives.FirstOrDefault(d => CompanionDependency.ForDirective(d.Name).Count > 0)?.SourceRef
                ?? selectedSkills.FirstOrDefault(skill => !skill.IsDna && skill.Dependencies.Contains(CompanionDependency.Identity))?.ResolvedSourceRef
                ?? sourceVersion?.ResolvedSourceRef ?? string.Empty;
            if (repairError is not null && !selectedConsumer)
            {
                reporter.Error(repairError);
                report.Companion = new(CompanionInstaller.ManifestPath(targetDirectory), null, "unknown", "unknown", "repair", false, null, false, repairError);
            }
            else if (!await EnsureCompanionAsync(selectedRef, selectedConsumer ? null : repairRequirement, !selectedConsumer && restoreOnly).ConfigureAwait(false))
            {
                var failedItems = RecommendationSelectionPrompt.BuildItems(selectedDirectives, selectedSkills);
                RecommendationSelectionState failedSelection = new(failedItems);
                // Deselecting a failed prerequisite propagates through every transitive consumer.
                failedSelection.DeselectWithDependents(RecommendationSelectionState.FormatSkillKey(string.Empty, CompanionDependency.PackageId));
                selectedDirectives = failedSelection.SelectedDirectives;
                selectedSkills = failedSelection.SelectedSkills;
            }
        }

        if (report.Companion?.Success == false)
            selectedSkills = [.. selectedSkills.Where(skill => !skill.IsDna)];

        if (selectedSkills.Any(skill => skill.IsDna))
        {
            var dna = await shorthandInstaller.EnsureAsync(dnaInstallation!, targetDirectory, options.DryRun, options.Yes, prompts, cancellationToken).ConfigureAwait(false);
            report.Dna = dna;
            string description = $"{(options.DryRun ? "Would " : string.Empty)}{dna.Action} {DnaInstaller.PackageId} globally (`dna` shorthand for `dotnet agentic`)";
            if (dna.Skipped)
                description = dna.Error!;
            report.Actions.Add(description);
            foreach (string conflict in dna.Conflicts)
            {
                string warning = $"The dna command at {conflict} may hide the shorthand.";
                report.Warnings.Add(warning);
                reporter.Warning(warning);
            }
            if (dna.Skipped)
            {
                reporter.Warning(description);
            }
            else if (!dna.Success)
            {
                reporter.Error(dna.Error!);
            }
            else
            {
                reporter.Success(description);
                if (!options.DryRun)
                    reporter.Info($"Global launcher: {shorthandInstaller.CommandPath}. Its directory must be on PATH to use dna.");
            }
        }

        selectedSkills = [.. selectedSkills.Where(skill => !skill.IsCompanion && !skill.IsDna)];

        var directiveResult = await directiveInstaller
            .ApplyAsync(directivePlan, selectedDirectives.Select(directive => directive.Name), options.DryRun, cancellationToken)
            .ConfigureAwait(false);
        report.Actions.AddRange(directiveResult.Actions);
        if (!directiveResult.Success)
        {
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        if (!options.DryRun)
        {
            ReportDirectiveApplyActions(selectedDirectives);
        }

        if (options.DryRun)
        {
            ReportDirectiveDryRunActions(selectedDirectives, report.AgentsFile);
            ReportSkillInstallDryRunActions(selectedSkills);

            foreach (var skill in selectedSkills)
            {
                foreach (string skillsDirectory in skillsDirectories.Where(directory => options.Preview || skill.ForceInstall || SkillInstaller.IsMissing(skill, directory)))
                {
                    report.Actions.Add($"Would install {skill.SourceSpec} {skill.InstallArg} into {skillsDirectory}.");
                }
            }

            if (!options.Preview)
            {
                ReportSkillUpdateDryRunActions(report.SkillUpdateDryRuns, recommended);
            }

            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(report.Companion?.Success == false || report.Dna?.Success == false ? 1 : 0, report);
        }

        if (selectedSkills.Count > 0)
        {
            SkillInstaller skillInstaller = new(githubRunner, reporter);
            var firstDirectoryInstallSkills = options.Preview
                ? selectedSkills
                : [.. selectedSkills.Where(skill => skill.ForceInstall || SkillInstaller.IsMissing(skill, firstSkillsDirectory))];
            int installOperationCount = CountSkillInstallOperations(
                selectedSkills,
                firstDirectoryInstallSkills,
                [.. skillsDirectories.Skip(1)],
                options.Preview);
            await reporter.RunProgressAsync(
                ActionOutputFormatter.ProgressIndent,
                installOperationCount,
                async advance =>
                {
                    _ = await skillInstaller
                        .InstallAsync(
                            firstDirectoryInstallSkills,
                            firstSkillsDirectory,
                            targetDirectory,
                            cancellationToken,
                            advance,
                            reportPreviewChangeStatus: options.Preview,
                            reportResult: report.InstallResults.Add)
                        .ConfigureAwait(false);

                    if (skillsDirectories.Count > 1)
                    {
                        SkillManifestEntry[] copyableSkills = [.. selectedSkills
                            .Where(skill => options.Preview || skill.ForceInstall || !SkillInstaller.IsMissing(skill, firstSkillsDirectory))];
                        report.SkillCopyResults.AddRange(skillInstaller.CopyInstalledSkills(
                            copyableSkills,
                            firstSkillsDirectory,
                            [.. skillsDirectories.Skip(1)],
                            targetDirectory,
                            advance,
                            overwriteExisting: options.Preview,
                            reportPreviewChangeStatus: options.Preview));
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        static int CountSkillInstallOperations(
            IReadOnlyList<SkillManifestEntry> selectedSkills,
            IReadOnlyList<SkillManifestEntry> firstDirectoryInstallSkills,
            IReadOnlyList<string> targetSkillsDirectories,
            bool overwriteCopies)
        {
            int copyOperations = 0;
            foreach (string targetSkillsDirectory in targetSkillsDirectories)
            {
                copyOperations += overwriteCopies
                    ? selectedSkills.Count
                    : selectedSkills.Count(skill => skill.ForceInstall || SkillInstaller.IsMissing(skill, targetSkillsDirectory));
            }

            return firstDirectoryInstallSkills.Count + copyOperations;
        }

        if (!options.Preview)
        {
            await RunSkillUpdateAsync(githubRunner, options, skillsDirectories, targetDirectory, report, skillUpdates, manifest, async updateSkills =>
            {
                var closure = CloseDependencies([], updateSkills, [.. manifest, CompanionDependency.Action()]);
                return !closure.SelectedSkills.Any(skill => skill.IsCompanion)
                    || await EnsureCompanionAsync(closure.SelectedSkills.First(skill => skill.Dependencies.Contains(CompanionDependency.Identity)).ResolvedSourceRef, null, false).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        int exitCode = report.Companion?.Success == false || report.Dna?.Success == false || report.SkillUpdates.Any(result => !result.Success) || report.InstallResults.Any(result => !result.Success) || report.SkillCopyResults.Any(result => !result.Success) ? 1 : 0;
        await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
        return new CheckRunResult(exitCode, report);
    }

    async Task<IReadOnlyList<SkillManifestEntry>> AddSourceVersionInfoAsync(
        IReadOnlyList<SkillManifestEntry> manifest,
        SourceVersionMode sourceVersionMode,
        DirectiveCacheSettings cacheSettings,
        string? previewSourceRef,
        HttpClient githubClient,
        CancellationToken cancellationToken)
    {
        var resolver = sourceVersionResolver ?? new GitHubSourceVersionResolver(githubClient, reporter, previewSourceRef);
        IReadOnlyDictionary<string, SourceVersionInfo> versions;
        try
        {
            versions = await resolver
                .ResolveVersionsAsync(manifest.Select(skill => skill.SourceRepo), sourceVersionMode, cacheSettings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DirectiveException exception) when (previewSourceRef is null)
        {
            reporter.Warning($"Could not resolve skill source versions: {exception.Message}");
            return manifest;
        }

        return [.. manifest.Select(skill => versions.TryGetValue(skill.SourceRepo, out var version)
            ? skill with
            {
                SourceRef = sourceVersionMode == SourceVersionMode.Preview ? version.ContentRef : string.Empty,
                Version = version.Display,
                ResolvedSource = version
            }
            : skill)];
    }

    static DirectoryValidationResult ValidateTargetDirectory(string targetDirectory)
    {
        var pathValidation = TryGetFullPath(targetDirectory, "target directory");
        if (!pathValidation.Success)
        {
            return DirectoryValidationResult.Invalid(targetDirectory, pathValidation.Error);
        }

        string fullTargetDirectory = pathValidation.Directory;
        return File.Exists(fullTargetDirectory)
            ? DirectoryValidationResult.Invalid(
                fullTargetDirectory,
                $"Invalid target directory: {fullTargetDirectory} is a file.")
            : Directory.Exists(fullTargetDirectory)
            ? DirectoryValidationResult.Valid(fullTargetDirectory, [])
            : DirectoryValidationResult.Invalid(
            fullTargetDirectory,
            $"Target directory does not exist: {fullTargetDirectory}.");
    }

    static DirectoryValidationResult ValidateSkillsDirectory(string? skillsDirectory, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(skillsDirectory))
        {
            return DirectoryValidationResult.Valid(string.Empty, []);
        }

        if (Path.IsPathRooted(skillsDirectory))
        {
            return DirectoryValidationResult.Invalid(
                skillsDirectory,
                $"Invalid skills directory: {skillsDirectory} must be relative to the target directory.");
        }

        var pathValidation = TryGetFullPath(Path.Combine(targetDirectory, skillsDirectory), "skills directory");
        if (!pathValidation.Success)
        {
            return DirectoryValidationResult.Invalid(skillsDirectory, pathValidation.Error);
        }

        string fullSkillsDirectory = pathValidation.Directory;
        return !IsPathBelowDirectory(targetDirectory, fullSkillsDirectory)
            ? DirectoryValidationResult.Invalid(
                fullSkillsDirectory,
                $"Invalid skills directory: {skillsDirectory} must resolve below the target directory.")
            : File.Exists(fullSkillsDirectory)
            ? DirectoryValidationResult.Invalid(fullSkillsDirectory, $"Invalid skills directory: {fullSkillsDirectory} is a file.")
            : Directory.Exists(fullSkillsDirectory)
            ? DirectoryValidationResult.Valid(fullSkillsDirectory, [])
            : DirectoryValidationResult.Invalid(fullSkillsDirectory, $"Skills directory does not exist: {fullSkillsDirectory}.");
    }

    static bool IsPathBelowDirectory(string parentDirectory, string childPath)
    {
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDirectory));
        string child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(childPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return child.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
    }

    static DirectoryValidationResult TryGetFullPath(string path, string parameterName)
    {
        try
        {
            return DirectoryValidationResult.Valid(Path.GetFullPath(path), []);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DirectoryValidationResult.Invalid(path, $"Invalid {parameterName}: {exception.Message}");
        }
    }

    async Task RunSkillUpdateAsync(
        ICommandRunner githubRunner,
        AgenticCheckOptions options,
        IReadOnlyList<string> skillsDirectories,
        string repoRoot,
        AgenticCheckReport report,
        IReadOnlyList<SkillUpdateCandidate> skillUpdates,
        IReadOnlyList<SkillManifestEntry> manifest,
        Func<IReadOnlyList<SkillManifestEntry>, Task<bool>> ensurePrerequisites,
        CancellationToken cancellationToken)
    {
        if (report.SkillUpdateDryRuns.All(result => !result.Success))
        {
            return;
        }

        if (skillUpdates.Count == 0)
        {
            report.Actions.Add("No target-local skill updates found.");
            return;
        }

        ReportSkillUpdates(skillUpdates, recommendedSkills: manifest);

        bool update = options.Yes || await prompts.ConfirmAsync("Update these skill(s)?", false, cancellationToken)
            .ConfigureAwait(false);

        if (!update)
        {
            report.Actions.Add("Skipped target-local skill updates.");
            return;
        }

        List<(string SkillsDirectory, CommandReport Report)> failures = [];
        bool skippedPrerequisite = false;
        await reporter.RunProgressAsync(
            "Updating skills",
            skillsDirectories.Count,
            async advance =>
            {
                foreach (string skillsDirectory in skillsDirectories)
                {
                    var directoryUpdates = skillUpdates.SelectMany(update => FindMatchingManifestEntries(update, manifest))
                        .Where(skill => !SkillInstaller.IsMissing(skill, skillsDirectory)).DistinctBy(skill => skill.Key).ToArray();
                    if (!await ensurePrerequisites(directoryUpdates).ConfigureAwait(false))
                    {
                        skippedPrerequisite = true;
                        reporter.Error($"Skipped skill update --all in {skillsDirectory}: companion prerequisite failed.");
                        advance();
                        continue;
                    }

                    var updateResult = await githubRunner.RunAsync(
                        "gh",
                        ["skill", "update", "--dir", skillsDirectory, "--all"],
                        repoRoot,
                        cancellationToken).ConfigureAwait(false);

                    report.Actions.Add($"Ran gh skill update --dir {skillsDirectory} --all.");
                    var updateReport = CommandReport.FromCommandResult(updateResult);
                    report.SkillUpdates.Add(updateReport);
                    report.SkillUpdate ??= updateReport;
                    if (!updateReport.Success)
                    {
                        failures.Add((skillsDirectory, updateReport));
                    }

                    advance();
                }
            },
            cancellationToken).ConfigureAwait(false);

        foreach (var (skillsDirectory, updateReport) in failures)
        {
            reporter.Error($"Failed to update skills in {skillsDirectory}.");
            ReportCommandOutput(updateReport);
        }

        if (failures.Count == 0 && !skippedPrerequisite)
        {
            reporter.Success(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Updated {skillUpdates.Count} skill(s) successfully."));
        }
    }

    async Task RunSkillUpdateDryRunAsync(
        ICommandRunner githubRunner,
        IReadOnlyList<string> skillsDirectories,
        string repoRoot,
        AgenticCheckReport report,
        CancellationToken cancellationToken)
    {
        foreach (string skillsDirectory in skillsDirectories)
        {
            var dryRunResult = await githubRunner.RunAsync(
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
                reporter.Warning($"Could not check target-local skills for updates in {skillsDirectory}.");
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

        ReportSectionHeader(header);
        foreach (string directiveName in directiveNames)
        {
            reporter.Plain($"  {directiveName}");
        }
    }

    void ReportSkillInstallDryRunActions(IReadOnlyList<SkillManifestEntry> selectedSkills)
    {
        if (selectedSkills.Count == 0)
        {
            return;
        }

        ReportSectionHeader("Would install skills into skills directories:");
        ReportSkillGroups(selectedSkills, skill => $"      {skill.LocalFolder}", ItemStyle.Plain);
    }

    void ReportSkillUpdateDryRunActions(
        IReadOnlyList<CommandReport> dryRunReports,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
    {
        SkillUpdateCandidate[] skillUpdates = [.. ExtractDistinctSkillUpdates(dryRunReports, recommendedSkills)];
        if (skillUpdates.Length == 0)
        {
            return;
        }

        ReportSectionHeader("Would update skills in skills directories:");
        ReportSkillUpdateGroups(skillUpdates, recommendedSkills);
    }

    void ReportUpToDateItems(
        IReadOnlyList<DirectivePlanItem> directives,
        IReadOnlyList<SkillManifestEntry> recommendedSkills,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        IReadOnlyList<SkillUpdateCandidate> skillUpdates,
        IReadOnlyList<SkillManifestEntry> stableSwitchSkills)
    {
        DirectivePlanItem[] currentDirectives = [.. directives
            .Where(directive => directive.Status == DirectiveStatuses.Current)
        ];
        var missingSkillKeys = missingSkills
            .Select(SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stableSwitchSkillKeys = stableSwitchSkills
            .Select(SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updateSkillKeys = skillUpdates
            .SelectMany(update => UpdateSkillKeys(update, recommendedSkills))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SkillManifestEntry[] upToDateSkills = [.. recommendedSkills
            .Where(skill => !missingSkillKeys.Contains(SkillKey(skill))
                && !stableSwitchSkillKeys.Contains(SkillKey(skill))
                && !updateSkillKeys.Contains(SkillKey(skill)))
        ];
        if (currentDirectives.Length == 0 && upToDateSkills.Length == 0)
        {
            return;
        }

        if (currentDirectives.Length > 0)
        {
            ReportSectionHeader("Up to date directives:");
            foreach (var directive in currentDirectives)
            {
                reporter.Success($"  ✓ {directive.Name}");
            }
        }

        if (upToDateSkills.Length == 0)
        {
            return;
        }

        ReportSectionHeader("Up to date skills:");
        ReportSkillGroups(
            upToDateSkills,
            skill => $"      ✓ {skill.LocalFolder}",
            ItemStyle.Success);
    }

    void ReportSkillUpdates(
        IReadOnlyList<SkillUpdateCandidate> skillUpdates,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
    {
        ReportSectionHeader(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Found {skillUpdates.Count} skill update(s) available:"));
        ReportSkillUpdateGroups(skillUpdates, recommendedSkills);
    }

    void ReportSelectedActions(int selectedActionCount)
    {
        reporter.Plain(string.Empty);
        reporter.Info(ActionOutputFormatter.FormatSelectionSummary(selectedActionCount));
        if (selectedActionCount == 0)
        {
            return;
        }

        reporter.Plain(string.Empty);
        reporter.Bold(ActionOutputFormatter.FormatHeader());
    }

    void ReportDirectiveApplyActions(IReadOnlyList<DirectivePlanItem> selectedDirectives)
    {
        foreach (var directive in selectedDirectives)
        {
            reporter.Success(ActionOutputFormatter.FormatLine(
                FormatDirectiveApplyAction(directive),
                directive.Name));
        }
    }

    static string FormatDirectiveApplyAction(DirectivePlanItem directive)
        => directive.Status == DirectiveStatuses.Outdated ? "Updated directive" : "Installed directive";

    void ReportSectionHeader(string header)
    {
        reporter.Plain(string.Empty);
        if (header.StartsWith("Would ", StringComparison.Ordinal))
        {
            reporter.Bold(header, ToolHeader.CheckColor);
            return;
        }

        if (header.StartsWith("Up to date ", StringComparison.Ordinal))
        {
            reporter.Bold(header, ToolHeader.AgenticColor);
            return;
        }

        reporter.Bold(header);
    }

    enum ItemStyle
    {
        Info,
        Plain,
        Success
    }

    void ReportSkillGroups(
        IEnumerable<SkillManifestEntry> skills,
        Func<SkillManifestEntry, string> formatSkill,
        ItemStyle itemStyle = ItemStyle.Info)
    {
        foreach (var sourceGroup in skills
            .GroupBy(skill => skill.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => SkillOrdering.GetSourceRepoOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            ReportHeader($"  {FormatSkillSourceHeader(sourceGroup.Key)}:");
            var pluginGroups = OrderPluginGroups(sourceGroup.Key, sourceGroup.GroupBy(skill => skill.Plugin, StringComparer.OrdinalIgnoreCase));
            bool showPluginHeaders = SkillGroupHeaderPolicy.ShouldShowPluginHeaders(sourceGroup.Key, pluginGroups.Select(group => group.Key));
            foreach (var pluginGroup in pluginGroups)
            {
                if (showPluginHeaders)
                {
                    ReportHeader($"    {FormatSkillPluginHeader(pluginGroup.Key)}:");
                }

                foreach (var skill in pluginGroup)
                {
                    ReportItem(formatSkill(skill), itemStyle);
                }
            }
        }
    }

    void ReportHeader(string message)
        => reporter.Bold(message, ToolHeader.AgenticColor);

    void ReportItem(string message, ItemStyle style)
    {
        if (style == ItemStyle.Plain)
        {
            reporter.Plain(message);
            return;
        }

        if (style == ItemStyle.Success)
        {
            reporter.Success(message);
            return;
        }

        reporter.Info(message);
    }

    void ReportSkillUpdateGroups(
        IReadOnlyList<SkillUpdateCandidate> skillUpdates,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
    {
        var displayItems = skillUpdates.Select(update => ResolveSkillUpdateDisplayItem(update, recommendedSkills));
        foreach (var sourceGroup in displayItems
            .GroupBy(update => update.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => SkillOrdering.GetSourceRepoOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            reporter.Bold($"  {FormatSkillSourceHeader(sourceGroup.Key)}:", ToolHeader.AgenticColor);
            var pluginGroups = OrderPluginGroups(sourceGroup.Key, sourceGroup.GroupBy(update => update.Plugin, StringComparer.OrdinalIgnoreCase));
            bool showPluginHeaders = SkillGroupHeaderPolicy.ShouldShowPluginHeaders(sourceGroup.Key, pluginGroups.Select(group => group.Key));
            foreach (var pluginGroup in pluginGroups)
            {
                if (showPluginHeaders)
                {
                    reporter.Bold($"    {FormatSkillPluginHeader(pluginGroup.Key)}:", ToolHeader.AgenticColor);
                }

                foreach (var update in pluginGroup)
                {
                    reporter.Plain($"      {update.Name}");
                }
            }
        }
    }

    void ReportCommandOutput(CommandReport updateReport)
    {
        if (!string.IsNullOrWhiteSpace(updateReport.StandardOutput))
        {
            reporter.Info(updateReport.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(updateReport.StandardError))
        {
            reporter.Error(updateReport.StandardError.TrimEnd());
        }
    }

    static IReadOnlyList<SkillUpdateCandidate> ExtractDistinctSkillUpdates(
        IReadOnlyList<CommandReport> updateReports,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
        => [.. updateReports
            .SelectMany(ExtractSkillUpdates)
            .GroupBy(update => CanonicalUpdateKey(update, recommendedSkills), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

    static string SkillKey(SkillManifestEntry skill)
        => SkillKey(skill.SourceRepo, skill.InstallArg);

    static string SkillKey(string sourceRepo, string skillName)
        => $"{sourceRepo}\n{skillName}";

    static IReadOnlyList<SkillManifestEntry> BuildStableSkillActions(
        IReadOnlyList<SkillManifestEntry> recommendedSkills,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        IReadOnlyList<SkillManifestEntry> stableSwitchSkills)
    {
        var missingSkillKeys = missingSkills
            .Select(SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stableSwitchSkillKeys = stableSwitchSkills
            .Select(SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. recommendedSkills
                .Where(skill => missingSkillKeys.Contains(SkillKey(skill)) || stableSwitchSkillKeys.Contains(SkillKey(skill)))
                .Select(skill => stableSwitchSkillKeys.Contains(SkillKey(skill))
                    ? skill with
                    {
                        RecommendationAction = "switch to stable",
                        ForceInstall = true
                    }
                    : skill)
        ];
    }

    static IReadOnlyList<SkillManifestEntry> FindStableSwitchSkillActions(
        bool preview,
        IReadOnlyList<SkillManifestEntry> recommendedSkills,
        IReadOnlyList<string> skillsDirectories)
        => preview ? [] : SkillInstaller.FindRequiringStableSwitch(recommendedSkills, skillsDirectories);

    internal static RecommendationSelectionResult CloseDependencies(
        IReadOnlyList<DirectivePlanItem> directives,
        IReadOnlyList<SkillManifestEntry> selected,
        IReadOnlyList<SkillManifestEntry> available)
    {
        var items = RecommendationSelectionPrompt.BuildItems(directives, available);
        RecommendationSelectionState state = new(items);
        state.Apply(new(SkillSelectionCommand.SelectNone));
        foreach (var item in items.Where(item => item.Directive is not null || selected.Any(skill => skill.Key == item.Skill?.Key)))
        {
            state.SelectWithDependencies(item.Key);
        }

        return new(state.SelectedDirectives, state.SelectedSkills);
    }

    static IEnumerable<string> UpdateSkillKeys(
        SkillUpdateCandidate update,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
    {
        yield return SkillKey(update.SourceRepo, update.Name);
        foreach (var skill in FindMatchingManifestEntries(update, recommendedSkills))
        {
            yield return SkillKey(skill);
        }
    }

    static string CanonicalUpdateKey(
        SkillUpdateCandidate update,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
        => FindMatchingManifestEntries(update, recommendedSkills).FirstOrDefault() is { } skill
            ? SkillKey(skill)
            : SkillKey(update.SourceRepo, update.Name);

    static SkillUpdateDisplayItem ResolveSkillUpdateDisplayItem(
        SkillUpdateCandidate update,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
        => FindMatchingManifestEntries(update, recommendedSkills).FirstOrDefault() is { } skill
            ? new SkillUpdateDisplayItem(skill.LocalFolder, skill.SourceRepo, skill.Plugin)
            : new SkillUpdateDisplayItem(update.Name, update.SourceRepo, "default");

    static IEnumerable<SkillManifestEntry> FindMatchingManifestEntries(
        SkillUpdateCandidate update,
        IReadOnlyList<SkillManifestEntry> recommendedSkills)
        => recommendedSkills.Where(skill =>
            skill.SourceRepo.Equals(update.SourceRepo, StringComparison.OrdinalIgnoreCase)
            && (skill.InstallArg.Equals(update.Name, StringComparison.OrdinalIgnoreCase)
                || skill.LocalFolder.Equals(update.Name, StringComparison.OrdinalIgnoreCase)));

    static string FormatSkillSourceHeader(string sourceRepo)
        => $"{sourceRepo} repo";

    static string FormatSkillPluginHeader(string plugin)
        => string.IsNullOrWhiteSpace(plugin) ? "default" : plugin;

    static IEnumerable<IGrouping<string, T>> OrderPluginGroups<T>(string sourceRepo, IEnumerable<IGrouping<string, T>> pluginGroups)
        => pluginGroups
            .OrderBy(group => SkillOrdering.GetPluginOrder(sourceRepo, group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    static IReadOnlyList<SkillUpdateCandidate> ExtractSkillUpdates(CommandReport updateReport)
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
        SkillUpdateCandidate[] updateLines = [.. normalized
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !IsIgnoredOutdatedSkillLine(line, ignoredLineFragments))
            .Select(ParseSkillUpdateLine)
            .OfType<SkillUpdateCandidate>()];
        return updateLines.Length == 0
            ? [new SkillUpdateCandidate(ExtractSkillNameFromUpdateLine(normalized), "unknown source")]
            : updateLines;
    }

    static bool IsIgnoredOutdatedSkillLine(string line, IReadOnlyList<string> ignoredLineFragments)
    {
        string normalized = TrimListMarker(line);
        return ignoredLineFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)) || normalized.Length > 0
            && char.IsDigit(normalized[0])
            && (normalized.Contains("update(s) available", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("updates available", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("update available", StringComparison.OrdinalIgnoreCase));
    }

    static SkillUpdateCandidate? ParseSkillUpdateLine(string line)
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
                return ExtractSkillUpdateCandidate(normalized[prefix.Length..]);
            }
        }

        return ExtractSkillUpdateCandidate(normalized);
    }

    static SkillUpdateCandidate? ExtractSkillUpdateCandidate(string line)
    {
        string normalized = TrimListMarker(line).TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        int metadataIndex = normalized.IndexOf(" (", StringComparison.Ordinal);
        if (metadataIndex <= 0)
        {
            return new SkillUpdateCandidate(normalized.Trim(), "unknown source");
        }

        string name = normalized[..metadataIndex].Trim();
        int sourceStartIndex = metadataIndex + 2;
        int sourceEndIndex = normalized.IndexOf(')', sourceStartIndex);
        string sourceRepo = sourceEndIndex > sourceStartIndex
            ? normalized[sourceStartIndex..sourceEndIndex].Trim()
            : "unknown source";
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new SkillUpdateCandidate(name, sourceRepo);
    }

    static string ExtractSkillNameFromUpdateLine(string line)
        => ExtractSkillUpdateCandidate(line)?.Name ?? string.Empty;

    static string TrimListMarker(string line)
    {
        string normalized = line.Trim();
        return normalized.Length > 0 && normalized[0] is '•' or '-' or '*'
            ? normalized[1..].Trim()
            : normalized;
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
