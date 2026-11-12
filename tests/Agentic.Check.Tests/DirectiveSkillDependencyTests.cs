namespace Agentic.Check.Tests;

public sealed class DirectiveSkillDependencyTests
{
    static readonly DirectivePlanItem Directive = new("dotnet-sample", DirectiveStatuses.Missing, "Follow the `needed` skill for both cases.");

    static SkillManifestEntry Skill(string name)
        => new("fixture", name, name, TechnologyNames.Dotnet, []);

    static string Key(SkillManifestEntry skill)
        => RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg);

    // The pairs this repository ships. A directive that starts or stops naming a skill shows up here, and so
    // does a skill that is not offered wherever its directive is.
    [Fact]
    public void RepositoryDirectivesNameTheSkillsTheyUse()
    {
        Dictionary<string, string[]> expected = new(StringComparer.Ordinal)
        {
            ["dotnet-backward-compatible-renames"] = ["dotnet-refactoring"],
            ["dotnet-build-errors-and-warnings"] = ["dotnet-modern-csharp-editorconfig"],
            ["uno-build-and-run"] = ["uno-agentic-support"]
        };
        string[] paths = Directory.GetFiles(Path.Combine(CompanionTests.CheckoutRoot(), "directives"), "*.md");

        string[] names = [.. paths.Select(path => Path.GetFileNameWithoutExtension(path))];
        Assert.All(expected.Keys, name => Assert.Contains(name, names));
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string content = File.ReadAllText(path);
            var block = DirectiveMarkers.FindBlock(content, name);
            Assert.True(block is not null, $"{name} has no directive block.");
            string[] named = [.. DirectiveSkillDependency.NamedIn(content[block.Value]).Order(StringComparer.Ordinal)];

            Assert.Equal(expected.GetValueOrDefault(name, []), named);
            string technology = name[..name.IndexOf('-', StringComparison.Ordinal)];
            foreach (string skillName in named)
            {
                foreach (var manifest in new[] { StaticSkillManifest.All, StaticSkillManifest.Preview })
                {
                    Assert.Contains(manifest, skill => skill.LocalFolder == skillName && skill.Technology == technology && skill.GateRequirements.Count == 0);
                }
            }
        }
    }

    [Theory]
    [InlineData("If NO, use the `some-skill` skill EXACTLY as written.", "some-skill")]
    [InlineData("Follow the `some-skill` skill.", "some-skill")]
    [InlineData("This overrides any skill's steps, including `some-skill`; apply it.", null)]
    [InlineData("A log entry that mentions `some-skill`.", null)]
    [InlineData("One of the `some-skill` skills.", null)]
    public void OnlyANameFollowedByTheWordSkillIsNamed(string content, string? expected)
        => Assert.Equal(expected is null ? [] : [expected], DirectiveSkillDependency.NamedIn(content));

    [Fact]
    public void SelectingADirectiveSelectsItsSkillAndDeselectingTheSkillDeselectsTheDirective()
    {
        var needed = Skill("needed");
        var other = Skill("other");
        RecommendationSelectionState state = new(RecommendationSelectionPrompt.BuildItems([Directive], [needed, other]));
        state.Apply(new(SkillSelectionCommand.SelectNone));

        state.SelectWithDependencies(RecommendationSelectionState.FormatDirectiveKey(Directive.Name));

        Assert.Equal([needed], state.SelectedSkills);
        state.Apply(new(SkillSelectionCommand.SelectAll));
        state.DeselectWithDependents(Key(needed));
        Assert.Empty(state.SelectedDirectives);
        Assert.Equal([other], state.SelectedSkills);
    }

    // What --yes and a dry run go through: a selected directive brings the skill it names.
    [Fact]
    public void ClosingTheSelectionBringsTheNamedSkill()
    {
        var needed = Skill("needed");

        var closed = CheckWorkflow.CloseDependencies([Directive], [], [needed, Skill("other")]);

        Assert.Equal([needed], closed.SelectedSkills);
        Assert.Equal([Directive], closed.SelectedDirectives);
    }

    // An installed skill is not a row, so there is nothing to bring along or to take the directive away.
    [Fact]
    public void ADirectiveWhoseSkillIsNotOfferedStandsOnItsOwn()
    {
        var other = Skill("other");
        RecommendationSelectionState state = new(RecommendationSelectionPrompt.BuildItems([Directive], [other]));

        state.DeselectWithDependents(Key(other));

        Assert.Equal([Directive], state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void ARowTheCheckLeavesUnselectedOrARemovalIsNotBroughtAlong()
    {
        var replacement = Skill("needed") with { SelectedByDefault = false, ForceInstall = true };
        SkillManifestEntry removal = new("someone/else", "needed", "needed", string.Empty, [], recommendationAction: SkillInstaller.RemoveAction);

        var closed = CheckWorkflow.CloseDependencies([Directive], [], [replacement, removal]);

        Assert.Empty(closed.SelectedSkills);
        Assert.Equal([Directive], closed.SelectedDirectives);
    }

    // A directive and the skill it names belong in the same folder, whatever a harness reads from the folders
    // above or below. So the skill that is left out because a copy exists there takes the directive with it,
    // as it takes a skill that depends on it.
    [Fact]
    public void ASkillPresentElsewhereTakesItsDirectiveOutOfTheDefaultSelection()
    {
        var needed = Skill("needed");
        ScopeDuplicateScanResult duplicates = new(new Dictionary<string, IReadOnlyList<string>> { [Key(needed)] = ["../.agents/skills/needed/SKILL.md"] });

        var selection = RecommendationSelectionPrompt.DefaultSelection([Directive], [needed], duplicates);

        Assert.Empty(selection.SelectedDirectives);
        Assert.Empty(selection.SelectedSkills);
    }

    // Tab OFF installs here what exists above or below, so the rows that left with it come back with it;
    // the next ON takes both out again. A row the user deselected stays out.
    [Fact]
    public void SpecializationOffBringsTheDirectiveBackWithItsSkill()
    {
        var needed = Skill("needed");
        var other = Skill("other");
        DirectivePlanItem unrelated = new("dotnet-unrelated", DirectiveStatuses.Missing, "Body.");
        RecommendationSelectionState state = new(RecommendationSelectionPrompt.BuildItems([Directive, unrelated], [needed, other]));
        state.ApplySpecializationScanResult(new(new Dictionary<string, IReadOnlyList<string>> { [Key(needed)] = ["../.agents/skills/needed/SKILL.md"] }));
        Assert.Equal([unrelated], state.SelectedDirectives);
        state.DeselectWithDependents(RecommendationSelectionState.FormatDirectiveKey(unrelated.Name));

        state.ToggleCachedSpecialization();

        Assert.Equal([Directive], state.SelectedDirectives);
        Assert.Equal([needed, other], state.SelectedSkills);
        // Deselecting another row must not drop the directive that came back.
        state.DeselectWithDependents(Key(other));
        Assert.Equal([Directive], state.SelectedDirectives);

        state.ToggleCachedSpecialization();

        Assert.Empty(state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);
    }

    // The same between skills: a skill selected from the start never brings along the replacement of a
    // skill with the same name from another repository, or its removal. Selecting the replacement still does.
    [Fact]
    public void ASkillSelectedFromTheStartDoesNotBringAnUnselectedReplacementOrItsRemoval()
    {
        InstalledSkill[] installed = [new("/repo/.agents/skills", "needed", "someone/else", "skills/needed")];
        var needed = Skill("needed");
        SkillManifestEntry dependent = new("fixture", "dependent", "dependent", TechnologyNames.Dotnet, [], dependencies: [new("fixture", "needed")]);
        SkillManifestEntry[] recommended = [needed, dependent];
        SkillManifestEntry[] rows = [
            .. SkillRemovalPlanner.WithReplacements([dependent], recommended, installed),
            .. SkillRemovalPlanner.PlanRemovals(installed, recommended, new HashSet<SkillIdentity>())];
        Assert.Equal(3, rows.Length);

        var defaults = RecommendationSelectionPrompt.DefaultSelection([], rows, new(new Dictionary<string, IReadOnlyList<string>>()));
        var closed = CheckWorkflow.CloseDependencies([], defaults.SelectedSkills, rows);

        Assert.Equal(["dependent"], closed.SelectedSkills.Select(skill => skill.LocalFolder));
        var replaced = CheckWorkflow.CloseDependencies([], [.. rows.Where(row => row.LocalFolder == "needed" && !SkillRemovalPlanner.IsRemoval(row))], rows);
        Assert.Equal(["install", SkillInstaller.RemoveAction], replaced.SelectedSkills.Select(skill => skill.RecommendationAction));
    }

    // As between skills: the directive is written before skills install and stays when its skill fails;
    // the failure is reported and the next run offers the skill again.
    [Fact]
    public async Task AFailedSkillInstallLeavesTheDirectiveAndFailsTheRun()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write(".agents/skills/dotnet-livecharts2/SKILL.md", "# Present");
        FakeCommandRunner commandRunner = new();
        commandRunner.Enqueue(new CommandResult(0, "gh version 2.93.0", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "gh skill help", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(0, "no updates", string.Empty));
        commandRunner.Enqueue(new CommandResult(1, string.Empty, "skill not found"));
        var directives = FakeDirectiveSource.DefaultDirectiveContents();
        directives["dotnet-sample.md"] = FakeDirectiveSource.DirectiveFile("dotnet-sample")
            .Replace("Body for dotnet-sample.", "Use the `dotnet-modern-csharp-editorconfig` skill.", StringComparison.Ordinal);
        CheckWorkflow workflow = new(
            commandRunner,
            // Only the directives are picked; the skill comes along because one of them names it.
            new FakePrompts { SelectedSkillInstallArgs = [] },
            new RecordingReporter(),
            new FakeDirectiveSource(directives), new FakeSourceVersionResolver());

        var result = await workflow.RunAsync(
            new AgenticCheckOptions(tempDirectory.Path, false, false, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        var failed = Assert.Single(result.Report.InstallResults, install => !install.Success);
        Assert.Equal("dotnet-modern-csharp-editorconfig", failed.InstallArg);
        Assert.Contains("<!-- dotnet-sample:start -->", await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "AGENTS.md"), CancellationToken.None), StringComparison.Ordinal);
    }
}
