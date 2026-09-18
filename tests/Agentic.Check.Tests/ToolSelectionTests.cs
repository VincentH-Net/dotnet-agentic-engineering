namespace Agentic.Check.Tests;

public sealed class ToolSelectionTests
{
    [Fact]
    public void DeselectingSoleConsumerRemovesAutomaticTools()
    {
        var state = CreateState();
        Toggle(state, "foundation-prompt-log");
        Assert.Equal(2, state.SelectedSkills.Count);

        Toggle(state, "foundation-prompt-log");

        Assert.Empty(state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void SharedConsumerKeepsAutomaticToolsUntilLastConsumerIsDeselected()
    {
        var state = CreateState(includeConsumer: true);
        Toggle(state, "foundation-prompt-log");
        Toggle(state, "fixture-consumer");

        Toggle(state, "foundation-prompt-log");

        Assert.Equal(3, state.SelectedSkills.Count);
        Toggle(state, "fixture-consumer");
        Assert.Empty(state.SelectedSkills);
    }

    [Theory]
    [InlineData("InnoWvate.Agentic")]
    [InlineData("shorthand")]
    public void IndependentlySelectedToolsSurviveConsumerDeselection(string tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var state = CreateState();
        Toggle(state, tool);
        Toggle(state, "foundation-prompt-log");

        Toggle(state, "foundation-prompt-log");

        Assert.Equal(2, state.SelectedSkills.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialAndSelectAllChoicesSurviveConsumerDeselection(bool selectAll)
    {
        var state = CreateState(clearSelection: false);
        if (selectAll)
        {
            state.Apply(new(SkillSelectionCommand.SelectNone));
            Toggle(state, "foundation-prompt-log");
            state.Apply(new(SkillSelectionCommand.SelectAll));
        }

        Toggle(state, "foundation-prompt-log");

        Assert.Equal(2, state.SelectedSkills.Count);
    }

    [Fact]
    public void ExplicitlyReselectedShorthandKeepsItsCompanion()
    {
        var state = CreateState();
        Toggle(state, "foundation-prompt-log");
        Toggle(state, "shorthand");
        Assert.True(Assert.Single(state.SelectedSkills).IsCompanion);
        Toggle(state, "shorthand");

        Toggle(state, "foundation-prompt-log");

        Assert.Equal(2, state.SelectedSkills.Count);
    }

    [Fact]
    public void RequiredRepairSurvivesDeselectionButCanStillBeExplicitlyDeclined()
    {
        var state = CreateState(repair: true);
        Toggle(state, "foundation-prompt-log");

        Toggle(state, "foundation-prompt-log");

        Assert.Contains(state.SelectedSkills, skill => skill.IsCompanion && skill.IsRequiredToolRepair);
        Toggle(state, "InnoWvate.Agentic");
        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void SpecializationRemovingSoleConsumerAlsoRemovesAutomaticTools()
    {
        var state = CreateState();
        Toggle(state, "foundation-prompt-log");

        state.ApplySpecializationScanResult(new(new Dictionary<string, IReadOnlyList<string>>
        {
            ["directive:foundation-prompt-log"] = ["../AGENTS.md"]
        }));

        Assert.Empty(state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void RestoredSpecializationRetainsAutomaticSelectionOrigins()
    {
        var state = CreateState();
        Toggle(state, "foundation-prompt-log");
        state.ApplySpecializationScanResult(new(new Dictionary<string, IReadOnlyList<string>>()));
        state.ToggleCachedSpecialization();
        state.Apply(new(SkillSelectionCommand.SelectNone));
        state.ToggleCachedSpecialization();
        Assert.Equal(2, state.SelectedSkills.Count);

        Toggle(state, "foundation-prompt-log");

        Assert.Empty(state.SelectedSkills);
    }

    [Theory]
    [InlineData(null, "install", "global launcher")]
    [InlineData("1.0.0", "update", "global launcher; currently 1.0.0")]
    public void ShorthandLabelDescribesPlannedAction(string? installed, string action, string detail)
    {
        var item = Assert.Single(RecommendationSelectionPrompt.BuildItems([], [DnaInstaller.Action(new(installed))]));

        Assert.Equal($"`dna` shorthand for `dotnet agentic` ({action})", item.Display);
        Assert.Equal(detail, item.Version);
    }

    static RecommendationSelectionState CreateState(bool includeConsumer = false, bool repair = false, bool clearSelection = true)
    {
        var companion = CompanionDependency.Action() with { IsRequiredToolRepair = repair };
        var skills = includeConsumer ? new[] { CompanionTests.Consumer(), companion, DnaInstaller.Action(new(null)) }
            : [companion, DnaInstaller.Action(new(null))];
        RecommendationSelectionState state = new(RecommendationSelectionPrompt.BuildItems(
            [new("foundation-prompt-log", DirectiveStatuses.Missing, "dotnet agentic prompt-log show -m 2.3")], skills));
        if (clearSelection)
            state.Apply(new(SkillSelectionCommand.SelectNone));
        return state;
    }

    static void Toggle(RecommendationSelectionState state, string filter)
    {
        state.Apply(new(SkillSelectionCommand.ClearFilter));
        foreach (char character in filter)
            state.Apply(new(SkillSelectionCommand.Character, character));
        _ = Assert.Single(state.FilteredItems);
        state.Apply(new(SkillSelectionCommand.Toggle));
    }
}
