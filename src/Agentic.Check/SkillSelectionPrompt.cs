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

readonly record struct SkillSelectionInput(SkillSelectionCommand Command, char Character = '\0');

sealed class SkillSelectionState(IReadOnlyList<SkillManifestEntry> skills)
{
    readonly IReadOnlyList<SkillManifestEntry> skills = skills;
    readonly HashSet<string> selectedDisplays = skills.Select(skill => skill.Display).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<SkillManifestEntry> FilteredSkills { get; private set; } = skills;

    public string Filter { get; private set; } = string.Empty;

    public int CursorIndex { get; private set; }

    public IReadOnlyList<SkillManifestEntry> SelectedSkills
        => [.. skills.Where(skill => selectedDisplays.Contains(skill.Display))];

    public bool IsSelected(SkillManifestEntry skill)
        => selectedDisplays.Contains(skill.Display);

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
        if (FilteredSkills.Count == 0)
        {
            CursorIndex = 0;
            return;
        }

        CursorIndex = Math.Clamp(CursorIndex + delta, 0, FilteredSkills.Count - 1);
    }

    void ToggleCurrent()
    {
        if (FilteredSkills.Count == 0)
        {
            return;
        }

        string display = FilteredSkills[CursorIndex].Display;
        if (!selectedDisplays.Remove(display))
        {
            _ = selectedDisplays.Add(display);
        }
    }

    void SetAllSelection(bool selected)
    {
        foreach (var skill in skills)
        {
            _ = selected ? selectedDisplays.Add(skill.Display) : selectedDisplays.Remove(skill.Display);
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
        FilteredSkills = string.IsNullOrWhiteSpace(Filter)
            ? skills
            : [.. skills.Where(skill => skill.Display.Contains(Filter, StringComparison.OrdinalIgnoreCase))];
        CursorIndex = Math.Min(CursorIndex, Math.Max(FilteredSkills.Count - 1, 0));
    }
}

sealed class SkillSelectionPrompt(IAnsiConsole console)
{
    const int PageSize = 20;

    public async Task<IReadOnlyList<SkillManifestEntry>> PromptAsync(
        IReadOnlyList<SkillManifestEntry> missingSkills,
        CancellationToken cancellationToken)
    {
        var state = new SkillSelectionState(missingSkills);

        while (true)
        {
            Render(missingSkills.Count, state);
            var key = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (key is null)
            {
                continue;
            }

            var input = MapKey(key.Value);
            if (input.Command == SkillSelectionCommand.Confirm)
            {
                return state.SelectedSkills;
            }

            state.Apply(input);
        }
    }

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

    void Render(int missingSkillCount, SkillSelectionState state)
    {
        console.Clear(false);
        console.MarkupLineInterpolated($"Found {missingSkillCount} recommended skills missing, select skill(s) to install:");
        console.MarkupLine("[grey][[Use arrows to move, space to select, <right> to all, <left> to none, type to filter]][/]");

        if (state.Filter.Length > 0)
        {
            console.MarkupLineInterpolated($"Filter: [yellow]{Markup.Escape(state.Filter)}[/]");
        }

        if (state.FilteredSkills.Count == 0)
        {
            console.MarkupLine("[grey]No skills match the current filter.[/]");
            return;
        }

        int startIndex = Math.Max(0, Math.Min(state.CursorIndex - PageSize + 1, state.FilteredSkills.Count - PageSize));
        IReadOnlyList<SkillManifestEntry> visibleSkills = [.. state.FilteredSkills.Skip(startIndex).Take(PageSize)];
        for (int index = 0; index < visibleSkills.Count; index++)
        {
            var skill = visibleSkills[index];
            string cursor = startIndex + index == state.CursorIndex ? ">" : " ";
            string check = state.IsSelected(skill) ? "[[x]]" : "[[ ]]";
            string display = Markup.Escape(skill.Display);
            console.MarkupLine($"{cursor} {check} {display}");
        }

        if (state.FilteredSkills.Count > PageSize)
        {
            console.MarkupLineInterpolated($"[grey]Showing {PageSize} of {state.FilteredSkills.Count} matches.[/]");
        }
    }
}
