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

        Assert.Equal("missing-skill (install)", RecommendationSelectionPrompt.FormatSkillListItem(skill));
        Assert.Equal("owner/repo repo", RecommendationSelectionPrompt.FormatSkillSourceHeader(skill));
        Assert.Equal("default", RecommendationSelectionPrompt.FormatSkillPluginHeader(skill));
    }

    [Fact]
    public void SinglePluginHeaderThatRepeatsRepoNameIsHidden()
    {
        Assert.False(SkillGroupHeaderPolicy.ShouldShowPluginHeaders("mtmattei/UnoPlatformSkills", ["UnoPlatformSkills"]));
        Assert.False(SkillGroupHeaderPolicy.ShouldShowPluginHeaders("unoplatform/studio", ["studio"]));
    }

    [Fact]
    public void PluginHeaderIsShownForDistinctOrMultiplePluginNames()
    {
        Assert.True(SkillGroupHeaderPolicy.ShouldShowPluginHeaders("VincentH-Net/dotnet-agentic-engineering", ["dotnet"]));
        Assert.True(SkillGroupHeaderPolicy.ShouldShowPluginHeaders("dotnet/skills", ["dotnet-test"]));
        Assert.True(SkillGroupHeaderPolicy.ShouldShowPluginHeaders("owner/repo", ["alpha", "beta"]));
    }

    [Fact]
    public void RecommendationPromptHeadingUsesActionWording()
        => Assert.Equal(
            "Recommend 3 action(s), select which to apply:",
            RecommendationSelectionPrompt.FormatRecommendationPromptHeading(3));

    [Fact]
    public void DirectiveListItemUsesActionText()
    {
        DirectivePlanItem missing = new("dotnet-cli-run", DirectiveStatuses.Missing, "content");
        DirectivePlanItem update = new("dotnet-build-errors-and-warnings", DirectiveStatuses.Outdated, "content");

        Assert.Equal("dotnet-cli-run (install)", RecommendationSelectionPrompt.FormatDirectiveListItem(missing));
        Assert.Equal("dotnet-build-errors-and-warnings (update)", RecommendationSelectionPrompt.FormatDirectiveListItem(update));
    }

    [Fact]
    public void VisibleItemsAreBoundedAroundCursor()
    {
        var items = CreateItems([], [.. Enumerable.Range(1, 30).Select(index => $"skill-{index}")]);

        var (startIndex, visibleItems) = RecommendationSelectionPrompt.GetVisibleItems(items, 20);

        Assert.Equal(6, startIndex);
        Assert.Equal(RecommendationSelectionPrompt.MaxVisibleItems, visibleItems.Count);
        Assert.Equal("skill-7", visibleItems[0].Skill?.LocalFolder);
        Assert.Equal("skill-30", visibleItems[^1].Skill?.LocalFolder);
    }

    [Fact]
    public void VisibleItemsDoNotScrollWhenAllItemsFit()
    {
        var items = CreateItems([], ["alpha", "beta"]);

        var (startIndex, visibleItems) = RecommendationSelectionPrompt.GetVisibleItems(items, 1);

        Assert.Equal(0, startIndex);
        Assert.Same(items, visibleItems);
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

    [Fact]
    public void SelectingSkillSelectsMissingDependencies()
    {
        RecommendationSelectionState state = new([
            CreateSkillItem(new SkillManifestEntry(
                "owner/repo",
                "dependency",
                "dependency",
                TechnologyNames.Dotnet,
                [])),
            CreateSkillItem(new SkillManifestEntry(
                "owner/repo",
                "dependent",
                "dependent",
                TechnologyNames.Dotnet,
                [],
                dependencies: [new SkillDependency("owner/repo", "dependency")]))
        ]);
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Down));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Equal(["dependency", "dependent"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void DeselectingDependencyDeselectsDependentSkills()
    {
        RecommendationSelectionState state = new([
            CreateSkillItem(new SkillManifestEntry(
                "owner/repo",
                "dependency",
                "dependency",
                TechnologyNames.Dotnet,
                [])),
            CreateSkillItem(new SkillManifestEntry(
                "owner/repo",
                "dependent",
                "dependent",
                TechnologyNames.Dotnet,
                [],
                dependencies: [new SkillDependency("owner/repo", "dependency")]))
        ]);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Empty(state.SelectedSkills);
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
                RecommendationSelectionState.FormatSkillKey("owner/repo", name),
                name,
                RecommendationSelectionKind.Skill,
                null,
                new SkillManifestEntry("owner/repo", name, name, TechnologyNames.Dotnet, []))))];

    static RecommendationSelectionItem CreateSkillItem(SkillManifestEntry skill)
        => new(
            RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg),
            skill.LocalFolder,
            RecommendationSelectionKind.Skill,
            null,
            skill);
}
