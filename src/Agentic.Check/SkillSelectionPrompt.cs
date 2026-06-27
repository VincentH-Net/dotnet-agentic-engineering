using Spectre.Console;

namespace Agentic.Check;

enum SkillSelectionCommand
{
    Up,
    Down,
    Toggle,
    SelectAll,
    SelectNone,
    Backspace,
    ClearFilter,
    Confirm,
    Character
}

enum RecommendationSelectionKind
{
    Directive,
    Skill
}

readonly record struct SkillSelectionInput(SkillSelectionCommand Command, char Character = '\0');

sealed record RecommendationSelectionItem(
    string Key,
    string Display,
    RecommendationSelectionKind Kind,
    DirectivePlanItem? Directive,
    SkillManifestEntry? Skill);

sealed class RecommendationSelectionState(IReadOnlyList<RecommendationSelectionItem> items)
{
    readonly IReadOnlyList<RecommendationSelectionItem> items = items;
    readonly Dictionary<string, IReadOnlyList<string>> dependencyKeysByKey = BuildDependencyKeysByKey(items);
    readonly Dictionary<string, IReadOnlyList<string>> dependentKeysByKey = BuildDependentKeysByKey(items);
    readonly HashSet<string> selectedKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<RecommendationSelectionItem> FilteredItems { get; private set; } = items;

    public string Filter { get; private set; } = string.Empty;

    public int CursorIndex { get; private set; }

    public IReadOnlyList<DirectivePlanItem> SelectedDirectives
        => [.. items
            .Where(item => selectedKeys.Contains(item.Key))
            .Select(item => item.Directive)
            .OfType<DirectivePlanItem>()];

    public IReadOnlyList<SkillManifestEntry> SelectedSkills
        => [.. items
            .Where(item => selectedKeys.Contains(item.Key))
            .Select(item => item.Skill)
            .OfType<SkillManifestEntry>()];

    public bool IsSelected(RecommendationSelectionItem item)
        => selectedKeys.Contains(item.Key);

    public void Apply(SkillSelectionInput input)
    {
        switch (input.Command)
        {
            case SkillSelectionCommand.Up:
                Move(-1);
                break;
            case SkillSelectionCommand.Down:
                Move(1);
                break;
            case SkillSelectionCommand.Toggle:
                ToggleCurrent();
                break;
            case SkillSelectionCommand.SelectAll:
                SetAllSelection(true);
                break;
            case SkillSelectionCommand.SelectNone:
                SetAllSelection(false);
                break;
            case SkillSelectionCommand.Backspace:
                RemoveFilterCharacter();
                break;
            case SkillSelectionCommand.ClearFilter:
                SetFilter(string.Empty);
                break;
            case SkillSelectionCommand.Character:
                AddFilterCharacter(input.Character);
                break;
            case SkillSelectionCommand.Confirm:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Command, "Unsupported selection input.");
        }
    }

    void Move(int delta)
    {
        if (FilteredItems.Count == 0)
        {
            CursorIndex = 0;
            return;
        }

        CursorIndex = Math.Clamp(CursorIndex + delta, 0, FilteredItems.Count - 1);
    }

    void ToggleCurrent()
    {
        if (FilteredItems.Count == 0)
        {
            return;
        }

        string key = FilteredItems[CursorIndex].Key;
        if (selectedKeys.Contains(key))
        {
            DeselectWithDependents(key);
        }
        else
        {
            SelectWithDependencies(key);
        }
    }

    void SetAllSelection(bool selected)
    {
        if (!selected)
        {
            selectedKeys.Clear();
            return;
        }

        foreach (var item in items)
        {
            SelectWithDependencies(item.Key);
        }
    }

    void SelectWithDependencies(string key)
    {
        if (!selectedKeys.Add(key))
        {
            return;
        }

        foreach (string dependencyKey in dependencyKeysByKey.GetValueOrDefault(key, []))
        {
            SelectWithDependencies(dependencyKey);
        }
    }

    void DeselectWithDependents(string key)
    {
        if (!selectedKeys.Remove(key))
        {
            return;
        }

        foreach (string dependentKey in dependentKeysByKey.GetValueOrDefault(key, []))
        {
            DeselectWithDependents(dependentKey);
        }
    }

    void AddFilterCharacter(char character)
    {
        if (!char.IsControl(character))
        {
            SetFilter(Filter + character);
        }
    }

    void RemoveFilterCharacter()
    {
        if (Filter.Length > 0)
        {
            SetFilter(Filter[..^1]);
        }
    }

    void SetFilter(string filter)
    {
        Filter = filter;
        FilteredItems = string.IsNullOrWhiteSpace(Filter)
            ? items
            : [.. items.Where(item => MatchesFilter(item, Filter))];
        CursorIndex = Math.Min(CursorIndex, Math.Max(FilteredItems.Count - 1, 0));
    }

    static bool MatchesFilter(RecommendationSelectionItem item, string filter)
        => item.Display.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || item.Skill?.SourceRepo.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
            || item.Skill?.Plugin.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    static Dictionary<string, IReadOnlyList<string>> BuildDependencyKeysByKey(IReadOnlyList<RecommendationSelectionItem> items)
    {
        var selectableKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> dependencyKeysByKey = new(StringComparer.Ordinal);
        foreach (var item in items.Where(item => item.Skill is not null))
        {
            string[] dependencyKeys = [.. item.Skill!.Dependencies
                .Select(dependency => FormatSkillKey(dependency.SourceRepo, dependency.InstallArg))
                .Where(selectableKeys.Contains)
                .Distinct(StringComparer.Ordinal)];
            dependencyKeysByKey[item.Key] = dependencyKeys;
        }

        return dependencyKeysByKey;
    }

    static Dictionary<string, IReadOnlyList<string>> BuildDependentKeysByKey(IReadOnlyList<RecommendationSelectionItem> items)
    {
        var dependencyKeysByKey = BuildDependencyKeysByKey(items);
        Dictionary<string, List<string>> mutable = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (string dependencyKey in dependencyKeysByKey.GetValueOrDefault(item.Key, []))
            {
                if (!mutable.TryGetValue(dependencyKey, out var dependents))
                {
                    dependents = [];
                    mutable[dependencyKey] = dependents;
                }

                dependents.Add(item.Key);
            }
        }

        return mutable.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)[.. pair.Value.Distinct(StringComparer.Ordinal)],
            StringComparer.Ordinal);
    }

    internal static string FormatSkillKey(string sourceRepo, string installArg)
        => $"skill:{SkillDependency.CreateKey(sourceRepo, installArg)}";
}

sealed class RecommendationSelectionPrompt(IAnsiConsole console)
{
    internal const int MaxVisibleItems = 24;

    int previousRenderLineCount;

    public async Task<RecommendationSelectionResult> PromptAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        CancellationToken cancellationToken)
    {
        var items = BuildItems(recommendedDirectives, missingSkills);
        RecommendationSelectionState state = new(items);

        while (true)
        {
            Render(items.Count, state);
            var key = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (key is null)
            {
                continue;
            }

            var input = MapKey(key.Value);
            if (input.Command == SkillSelectionCommand.Confirm)
            {
                return new RecommendationSelectionResult(state.SelectedDirectives, state.SelectedSkills);
            }

            state.Apply(input);
        }
    }

    static List<RecommendationSelectionItem> BuildItems(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills)
    {
        List<RecommendationSelectionItem> items = [];
        items.AddRange(recommendedDirectives.Select(directive => new RecommendationSelectionItem(
            $"directive:{directive.Name}",
            FormatDirectiveListItem(directive),
            RecommendationSelectionKind.Directive,
            directive,
            null)));
        items.AddRange(missingSkills.Select(skill => new RecommendationSelectionItem(
            RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg),
            FormatSkillListItem(skill),
            RecommendationSelectionKind.Skill,
            null,
            skill)));
        return items;
    }

    internal static string FormatDirectiveListItem(DirectivePlanItem directive)
        => $"{directive.Name} ({FormatDirectiveAction(directive.Status)})";

    internal static string FormatDirectiveAction(string status)
        => status switch
        {
            DirectiveStatuses.Missing => "install",
            DirectiveStatuses.Outdated => "update",
            _ => DirectiveInstaller.FormatDirectiveStatus(status)
        };

    internal static string FormatSkillListItem(SkillManifestEntry skill)
        => $"{skill.LocalFolder} (install)";

    internal static string FormatSkillSourceHeader(SkillManifestEntry skill)
        => FormatSkillSourceHeader(skill.SourceRepo);

    internal static string FormatSkillSourceHeader(string sourceRepo)
        => $"{sourceRepo} repo";

    internal static string FormatSkillPluginHeader(SkillManifestEntry skill)
        => FormatSkillPluginHeader(skill.Plugin);

    internal static string FormatSkillPluginHeader(string plugin)
        => string.IsNullOrWhiteSpace(plugin) ? "default" : plugin;

    internal static string FormatRecommendationPromptHeading(int itemCount)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Recommend {itemCount} action(s), select which to apply:");

    static SkillSelectionInput MapKey(ConsoleKeyInfo key)
        => key.Key switch
        {
            ConsoleKey.UpArrow => new(SkillSelectionCommand.Up),
            ConsoleKey.DownArrow => new(SkillSelectionCommand.Down),
            ConsoleKey.Spacebar => new(SkillSelectionCommand.Toggle),
            ConsoleKey.RightArrow => new(SkillSelectionCommand.SelectAll),
            ConsoleKey.LeftArrow => new(SkillSelectionCommand.SelectNone),
            ConsoleKey.Backspace => new(SkillSelectionCommand.Backspace),
            ConsoleKey.Escape => new(SkillSelectionCommand.ClearFilter),
            ConsoleKey.Enter => new(SkillSelectionCommand.Confirm),
            _ => new(SkillSelectionCommand.Character, key.KeyChar)
        };

    void Render(int itemCount, RecommendationSelectionState state)
    {
        if (previousRenderLineCount > 0 && !Console.IsOutputRedirected)
        {
            Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - previousRenderLineCount));
        }

        int lineCount = 0;
        void MarkupLine(string value)
        {
            ClearCurrentLine();
            console.MarkupLine(value);
            lineCount++;
        }

        ClearCurrentLine();
        console.WriteLine();
        lineCount++;
        MarkupLine($"[bold]{Markup.Escape(FormatRecommendationPromptHeading(itemCount))}[/]");
        MarkupLine("[grey][[Use arrows to move, space to select, <right> to all, <left> to none, type to filter]][/]");

        if (state.Filter.Length > 0)
        {
            MarkupLine($"Filter: [yellow]{Markup.Escape(state.Filter)}[/]");
        }

        if (state.FilteredItems.Count == 0)
        {
            MarkupLine("[grey]No recommendations match the current filter.[/]");
            FinishRender(lineCount);
            return;
        }

        var (visibleStartIndex, visibleItems) = GetVisibleItems(state.FilteredItems, state.CursorIndex);
        if (visibleItems.Count < state.FilteredItems.Count)
        {
            int visibleEndIndex = visibleStartIndex + visibleItems.Count;
            MarkupLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[grey]Showing {visibleStartIndex + 1}-{visibleEndIndex} of {state.FilteredItems.Count} matches[/]"));
        }

        RecommendationSelectionKind? lastKind = null;
        string? lastSkillSourceRepo = null;
        string? lastSkillPlugin = null;
        string[] visibleSkillSourceReposWithoutPluginHeaders = [.. visibleItems
            .Select(item => item.Skill)
            .OfType<SkillManifestEntry>()
            .GroupBy(skill => skill.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .Where(group => !SkillGroupHeaderPolicy.ShouldShowPluginHeaders(group.Key, group.Select(skill => skill.Plugin)))
            .Select(group => group.Key)];
        for (int index = 0; index < visibleItems.Count; index++)
        {
            int itemIndex = visibleStartIndex + index;
            var item = visibleItems[index];
            if (item.Kind != lastKind)
            {
                MarkupLine($"[bold]{(item.Kind == RecommendationSelectionKind.Directive ? "Directives" : "Skills")}[/]");
                lastKind = item.Kind;
                lastSkillSourceRepo = null;
                lastSkillPlugin = null;
            }

            if (item.Skill is not null)
            {
                string skillSourceRepo = item.Skill.SourceRepo;
                bool showPluginHeaders = !visibleSkillSourceReposWithoutPluginHeaders.Contains(skillSourceRepo, StringComparer.OrdinalIgnoreCase);
                if (!skillSourceRepo.Equals(lastSkillSourceRepo, StringComparison.OrdinalIgnoreCase))
                {
                    MarkupLine($"  [bold]{Markup.Escape(FormatSkillSourceHeader(skillSourceRepo))}[/]");
                    lastSkillSourceRepo = skillSourceRepo;
                    lastSkillPlugin = null;
                }

                string skillPlugin = item.Skill.Plugin;
                if (showPluginHeaders && !skillPlugin.Equals(lastSkillPlugin, StringComparison.OrdinalIgnoreCase))
                {
                    MarkupLine($"    [bold]{Markup.Escape(FormatSkillPluginHeader(skillPlugin))}[/]");
                    lastSkillPlugin = skillPlugin;
                }
            }

            string cursor = itemIndex == state.CursorIndex ? ">" : " ";
            string check = state.IsSelected(item) ? "[[x]]" : "[[ ]]";
            string display = Markup.Escape(item.Display);
            string indent = item.Skill is null ? string.Empty : "    ";
            MarkupLine($"{indent}{cursor} {check} {display}");
        }

        FinishRender(lineCount);
    }

    internal static (int StartIndex, IReadOnlyList<RecommendationSelectionItem> Items) GetVisibleItems(
        IReadOnlyList<RecommendationSelectionItem> items,
        int cursorIndex)
    {
        if (items.Count <= MaxVisibleItems)
        {
            return (0, items);
        }

        int startIndex = Math.Clamp(cursorIndex - (MaxVisibleItems / 2), 0, items.Count - MaxVisibleItems);
        return (startIndex, [.. items.Skip(startIndex).Take(MaxVisibleItems)]);
    }

    void FinishRender(int currentRenderLineCount)
    {
        int renderRegionLineCount = Math.Max(currentRenderLineCount, previousRenderLineCount);
        ClearTrailingLines(currentRenderLineCount, renderRegionLineCount);
        previousRenderLineCount = renderRegionLineCount;
    }

    static void ClearCurrentLine()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.Write('\r');
        Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
        Console.Write('\r');
    }

    static void ClearTrailingLines(int currentRenderLineCount, int renderRegionLineCount)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        for (int index = currentRenderLineCount; index < renderRegionLineCount; index++)
        {
            ClearCurrentLine();
            Console.WriteLine();
        }
    }

}

static class SkillGroupHeaderPolicy
{
    internal static bool ShouldShowPluginHeaders(string sourceRepo, IEnumerable<string> pluginNames)
    {
        string[] names = [.. pluginNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (names.Length != 1)
        {
            return true;
        }

        return !sourceRepo.EndsWith(names[0], StringComparison.OrdinalIgnoreCase);
    }
}
