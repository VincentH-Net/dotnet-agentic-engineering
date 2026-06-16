namespace Agentic.Check.Tests;

public sealed class SkillSelectionStateTests
{
    [Fact]
    public void StartsWithAllSkillsSelected()
    {
        SkillSelectionState state = new(CreateSkills("alpha", "beta"));

        Assert.Equal(["alpha", "beta"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void RightSelectsAllSkills()
    {
        SkillSelectionState state = new(CreateSkills("alpha", "beta", "gamma"));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'g'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectAll));

        Assert.Equal(["alpha", "beta", "gamma"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void LeftClearsAllSkills()
    {
        SkillSelectionState state = new(CreateSkills("alpha", "beta", "gamma"));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'g'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));

        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void TypingFiltersByDisplay()
    {
        SkillSelectionState state = new(CreateSkills("uno-mvvm", "dotnet-livecharts2"));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'u'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'n'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'o'));

        var filteredSkill = Assert.Single(state.FilteredSkills);
        Assert.Equal("uno-mvvm", filteredSkill.LocalFolder);
    }

    [Fact]
    public void BackspaceUpdatesFilter()
    {
        SkillSelectionState state = new(CreateSkills("uno-mvvm", "dotnet-livecharts2"));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'u'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Backspace));

        Assert.Equal(string.Empty, state.Filter);
        Assert.Equal(2, state.FilteredSkills.Count);
    }

    static IReadOnlyList<SkillManifestEntry> CreateSkills(params string[] names)
        => [.. names.Select(name => new SkillManifestEntry("owner/repo", name, name, TechnologyNames.Dotnet, []))];
}
