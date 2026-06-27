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
            DryRun = options.DryRun
        };

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

        var skillsDirectoryValidation = ValidateSkillsDirectory(options.SkillsDirectory);
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
            reporter.Error("Required tools are missing or too old. Update git/GitHub CLI and confirm `gh skill --help` or `gh skills --help` works.");
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

        var repoResolution = await new RepoResolver(commandRunner, prompts)
            .ResolveAsync(report.TargetDirectory, options.DryRun, cancellationToken)
            .ConfigureAwait(false);

        report.RepoRoot = repoResolution.RepoRoot;
        report.Actions.AddRange(repoResolution.Actions);

        if (!repoResolution.CanProceed)
        {
            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(2, report);
        }

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

        StackDetectionResult? stack = null;
        DirectivePlanResult? directivePlan = null;
        IReadOnlyList<SkillManifestEntry> recommended = [];
        IReadOnlyList<SkillManifestEntry> missing = [];
        IReadOnlyList<SkillUpdateCandidate> skillUpdates = [];
        DirectiveInstaller directiveInstaller = new(directiveSource ?? new GitHubDirectiveSource(), reporter);
        string firstSkillsDirectory = skillsDirectories[0];
        report.SkillsDirectory = firstSkillsDirectory;
        report.SkillsDirectories.AddRange(skillsDirectories);

        await reporter.RunProgressAsync(
            "Scanning repository",
            3,
            async advance =>
            {
                var detectedStack = StackDetector.Detect(repoResolution.RepoRoot);
                stack = detectedStack;
                report.Technologies.AddRange(detectedStack.Technologies.Order(StringComparer.OrdinalIgnoreCase));
                report.UnoGates.AddRange(detectedStack.UnoGates);
                report.Warnings.AddRange(detectedStack.Warnings);
                advance();

                directivePlan = await directiveInstaller
                    .PlanAsync(repoResolution.RepoRoot, detectedStack, manageClaudeFile, cancellationToken)
                    .ConfigureAwait(false);
                advance();

                recommended = SkillPlanner.Plan(StaticSkillManifest.All, detectedStack);
                report.RecommendedSkills.AddRange(recommended.Select(SkillReportItem.FromManifestEntry));

                missing = SkillInstaller.FindMissing(recommended, skillsDirectories);
                report.MissingSkills.AddRange(missing.Select(SkillReportItem.FromManifestEntry));

                await RunSkillUpdateDryRunAsync(skillsDirectories, repoResolution.RepoRoot, report, cancellationToken).ConfigureAwait(false);
                skillUpdates = ExtractDistinctSkillUpdates(report.SkillUpdateDryRuns, recommended);
                report.OutdatedSkills = skillUpdates.Count;
                advance();
            },
            cancellationToken).ConfigureAwait(false);

        stack = stack ?? throw new InvalidOperationException("Repository scan did not detect a stack.");
        directivePlan = directivePlan ?? throw new InvalidOperationException("Repository scan did not plan directives.");

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

        reporter.Summary(repoResolution.RepoRoot, stack.Technologies, stack.UnoGates, targetAgents, skillsDirectories, directiveSummary, recommended.Count, missing.Count, report.OutdatedSkills);

        foreach (string warning in stack.Warnings)
        {
            reporter.Warning(warning);
        }

        var recommendedDirectives = directivePlan.SelectableDirectives;
        IReadOnlyList<DirectivePlanItem> selectedDirectives = [];
        IReadOnlyList<SkillManifestEntry> selectedSkills = [];
        if (!options.DryRun)
        {
            ReportUpToDateItems(directivePlan.Directives, recommended, missing, skillUpdates);
        }

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
                selectedSkills = AddSelectedSkillDependencies(selection.SelectedSkills, missing);
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

            ReportSkillUpdateDryRunActions(report.SkillUpdateDryRuns, recommended);

            await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
            return new CheckRunResult(0, report);
        }

        if (selectedSkills.Count > 0)
        {
            SkillInstaller skillInstaller = new(commandRunner, reporter);
            IReadOnlyList<SkillManifestEntry> firstDirectoryMissingSkills = [.. selectedSkills.Where(skill => SkillInstaller.IsMissing(skill, firstSkillsDirectory))];
            int installOperationCount = CountSkillInstallOperations(selectedSkills, firstDirectoryMissingSkills, [.. skillsDirectories.Skip(1)]);
            await reporter.RunProgressAsync(
                "Installing skills",
                installOperationCount,
                async advance =>
                {
                    var installResults = await skillInstaller
                        .InstallAsync(firstDirectoryMissingSkills, firstSkillsDirectory, repoResolution.RepoRoot, cancellationToken, advance)
                        .ConfigureAwait(false);
                    report.InstallResults.AddRange(installResults);

                    if (skillsDirectories.Count > 1)
                    {
                        SkillManifestEntry[] copyableSkills = [.. selectedSkills
                            .Where(skill => !SkillInstaller.IsMissing(skill, firstSkillsDirectory))];
                        report.SkillCopyResults.AddRange(skillInstaller.CopyInstalledSkills(
                            copyableSkills,
                            firstSkillsDirectory,
                            [.. skillsDirectories.Skip(1)],
                            advance));
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        static int CountSkillInstallOperations(
            IReadOnlyList<SkillManifestEntry> selectedSkills,
            IReadOnlyList<SkillManifestEntry> firstDirectoryMissingSkills,
            IReadOnlyList<string> targetSkillsDirectories)
        {
            int copyOperations = 0;
            foreach (string targetSkillsDirectory in targetSkillsDirectories)
            {
                copyOperations += selectedSkills.Count(skill => SkillInstaller.IsMissing(skill, targetSkillsDirectory));
            }

            return firstDirectoryMissingSkills.Count + copyOperations;
        }

        await RunSkillUpdateAsync(options, skillsDirectories, repoResolution.RepoRoot, report, skillUpdates, cancellationToken).ConfigureAwait(false);

        int exitCode = report.InstallResults.Any(result => !result.Success) || report.SkillCopyResults.Any(result => !result.Success) ? 1 : 0;
        await WriteReportAsync(options.ReportPath, report, cancellationToken).ConfigureAwait(false);
        return new CheckRunResult(exitCode, report);
    }

    static DirectoryValidationResult ValidateTargetDirectory(string targetDirectory)
    {
        var pathValidation = TryGetFullPath(targetDirectory, "target directory");
        if (!pathValidation.Success)
        {
            return DirectoryValidationResult.Invalid(targetDirectory, pathValidation.Error);
        }

        string fullTargetDirectory = pathValidation.Directory;
        if (File.Exists(fullTargetDirectory))
        {
            return DirectoryValidationResult.Invalid(
                fullTargetDirectory,
                $"Invalid target directory: {fullTargetDirectory} is a file.");
        }

        if (Directory.Exists(fullTargetDirectory))
        {
            return DirectoryValidationResult.Valid(fullTargetDirectory, []);
        }

        return DirectoryValidationResult.Invalid(
            fullTargetDirectory,
            $"Target directory does not exist: {fullTargetDirectory}.");
    }

    static DirectoryValidationResult ValidateSkillsDirectory(string? skillsDirectory)
    {
        if (string.IsNullOrWhiteSpace(skillsDirectory))
        {
            return DirectoryValidationResult.Valid(string.Empty, []);
        }

        var pathValidation = TryGetFullPath(skillsDirectory, "skills directory");
        if (!pathValidation.Success)
        {
            return DirectoryValidationResult.Invalid(skillsDirectory, pathValidation.Error);
        }

        string fullSkillsDirectory = pathValidation.Directory;
        return File.Exists(fullSkillsDirectory)
            ? DirectoryValidationResult.Invalid(fullSkillsDirectory, $"Invalid skills directory: {fullSkillsDirectory} is a file.")
            : DirectoryValidationResult.Valid(fullSkillsDirectory, []);
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
        AgenticCheckOptions options,
        IReadOnlyList<string> skillsDirectories,
        string repoRoot,
        AgenticCheckReport report,
        IReadOnlyList<SkillUpdateCandidate> skillUpdates,
        CancellationToken cancellationToken)
    {
        if (report.SkillUpdateDryRuns.All(result => !result.Success))
        {
            return;
        }

        if (skillUpdates.Count == 0)
        {
            report.Actions.Add("No repo-local skill updates found.");
            return;
        }

        ReportSkillUpdates(skillUpdates, recommendedSkills: StaticSkillManifest.All);

        bool update = options.Yes || await prompts.ConfirmAsync("Update these skill(s)?", false, cancellationToken)
            .ConfigureAwait(false);

        if (!update)
        {
            report.Actions.Add("Skipped repo-local skill updates.");
            return;
        }

        List<(string SkillsDirectory, CommandReport Report)> failures = [];
        await reporter.RunProgressAsync(
            "Updating skills",
            skillsDirectories.Count,
            async advance =>
            {
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

        if (failures.Count == 0)
        {
            reporter.Success(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Updated {skillUpdates.Count} skill(s) successfully."));
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

        ReportSectionHeader("Would install skills into repo skills directories:");
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

        ReportSectionHeader("Would update skills in repo skills directories:");
        ReportSkillUpdateGroups(skillUpdates, recommendedSkills);
    }

    void ReportUpToDateItems(
        IReadOnlyList<DirectivePlanItem> directives,
        IReadOnlyList<SkillManifestEntry> recommendedSkills,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        IReadOnlyList<SkillUpdateCandidate> skillUpdates)
    {
        DirectivePlanItem[] currentDirectives = [.. directives
            .Where(directive => directive.Status == DirectiveStatuses.Current)
        ];
        var missingSkillKeys = missingSkills
            .Select(SkillKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updateSkillKeys = skillUpdates
            .SelectMany(update => UpdateSkillKeys(update, recommendedSkills))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SkillManifestEntry[] upToDateSkills = [.. recommendedSkills
            .Where(skill => !missingSkillKeys.Contains(SkillKey(skill))
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

    void ReportSectionHeader(string header)
    {
        reporter.Plain(string.Empty);
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
        => reporter.Bold(message);

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
            reporter.Bold($"  {FormatSkillSourceHeader(sourceGroup.Key)}:");
            var pluginGroups = OrderPluginGroups(sourceGroup.Key, sourceGroup.GroupBy(update => update.Plugin, StringComparer.OrdinalIgnoreCase));
            bool showPluginHeaders = SkillGroupHeaderPolicy.ShouldShowPluginHeaders(sourceGroup.Key, pluginGroups.Select(group => group.Key));
            foreach (var pluginGroup in pluginGroups)
            {
                if (showPluginHeaders)
                {
                    reporter.Bold($"    {FormatSkillPluginHeader(pluginGroup.Key)}:");
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

    static IReadOnlyList<SkillManifestEntry> AddSelectedSkillDependencies(
        IReadOnlyList<SkillManifestEntry> selectedSkills,
        IReadOnlyList<SkillManifestEntry> selectableSkills)
    {
        var selectableByKey = selectableSkills.ToDictionary(skill => skill.Key, StringComparer.OrdinalIgnoreCase);
        var selectedKeys = selectedSkills.Select(skill => skill.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in selectedSkills)
        {
            AddDependencies(skill);
        }

        return [.. selectableSkills.Where(skill => selectedKeys.Contains(skill.Key))];

        void AddDependencies(SkillManifestEntry skill)
        {
            foreach (var dependency in skill.Dependencies)
            {
                if (!selectableByKey.TryGetValue(dependency.Key, out var selectableDependency)
                    || !selectedKeys.Add(selectableDependency.Key))
                {
                    continue;
                }

                AddDependencies(selectableDependency);
            }
        }
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
