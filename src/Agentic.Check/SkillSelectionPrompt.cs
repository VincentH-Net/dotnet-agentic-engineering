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
        if (!selectedKeys.Remove(key))
        {
            _ = selectedKeys.Add(key);
        }
    }

    void SetAllSelection(bool selected)
    {
        foreach (var item in items)
        {
            _ = selected ? selectedKeys.Add(item.Key) : selectedKeys.Remove(item.Key);
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
            || item.Skill?.SourceRepo.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
}

sealed class RecommendationSelectionPrompt(IAnsiConsole console)
{
    const int PageSize = 20;
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
            $"skill:{skill.SourceRepo}:{skill.InstallArg}",
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
        => $"{skill.InstallArg} (install)";

    internal static string FormatSkillSourceHeader(SkillManifestEntry skill)
        => skill.SourceRepo;

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
            console.MarkupLine(value);
            lineCount++;
        }

        console.WriteLine();
        lineCount++;
        console.MarkupLineInterpolated($"{FormatRecommendationPromptHeading(itemCount)}");
        lineCount++;
        MarkupLine("[grey][[Use arrows to move, space to select, <right> to all, <left> to none, type to filter]][/]");

        if (state.Filter.Length > 0)
        {
            console.MarkupLineInterpolated($"Filter: [yellow]{Markup.Escape(state.Filter)}[/]");
            lineCount++;
        }

        if (state.FilteredItems.Count == 0)
        {
            MarkupLine("[grey]No recommendations match the current filter.[/]");
            ClearTrailingLines(lineCount);
            previousRenderLineCount = lineCount;
            return;
        }

        int startIndex = Math.Max(0, Math.Min(state.CursorIndex - PageSize + 1, state.FilteredItems.Count - PageSize));
        IReadOnlyList<RecommendationSelectionItem> visibleItems = [.. state.FilteredItems.Skip(startIndex).Take(PageSize)];
        RecommendationSelectionKind? lastKind = null;
        string? lastSkillSourceRepo = null;
        for (int index = 0; index < visibleItems.Count; index++)
        {
            var item = visibleItems[index];
            if (item.Kind != lastKind)
            {
                MarkupLine($"[bold]{(item.Kind == RecommendationSelectionKind.Directive ? "Directives" : "Skills")}[/]");
                lastKind = item.Kind;
                lastSkillSourceRepo = null;
            }

            if (item.Skill is not null)
            {
                string skillSourceRepo = FormatSkillSourceHeader(item.Skill);
                if (!skillSourceRepo.Equals(lastSkillSourceRepo, StringComparison.OrdinalIgnoreCase))
                {
                    MarkupLine($"  [bold]{Markup.Escape(skillSourceRepo)}[/]");
                    lastSkillSourceRepo = skillSourceRepo;
                }
            }

            string cursor = startIndex + index == state.CursorIndex ? ">" : " ";
            string check = state.IsSelected(item) ? "[[x]]" : "[[ ]]";
            string display = Markup.Escape(item.Display);
            string indent = item.Skill is null ? string.Empty : "  ";
            console.MarkupLine($"{indent}{cursor} {check} {display}");
            lineCount++;
        }

        if (state.FilteredItems.Count > PageSize)
        {
            console.MarkupLineInterpolated($"[grey]Showing {PageSize} of {state.FilteredItems.Count} matches.[/]");
            lineCount++;
        }

        ClearTrailingLines(lineCount);
        previousRenderLineCount = lineCount;
    }

    void ClearTrailingLines(int currentRenderLineCount)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        for (int index = currentRenderLineCount; index < previousRenderLineCount; index++)
        {
            Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
            Console.WriteLine();
        }
    }
}
