namespace Agentic.Check.Tests;

public sealed class SkillSelectionStateTests
{
    [Fact]
    public void StartsWithAllRecommendationsSelected()
    {
        RecommendationSelectionState state = new(CreateItems(["foundation-prompt-log"], ["alpha", "beta"]));

        Assert.Equal(["foundation-prompt-log"], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["alpha", "beta"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void RightSelectsAllRecommendations()
    {
        RecommendationSelectionState state = new(CreateItems(["foundation-prompt-log"], ["alpha", "beta", "gamma"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'g'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectAll));

        Assert.Equal(["foundation-prompt-log"], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["alpha", "beta", "gamma"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void LeftClearsAllRecommendations()
    {
        RecommendationSelectionState state = new(CreateItems(["foundation-prompt-log"], ["alpha", "beta", "gamma"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'g'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));

        Assert.Empty(state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);
    }

    [Fact]
    public void TypingFiltersByDisplay()
    {
        RecommendationSelectionState state = new(CreateItems([], ["uno-mvvm", "dotnet-livecharts2"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'u'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'n'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'o'));

        var filteredItem = Assert.Single(state.FilteredItems);
        Assert.Equal("uno-mvvm", filteredItem.Skill?.LocalFolder);
    }

    [Fact]
    public void BackspaceUpdatesFilter()
    {
        RecommendationSelectionState state = new(CreateItems([], ["uno-mvvm", "dotnet-livecharts2"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'u'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Backspace));

        Assert.Equal(string.Empty, state.Filter);
        Assert.Equal(2, state.FilteredItems.Count);
    }

    [Fact]
    public void SkillListItemOmitsSourceRepo()
    {
        SkillManifestEntry skill = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);

        Assert.Equal("missing-skill", RecommendationSelectionPrompt.FormatSkillListItem(skill));
        Assert.Equal("owner/repo", RecommendationSelectionPrompt.FormatSkillSourceHeader(skill));
    }

    [Fact]
    public void TypingFiltersBySourceRepo()
    {
        RecommendationSelectionState state = new([
            new RecommendationSelectionItem(
                "skill:owner/alpha:first",
                "first",
                RecommendationSelectionKind.Skill,
                null,
                new SkillManifestEntry("owner/alpha", "first", "first", TechnologyNames.Dotnet, [])),
            new RecommendationSelectionItem(
                "skill:owner/beta:second",
                "second",
                RecommendationSelectionKind.Skill,
                null,
                new SkillManifestEntry("owner/beta", "second", "second", TechnologyNames.Dotnet, []))
        ]);
        foreach (char character in "beta")
        {
            state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, character));
        }

        var filteredItem = Assert.Single(state.FilteredItems);
        Assert.Equal("second", filteredItem.Skill?.LocalFolder);
    }

    static IReadOnlyList<RecommendationSelectionItem> CreateItems(string[] directiveNames, string[] skillNames)
        => [.. directiveNames
            .Select(name => new RecommendationSelectionItem(
                $"directive:{name}",
                $"{name} ({DirectiveStatuses.Missing})",
                RecommendationSelectionKind.Directive,
                new DirectivePlanItem(name, DirectiveStatuses.Missing, $"content {name}"),
                null))
            .Concat(skillNames.Select(name => new RecommendationSelectionItem(
                $"skill:owner/repo:{name}",
                name,
                RecommendationSelectionKind.Skill,
                null,
                new SkillManifestEntry("owner/repo", name, name, TechnologyNames.Dotnet, []))))];
}
