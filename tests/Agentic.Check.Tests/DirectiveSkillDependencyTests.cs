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

    // The same as for a skill that depends on that skill: the row left out because a copy exists above or
    // below takes the rows that depend on it out of the default selection.
    [Fact]
    public void ASkillPresentElsewhereTakesItsDirectiveOutOfTheDefaultSelection()
    {
        var needed = Skill("needed");
        ScopeDuplicateScanResult duplicates = new(new Dictionary<string, IReadOnlyList<string>> { [Key(needed)] = ["../.agents/skills/needed/SKILL.md"] });

        var selection = RecommendationSelectionPrompt.DefaultSelection([Directive], [needed], duplicates);

        Assert.Empty(selection.SelectedDirectives);
        Assert.Empty(selection.SelectedSkills);
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
