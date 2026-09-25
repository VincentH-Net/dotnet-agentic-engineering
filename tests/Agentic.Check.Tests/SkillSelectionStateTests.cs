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
    public void SkillListItemUsesRecommendationAction()
    {
        SkillManifestEntry skill = new(
            "owner/repo",
            "preview-skill",
            "preview-skill",
            TechnologyNames.Dotnet,
            [],
            recommendationAction: "switch to stable");

        Assert.Equal("preview-skill (switch to stable)", RecommendationSelectionPrompt.FormatSkillListItem(skill));
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
    public void PreviewTestAntiPatternsSelectionRequiresAnalysisExtensions()
    {
        var reference = Assert.Single(StaticSkillManifest.Preview, skill => skill.LocalFolder == "test-analysis-extensions");
        var dependent = Assert.Single(StaticSkillManifest.Preview, skill => skill.LocalFolder == "test-anti-patterns");
        RecommendationSelectionState state = new([CreateSkillItem(reference), CreateSkillItem(dependent)]);
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Down));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Equal([reference.Key, dependent.Key], state.SelectedSkills.Select(skill => skill.Key));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Up));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Empty(state.SelectedSkills);
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

    [Fact]
    public void SpecializationToggleOffKeepsCurrentSelectionAndToggleOnRestoresSpecializedDefault()
    {
        var directive = new DirectivePlanItem("foundation-prompt-log", DirectiveStatuses.Missing, "content");
        var outdatedDirective = new DirectivePlanItem("dotnet-cli-run", DirectiveStatuses.Outdated, "content");
        SkillManifestEntry missingSkill = new("owner/repo", "missing-skill", "missing-skill", TechnologyNames.Dotnet, []);
        SkillManifestEntry repairSkill = new(
            "owner/repo",
            "repair-skill",
            "repair-skill",
            TechnologyNames.Dotnet,
            [],
            recommendationAction: "switch to stable",
            forceInstall: true);
        RecommendationSelectionState state = new([
            new RecommendationSelectionItem("directive:foundation-prompt-log", "foundation-prompt-log (install)", RecommendationSelectionKind.Directive, directive, null),
            new RecommendationSelectionItem("directive:dotnet-cli-run", "dotnet-cli-run (update)", RecommendationSelectionKind.Directive, outdatedDirective, null),
            CreateSkillItem(missingSkill),
            CreateSkillItem(repairSkill)
        ]);

        state.ApplySpecializationScanResult(new ScopeDuplicateScanResult(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["directive:foundation-prompt-log"] = ["../AGENTS.md"],
                ["directive:dotnet-cli-run"] = ["../AGENTS.md"],
                [RecommendationSelectionState.FormatSkillKey(missingSkill.SourceRepo, missingSkill.InstallArg)] = ["../.agents/skills/missing-skill/SKILL.md"],
                [RecommendationSelectionState.FormatSkillKey(repairSkill.SourceRepo, repairSkill.InstallArg)] = ["../.agents/skills/repair-skill/SKILL.md"]
            }));

        Assert.Equal(["dotnet-cli-run"], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["repair-skill"], state.SelectedSkills.Select(skill => skill.LocalFolder));

        state.ToggleCachedSpecialization();

        Assert.False(state.IsSpecialized);
        Assert.Equal(["dotnet-cli-run"], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["repair-skill"], state.SelectedSkills.Select(skill => skill.LocalFolder));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));

        Assert.Empty(state.SelectedDirectives);
        Assert.Empty(state.SelectedSkills);

        state.ToggleCachedSpecialization();

        Assert.True(state.IsSpecialized);
        Assert.Equal(["dotnet-cli-run"], state.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["repair-skill"], state.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void PromptHeadingStatesTheDefaultRecommendationWhenItDiffersFromTheList()
    {
        Assert.Equal(
            "Recommend 87 action(s), select which to apply:",
            RecommendationSelectionPrompt.FormatRecommendationPromptHeading(87, 87, new PresentElsewhere(0, 0, PresentElsewhere.Above)));
        Assert.Equal(
            "Recommend 4 of 87 action(s), 83 already present above, select which to apply:",
            RecommendationSelectionPrompt.FormatRecommendationPromptHeading(4, 87, new PresentElsewhere(5, 78, PresentElsewhere.Above)));
        Assert.Equal(
            "Recommend 85 of 87 action(s), select which to apply:",
            RecommendationSelectionPrompt.FormatRecommendationPromptHeading(85, 87, new PresentElsewhere(0, 0, PresentElsewhere.Above)));
    }

    [Fact]
    public void DefaultSelectionSkipsDuplicatesButKeepsTargetLocalRepairs()
    {
        var missingDirective = new DirectivePlanItem("foundation-prompt-log", DirectiveStatuses.Missing, "content");
        var outdatedDirective = new DirectivePlanItem("dotnet-cli-run", DirectiveStatuses.Outdated, "content");
        SkillManifestEntry duplicateSkill = new("owner/repo", "alpha", "alpha", TechnologyNames.Dotnet, []);
        SkillManifestEntry newSkill = new("owner/repo", "beta", "beta", TechnologyNames.Dotnet, []);
        ScopeDuplicateScanResult duplicates = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RecommendationSelectionState.FormatDirectiveKey("foundation-prompt-log")] = ["../AGENTS.md"],
            [RecommendationSelectionState.FormatDirectiveKey("dotnet-cli-run")] = ["../AGENTS.md"],
            [RecommendationSelectionState.FormatSkillKey("owner/repo", "alpha")] = ["../.agents/skills/alpha/SKILL.md"]
        });

        var selection = RecommendationSelectionPrompt.DefaultSelection([missingDirective, outdatedDirective], [duplicateSkill, newSkill], duplicates);

        Assert.Equal(["dotnet-cli-run"], selection.SelectedDirectives.Select(directive => directive.Name));
        Assert.Equal(["beta"], selection.SelectedSkills.Select(skill => skill.LocalFolder));
    }

    [Fact]
    public void StartsInTheSelectedViewAndToggleViewShowsAll()
    {
        RecommendationSelectionState state = new(CreateItems(["foundation-prompt-log"], ["alpha", "beta"]));

        Assert.True(state.ShowSelectedOnly);
        Assert.True(state.CanToggleView);
        Assert.Equal(3, state.FilteredItems.Count);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.False(state.ShowSelectedOnly);
        Assert.Equal(3, state.FilteredItems.Count);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.True(state.ShowSelectedOnly);
    }

    [Fact]
    public void SelectedViewHidesADeselectedRowAndMovesTheCursorToTheNextRow()
    {
        RecommendationSelectionState state = new(CreateItems([], ["alpha", "beta", "gamma"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Down));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Equal(["alpha", "gamma"], state.FilteredItems.Select(item => item.Display));
        Assert.Equal("gamma", state.FilteredItems[state.CursorIndex].Display);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.Equal(["alpha", "beta", "gamma"], state.FilteredItems.Select(item => item.Display));
        Assert.Equal("gamma", state.FilteredItems[state.CursorIndex].Display);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Home));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        var row = Assert.Single(state.FilteredItems);
        Assert.Equal("gamma", row.Display);
        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void EmptySelectionShowsAllRowsUntilTheViewIsToggledAgain()
    {
        RecommendationSelectionState state = new(CreateItems([], ["alpha", "beta"]));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));

        Assert.False(state.ShowSelectedOnly);
        Assert.False(state.CanToggleView);
        Assert.Equal(2, state.FilteredItems.Count);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.False(state.ShowSelectedOnly);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.False(state.ShowSelectedOnly);
        Assert.True(state.CanToggleView);
        Assert.Equal(2, state.FilteredItems.Count);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.True(state.ShowSelectedOnly);
        Assert.Equal(["alpha"], state.FilteredItems.Select(item => item.Display));
    }

    [Fact]
    public void DeselectingTheLastSelectedRowSwitchesToTheAllView()
    {
        RecommendationSelectionState state = new(CreateItems([], ["alpha", "beta"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.True(state.ShowSelectedOnly);
        Assert.Equal(["beta"], state.FilteredItems.Select(item => item.Display));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.False(state.ShowSelectedOnly);
        Assert.Equal(["alpha", "beta"], state.FilteredItems.Select(item => item.Display));
        Assert.Equal("beta", state.FilteredItems[state.CursorIndex].Display);
    }

    [Fact]
    public void TextFilterNarrowsWithinTheCurrentView()
    {
        RecommendationSelectionState state = new(CreateItems([], ["uno-mvvm", "uno-xaml", "dotnet-livecharts2"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Down));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'u'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'n'));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Character, 'o'));

        var row = Assert.Single(state.FilteredItems);
        Assert.Equal("uno-mvvm", row.Display);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));

        Assert.Equal(["uno-mvvm", "uno-xaml"], state.FilteredItems.Select(item => item.Display));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ClearFilter));

        Assert.False(state.ShowSelectedOnly);
        Assert.Equal(3, state.FilteredItems.Count);
    }

    [Fact]
    public void SpecializationScanRemovesDuplicatesFromTheSelectedView()
    {
        RecommendationSelectionState state = new(CreateItems([], ["alpha", "beta"]));

        state.ApplySpecializationScanResult(new ScopeDuplicateScanResult(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [RecommendationSelectionState.FormatSkillKey("owner/repo", "alpha")] = ["../.agents/skills/alpha/SKILL.md"]
            }));

        Assert.True(state.ShowSelectedOnly);
        Assert.Equal(["beta"], state.FilteredItems.Select(item => item.Display));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectAll));

        Assert.Equal(["alpha", "beta"], state.FilteredItems.Select(item => item.Display));

        state.ToggleCachedSpecialization();
        state.ToggleCachedSpecialization();

        Assert.Equal(["beta"], state.FilteredItems.Select(item => item.Display));
    }

    [Fact]
    public void PageHomeAndEndKeysJumpThroughTheList()
    {
        RecommendationSelectionState state = new(CreateItems([], [.. Enumerable.Range(1, 60)
            .Select(index => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"skill-{index:00}"))]));

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.PageDown));
        Assert.Equal(RecommendationSelectionPrompt.MaxVisibleItems, state.CursorIndex);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.End));
        Assert.Equal(59, state.CursorIndex);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.PageUp));
        Assert.Equal(59 - RecommendationSelectionPrompt.MaxVisibleItems, state.CursorIndex);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Home));
        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void ViewHelpLineShowsCountsAndOffersTheToggleOnlyWithASelection()
    {
        RecommendationSelectionState state = new(CreateItems([], ["alpha", "beta", "gamma"]));
        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        string selectedView = RecommendationSelectionPrompt.FormatViewHelpLine(state);
        Assert.Contains("[bold]selected[/] (2 of 3)", selectedView, StringComparison.Ordinal);
        Assert.Contains("to show all", selectedView, StringComparison.Ordinal);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.ToggleView));
        string allView = RecommendationSelectionPrompt.FormatViewHelpLine(state);
        Assert.Contains("[bold]all[/] (2 of 3 selected)", allView, StringComparison.Ordinal);
        Assert.Contains("to show selected", allView, StringComparison.Ordinal);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.SelectNone));
        string emptySelection = RecommendationSelectionPrompt.FormatViewHelpLine(state);
        Assert.Contains("[bold]all[/] (0 of 3 selected)", emptySelection, StringComparison.Ordinal);
        Assert.DoesNotContain("F2", emptySelection, StringComparison.Ordinal);
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
