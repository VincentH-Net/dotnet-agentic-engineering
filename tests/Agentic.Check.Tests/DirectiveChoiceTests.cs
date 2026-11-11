namespace Agentic.Check.Tests;

// A folder below directives holds alternatives of which a repository has one. The project phase is the
// first: pre-release or in production, which the user chooses and the check never defaults.
public sealed class DirectiveChoiceTests
{
    const string Other = "foundation-other";
    const string PreRelease = "project-phase-pre-release";
    const string Production = "project-phase-production";

    static readonly StackDetectionResult Foundation = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation }, [], []);

    static FakeDirectiveSource Source(string otherBody = "Body for foundation-other.")
        => new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Other + ".md"] = FakeDirectiveSource.DirectiveFile(Other).Replace("Body for foundation-other.", otherBody, StringComparison.Ordinal),
                [PreRelease + ".md"] = FakeDirectiveSource.DirectiveFile(PreRelease),
                [Production + ".md"] = FakeDirectiveSource.DirectiveFile(Production)
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PreRelease + ".md"] = DirectiveChoices.ProjectPhase,
                [Production + ".md"] = DirectiveChoices.ProjectPhase
            });

    static string Block(string name)
        => $"<!-- {name}:start -->\n## {name}\nBody for {name}.\n<!-- {name}:end -->";

    [Fact]
    public async Task MembersNobodyChoseAreAlternativesNotMissingDirectives()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory(".");

        var plan = await new DirectiveInstaller(Source(), new NullReporter()).PlanAsync(temp.Path, Foundation, CancellationToken.None);

        Assert.True(plan.Success);
        Assert.Equal(DirectiveStatuses.Missing, plan.Directives.Single(directive => directive.Name == Other).Status);
        Assert.All(plan.Directives.Where(directive => directive.Name != Other), directive =>
        {
            Assert.Equal(DirectiveStatuses.Alternative, directive.Status);
            Assert.Equal(DirectiveChoices.ProjectPhase, directive.Group);
            Assert.Null(directive.Replaces);
        });
        Assert.Equal(1, plan.RecommendedCount);
        Assert.Equal(1, plan.MissingCount);
        Assert.Equal([DirectiveChoices.ProjectPhase], plan.UnsetChoices);
        Assert.Null(plan.Chosen(DirectiveChoices.ProjectPhase));
        Assert.Equal(3, plan.SelectableDirectives.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChoosingAMemberInstallsItAndChoosingTheOtherReplacesIt(bool dryRun)
    {
        using TempDirectory temp = new();
        temp.Write("AGENTS.md", $"# Team notes\n\n{Block(PreRelease)}\n\nKeep this paragraph.\n");
        DirectiveInstaller installer = new(Source(), new NullReporter());

        var plan = await installer.PlanAsync(temp.Path, Foundation, CancellationToken.None);

        Assert.Equal(PreRelease, plan.Chosen(DirectiveChoices.ProjectPhase)?.Name);
        Assert.Empty(plan.UnsetChoices);
        var alternative = plan.Directives.Single(directive => directive.Name == Production);
        Assert.Equal(DirectiveStatuses.Alternative, alternative.Status);
        Assert.Equal(PreRelease, alternative.Replaces);
        Assert.Equal(2, plan.RecommendedCount);

        var result = await installer.ApplyAsync(plan, [Production], dryRun, CancellationToken.None);

        Assert.True(result.Success);
        string agents = await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md"));
        if (dryRun)
        {
            Assert.Equal([$"Would remove directive {PreRelease} from AGENTS.", $"Would add directive {Production} to AGENTS."], result.Actions.Take(2));
            Assert.Contains(Block(PreRelease), agents, StringComparison.Ordinal);
            return;
        }

        Assert.Equal([$"Removed directive {PreRelease} from AGENTS.", $"Added directive {Production} to AGENTS."], result.Actions.Take(2));
        Assert.Equal($"# Team notes\n\nKeep this paragraph.\n\n{Block(Production)}\n", agents);
        var replanned = await installer.PlanAsync(temp.Path, Foundation, CancellationToken.None);
        Assert.Equal(Production, replanned.Chosen(DirectiveChoices.ProjectPhase)?.Name);
        Assert.Equal(Production, replanned.Directives.Single(directive => directive.Name == PreRelease).Replaces);
    }

    [Fact]
    public async Task ChoiceRowsStartUnselectedExcludeEachOtherAndStayOutOfSelectAll()
    {
        using TempDirectory temp = new();
        _ = temp.CreateDirectory(".");
        var plan = await new DirectiveInstaller(Source(), new NullReporter()).PlanAsync(temp.Path, Foundation, CancellationToken.None);
        var items = RecommendationSelectionPrompt.BuildItems(plan.SelectableDirectives, []);
        RecommendationSelectionState state = new(items);

        Assert.Equal([Other], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal($"{PreRelease} (choose one)", items.Single(item => item.Directive?.Name == PreRelease).Display);
        Assert.NotEmpty(items.Single(item => item.Directive?.Name == PreRelease).Notes);

        state.SelectWithDependencies(RecommendationSelectionState.FormatDirectiveKey(PreRelease));
        Assert.Equal([Other, PreRelease], state.SelectedDirectives.Select(directive => directive.Name));
        state.SelectWithDependencies(RecommendationSelectionState.FormatDirectiveKey(Production));
        Assert.Equal([Other, Production], state.SelectedDirectives.Select(directive => directive.Name));

        state.Apply(new(SkillSelectionCommand.SelectNone));
        state.Apply(new(SkillSelectionCommand.SelectAll));
        Assert.Equal([Other], state.SelectedDirectives.Select(directive => directive.Name));
    }

    [Fact]
    public void ARowThatReplacesAnInstalledPhaseSaysWhichOne()
    {
        DirectivePlanItem alternative = new(Production, DirectiveStatuses.Alternative, "content") { Group = DirectiveChoices.ProjectPhase, Replaces = PreRelease };

        Assert.Equal($"{Production} (replace {PreRelease})", RecommendationSelectionPrompt.FormatDirectiveListItem(alternative));
        Assert.Equal("production", DirectiveChoices.ValueOf(alternative));
    }

    [Theory]
    [InlineData(Production)]
    [InlineData(null)]
    public async Task AnInteractiveRunAsksForAPhaseNobodyChoseAndInstallsOnlyTheAnswer(string? answer)
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [], ChoiceAnswer = answer };
        RecordingReporter reporter = new();

        var result = await Run(temp.Path, prompts, reporter);

        Assert.Equal(0, result.ExitCode);
        var asked = Assert.Single(prompts.Choices);
        Assert.Equal(DirectiveChoices.ProjectPhase, asked.Group);
        Assert.Equal([PreRelease, Production], asked.Options.Select(option => option.DirectiveName));
        Assert.NotEmpty(asked.Explanation);
        Assert.Equal([Other, PreRelease, Production], prompts.RecommendedDirectives!.Select(directive => directive.Name));
        Assert.Contains("Project phase: not set (choose one below, or with --project-phase <pre-release|production>)", reporter.Infos);
        Assert.Null(result.Report.ProjectPhase);
        string agentsFile = Path.Combine(temp.Path, "AGENTS.md");
        if (answer is null)
        {
            Assert.False(File.Exists(agentsFile));
            return;
        }

        Assert.Equal(Block(Production) + "\n", await File.ReadAllTextAsync(agentsFile));
    }

    [Fact]
    public async Task AnUnattendedRunNeitherAsksNorChoosesAPhase()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        FakePrompts prompts = new() { ChoiceAnswer = Production };

        var result = await Run(temp.Path, prompts, new RecordingReporter(), yes: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(prompts.Choices);
        Assert.Equal(Block(Other) + "\n", await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
    }

    [Fact]
    public async Task TheProjectPhaseOptionSetsAndSwitchesThePhaseWithoutAsking()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        FakePrompts prompts = new() { ChoiceAnswer = PreRelease };
        string agentsFile = Path.Combine(temp.Path, "AGENTS.md");

        var set = await Run(temp.Path, prompts, new RecordingReporter(), yes: true, projectPhase: "pre-release");
        Assert.Equal(0, set.ExitCode);
        Assert.Equal($"{Block(Other)}\n\n{Block(PreRelease)}\n", await File.ReadAllTextAsync(agentsFile));

        RecordingReporter reporter = new();
        var switched = await Run(temp.Path, prompts, reporter, yes: true, projectPhase: "production");
        Assert.Equal(0, switched.ExitCode);
        Assert.Equal("pre-release", switched.Report.ProjectPhase);
        Assert.Contains("Project phase: pre-release (switch with --project-phase <pre-release|production>)", reporter.Infos);
        Assert.Equal($"{Block(Other)}\n\n{Block(Production)}\n", await File.ReadAllTextAsync(agentsFile));

        var repeated = await Run(temp.Path, prompts, new RecordingReporter(), yes: true, projectPhase: "production");
        Assert.Equal("production", repeated.Report.ProjectPhase);
        Assert.Equal($"{Block(Other)}\n\n{Block(Production)}\n", await File.ReadAllTextAsync(agentsFile));
        Assert.Empty(prompts.Choices);
    }

    [Fact]
    public async Task AChosenPhaseIsNotAskedAndIsOfferedForSwitchingOnlyNextToOtherActions()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        temp.Write("AGENTS.md", $"{Block(Other)}\n\n{Block(PreRelease)}\n");
        FakePrompts upToDate = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] };

        var result = await Run(temp.Path, upToDate, new RecordingReporter());

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(upToDate.Choices);
        Assert.Null(upToDate.RecommendedDirectives);
        Assert.Equal("pre-release", result.Report.ProjectPhase);

        // Another directive has an update: the list is shown anyway, so the other phase joins it, unselected.
        FakePrompts withUpdate = new() { SelectedDirectiveNames = [Production], SelectedSkillInstallArgs = [] };
        var switched = await Run(temp.Path, withUpdate, new RecordingReporter(), source: Source("A newer body."));

        Assert.Equal(0, switched.ExitCode);
        Assert.Empty(withUpdate.Choices);
        Assert.Equal([Other, Production], withUpdate.RecommendedDirectives!.Select(directive => directive.Name));
        Assert.Equal($"{Block(Other)}\n\n{Block(Production)}\n", await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
    }

    [Fact]
    public async Task APhaseChosenInAFolderAboveIsNotAskedBelow()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        temp.Write("AGENTS.md", Block(Production) + "\n");
        string below = temp.CreateDirectory("apps/mobile");
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [], ChoiceAnswer = PreRelease };
        RecordingReporter reporter = new();

        var result = await Run(below, prompts, reporter);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(prompts.Choices);
        Assert.Contains("Project phase: set in a folder above", reporter.Infos);
        Assert.False(File.Exists(Path.Combine(below, "AGENTS.md")));
    }

    static Task<CheckRunResult> Run(string target, FakePrompts prompts, RecordingReporter reporter, bool yes = false, string? projectPhase = null, FakeDirectiveSource? source = null)
    {
        FakeCommandRunner runner = new();
        runner.Enqueue(new(0, "gh version 2.100.0", string.Empty));
        runner.Enqueue(new(0, "gh skill help", string.Empty));
        runner.Enqueue(new(0, "No installed skills found.", string.Empty));
        CheckWorkflow workflow = new(runner, prompts, reporter, source ?? Source(), new FakeSourceVersionResolver(), skillManifest: []);
        return workflow.RunAsync(new(target, false, yes, null, null, "codex", false, ProjectPhase: projectPhase), CancellationToken.None);
    }
}
