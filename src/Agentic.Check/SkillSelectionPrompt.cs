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
    Specialize,
    OpenHelp,
    Character,
    PageUp,
    PageDown,
    Home,
    End,
    ToggleView
}

enum RecommendationSelectionKind
{
    Directive,
    Skill,
    Tool
}

readonly record struct SkillSelectionInput(SkillSelectionCommand Command, char Character = '\0');

sealed record RecommendationSelectionItem(
    string Key,
    string Display,
    RecommendationSelectionKind Kind,
    DirectivePlanItem? Directive,
    SkillManifestEntry? Skill,
    string Version = "");

sealed class RecommendationSelectionState(IReadOnlyList<RecommendationSelectionItem> items)
{
    readonly IReadOnlyList<RecommendationSelectionItem> items = items;
    readonly Dictionary<string, IReadOnlyList<string>> dependencyKeysByKey = BuildDependencyKeysByKey(items);
    readonly Dictionary<string, IReadOnlyList<string>> dependentKeysByKey = BuildDependentKeysByKey(items);
    readonly HashSet<string> selectedKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
    readonly Dictionary<string, int> ordinalByKey = BuildOrdinalByKey(items);
    readonly HashSet<string> automaticallySelectedToolKeys = new(StringComparer.Ordinal);
    HashSet<string>? specializedDefaultSelectedKeys;
    HashSet<string> specializedDefaultAutomaticToolKeys = new(StringComparer.Ordinal);

    public IReadOnlyList<RecommendationSelectionItem> FilteredItems { get; private set; } = items;

    public string Filter { get; private set; } = string.Empty;

    // The selected view hides unselected rows. An empty selection always shows every row, so the
    // view is never empty and the toggle is offered only while there is something to show.
    public bool ShowSelectedOnly { get; private set; } = items.Count > 0;

    public bool CanToggleView => selectedKeys.Count > 0;

    public int SelectedCount => selectedKeys.Count;

    public int ItemCount => items.Count;

    public int CursorIndex { get; private set; }

    public bool IsSpecialized { get; private set; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> DuplicateLocationsByKey { get; private set; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> DuplicateScopeCountsByKey { get; private set; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

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

    public IReadOnlyList<string> GetDuplicateLocations(RecommendationSelectionItem item)
        => DuplicateLocationsByKey.GetValueOrDefault(item.Key, []);

    public int GetDuplicateScopeCount(RecommendationSelectionItem item)
        => DuplicateScopeCountsByKey.GetValueOrDefault(item.Key);

    public void ApplySpecializationScanResult(ScopeDuplicateScanResult result)
    {
        DuplicateLocationsByKey = result.LocationsByKey;
        DuplicateScopeCountsByKey = result.ScopeCountsByKey;
        specializedDefaultSelectedKeys = BuildSpecializedDefaultSelection();
        EnableSpecialization();
        Refresh();
    }

    public void ToggleCachedSpecialization()
    {
        if (IsSpecialized)
        {
            DisableSpecialization();
        }
        else
        {
            EnableSpecialization();
        }

        Refresh();
    }

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
            case SkillSelectionCommand.PageUp:
                Move(-RecommendationSelectionPrompt.MaxVisibleItems);
                break;
            case SkillSelectionCommand.PageDown:
                Move(RecommendationSelectionPrompt.MaxVisibleItems);
                break;
            case SkillSelectionCommand.Home:
                MoveTo(0);
                break;
            case SkillSelectionCommand.End:
                MoveTo(FilteredItems.Count - 1);
                break;
            case SkillSelectionCommand.ToggleView:
                if (CanToggleView)
                {
                    ShowSelectedOnly = !ShowSelectedOnly;
                }

                break;
            case SkillSelectionCommand.Confirm:
            case SkillSelectionCommand.Specialize:
            case SkillSelectionCommand.OpenHelp:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Command, "Unsupported selection input.");
        }

        Refresh();
    }

    void Move(int delta)
        => MoveTo(CursorIndex + delta);

    void MoveTo(int index)
        => CursorIndex = FilteredItems.Count == 0 ? 0 : Math.Clamp(index, 0, FilteredItems.Count - 1);

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
            var previouslySelectedKeys = selectedKeys.ToHashSet(StringComparer.Ordinal);
            bool companionSelected = SelectedSkills.Any(skill => skill.IsCompanion);
            SelectWithDependencies(key);
            // Default the optional shorthand on when the companion becomes selected. This is
            // a UI default, not a reverse dependency; later dependency closure preserves opt-out.
            if (!companionSelected && SelectedSkills.Any(skill => skill.IsCompanion))
            {
                var dna = items.FirstOrDefault(item => item.Skill?.IsDna == true);
                if (dna is not null)
                    SelectWithDependencies(dna.Key);
            }

            automaticallySelectedToolKeys.UnionWith(items
                .Where(item => item.Kind == RecommendationSelectionKind.Tool && item.Skill?.IsRequiredToolRepair != true
                    && item.Key != key && selectedKeys.Contains(item.Key) && !previouslySelectedKeys.Contains(item.Key))
                .Select(item => item.Key));
        }
    }

    void SetAllSelection(bool selected)
    {
        automaticallySelectedToolKeys.Clear();
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

    void EnableSpecialization()
    {
        specializedDefaultSelectedKeys ??= BuildSpecializedDefaultSelection();
        selectedKeys.Clear();
        foreach (string key in specializedDefaultSelectedKeys)
        {
            _ = selectedKeys.Add(key);
        }

        automaticallySelectedToolKeys.Clear();
        automaticallySelectedToolKeys.UnionWith(specializedDefaultAutomaticToolKeys);

        IsSpecialized = true;
    }

    void DisableSpecialization()
        => IsSpecialized = false;

    static bool IsTargetLocalRepair(RecommendationSelectionItem item)
        => item.Skill is { IsCompanion: true } or { IsDna: true } or { IsCodexRules: true }
            || item.Directive?.Status == DirectiveStatuses.Outdated
            || item.Skill?.ForceInstall == true;

    HashSet<string> BuildSpecializedDefaultSelection()
    {
        var specializedSelection = selectedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var item in items.Where(item => DuplicateLocationsByKey.ContainsKey(item.Key) && !IsTargetLocalRepair(item)))
        {
            RemoveWithDependents(specializedSelection, item.Key);
        }

        PruneAutomaticTools(specializedSelection);
        specializedDefaultAutomaticToolKeys = automaticallySelectedToolKeys.Intersect(specializedSelection).ToHashSet(StringComparer.Ordinal);

        return specializedSelection;
    }

    void RemoveWithDependents(HashSet<string> selection, string key)
    {
        if (!selection.Remove(key))
        {
            return;
        }

        foreach (string dependentKey in dependentKeysByKey.GetValueOrDefault(key, []))
        {
            RemoveWithDependents(selection, dependentKey);
        }
    }

    internal void SelectWithDependencies(string key)
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

    internal void DeselectWithDependents(string key)
    {
        RemoveWithDependents(selectedKeys, key);
        PruneAutomaticTools(selectedKeys);
        automaticallySelectedToolKeys.IntersectWith(selectedKeys);
    }

    void PruneAutomaticTools(HashSet<string> selection)
    {
        var retained = selection.Except(automaticallySelectedToolKeys).ToHashSet(StringComparer.Ordinal);
        Queue<string> pending = new(retained);
        while (pending.TryDequeue(out string? key))
        {
            foreach (string dependencyKey in dependencyKeysByKey.GetValueOrDefault(key, []))
            {
                if (selection.Contains(dependencyKey) && retained.Add(dependencyKey))
                    pending.Enqueue(dependencyKey);
            }
        }

        // The optional shorthand follows a retained companion, but must not keep an
        // otherwise unused, automatically selected companion alive through its dependency.
        if (items.Any(item => item.Skill?.IsCompanion == true && retained.Contains(item.Key)))
            retained.UnionWith(items.Where(item => item.Skill?.IsDna == true && selection.Contains(item.Key)).Select(item => item.Key));

        selection.ExceptWith(automaticallySelectedToolKeys.Except(retained));
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
        => Filter = filter;

    // Recomputes the rows for the current view and text filter. The cursor stays on its row while
    // that row is shown and otherwise moves to the next shown row, so a row that disappears under
    // the cursor is replaced by the one after it.
    void Refresh()
    {
        if (selectedKeys.Count == 0)
        {
            ShowSelectedOnly = false;
        }

        var cursorItem = CursorIndex < FilteredItems.Count ? FilteredItems[CursorIndex] : null;
        FilteredItems = [.. items.Where(IsShown)];
        int ordinal = cursorItem is null ? 0 : ordinalByKey[cursorItem.Key];
        int index = FilteredItems.Count - 1;
        for (int candidate = 0; candidate < FilteredItems.Count; candidate++)
        {
            if (ordinalByKey[FilteredItems[candidate].Key] >= ordinal)
            {
                index = candidate;
                break;
            }
        }

        MoveTo(index);
    }

    bool IsShown(RecommendationSelectionItem item)
        => (!ShowSelectedOnly || selectedKeys.Contains(item.Key))
            && (string.IsNullOrWhiteSpace(Filter) || MatchesFilter(item, Filter));

    static bool MatchesFilter(RecommendationSelectionItem item, string filter)
        => item.Display.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || item.Version.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || item.Skill?.SourceRepo.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
            || item.Skill?.Plugin.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    static Dictionary<string, int> BuildOrdinalByKey(IReadOnlyList<RecommendationSelectionItem> items)
    {
        Dictionary<string, int> ordinalByKey = new(StringComparer.Ordinal);
        for (int index = 0; index < items.Count; index++)
        {
            _ = ordinalByKey.TryAdd(items[index].Key, index);
        }

        return ordinalByKey;
    }

    static Dictionary<string, IReadOnlyList<string>> BuildDependencyKeysByKey(IReadOnlyList<RecommendationSelectionItem> items)
    {
        var selectableKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<string>> dependencyKeysByKey = new(StringComparer.Ordinal);
        foreach (var item in items)
        {
            string[] dependencyKeys = [.. (item.Skill?.Dependencies ?? CompanionDependency.ForDirective(item.Directive?.Name, item.Directive?.Content))
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
        => sourceRepo.Length == 0 && installArg == CompanionDependency.PackageId
            ? "tool:" + CompanionDependency.PackageId
            : $"skill:{SkillDependency.CreateKey(sourceRepo, installArg)}";

    internal static string FormatDirectiveKey(string name)
        => $"directive:{name}";
}

sealed class RecommendationSelectionPrompt(IAnsiConsole console)
{
    internal const int MaxVisibleItems = 24;

    int previousRenderLineCount;

    public async Task<RecommendationSelectionResult> PromptAsync(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills,
        ScopeDuplicateScanResult duplicates,
        PresentElsewhere presentElsewhere,
        CancellationToken cancellationToken)
    {
        var items = BuildItems(recommendedDirectives, missingSkills);
        RecommendationSelectionState state = new(items);
        state.ApplySpecializationScanResult(duplicates);
        string heading = FormatRecommendationPromptHeading(state.SelectedCount, items.Count, presentElsewhere);

        while (true)
        {
            Render(heading, state);
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

            if (input.Command == SkillSelectionCommand.Specialize)
            {
                state.ToggleCachedSpecialization();
                continue;
            }

            if (input.Command == SkillSelectionCommand.OpenHelp)
            {
                _ = BrowserLauncher.Open(ToolHeader.RepositoryUrl);
                continue;
            }

            state.Apply(input);
        }
    }

    // The selection a run starts from: everything except what the duplicate scan found above or below the
    // target. Non-interactive runs apply exactly this, so --yes never installs a second copy in a subfolder.
    internal static RecommendationSelectionResult DefaultSelection(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> recommendedSkills,
        ScopeDuplicateScanResult duplicates)
    {
        RecommendationSelectionState state = new(BuildItems(recommendedDirectives, recommendedSkills));
        state.ApplySpecializationScanResult(duplicates);
        return new(state.SelectedDirectives, state.SelectedSkills);
    }

    internal static List<RecommendationSelectionItem> BuildItems(
        IReadOnlyList<DirectivePlanItem> recommendedDirectives,
        IReadOnlyList<SkillManifestEntry> missingSkills)
    {
        List<RecommendationSelectionItem> items = [];
        items.AddRange(recommendedDirectives.Select(directive => new RecommendationSelectionItem(
            RecommendationSelectionState.FormatDirectiveKey(directive.Name),
            FormatDirectiveListItem(directive),
            RecommendationSelectionKind.Directive,
            directive,
            null,
            directive.Version)));
        items.AddRange(missingSkills.Select(skill => new RecommendationSelectionItem(
            RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg),
            FormatSkillListItem(skill),
            skill.IsCompanion || skill.IsDna || skill.IsCodexRules ? RecommendationSelectionKind.Tool : RecommendationSelectionKind.Skill,
            null,
            skill,
            skill.Version)));
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
        => $"{skill.LocalFolder} ({skill.RecommendationAction})";

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

    // States the default recommendation with the same counts and words as the summary table.
    internal static string FormatRecommendationPromptHeading(int recommendedCount, int itemCount, PresentElsewhere presentElsewhere)
    {
        if (recommendedCount == itemCount && presentElsewhere.Count == 0)
        {
            return FormatRecommendationPromptHeading(itemCount);
        }

        string elsewhere = presentElsewhere.Count == 0
            ? string.Empty
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $", {presentElsewhere.Count} already {presentElsewhere.Status}");
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Recommend {recommendedCount} of {itemCount} action(s){elsewhere}, select which to apply:");
    }

    internal static string FormatRecommendationKindHeaderMarkup(RecommendationSelectionKind kind)
        => $"[bold {ToolHeader.CheckColor}]{(kind == RecommendationSelectionKind.Directive ? "Directives" : kind == RecommendationSelectionKind.Tool ? "Tools" : "Skills")}[/]";

    internal static string FormatRecommendationSourceHeaderMarkup(string sourceRepo)
        => $"  [bold {ToolHeader.AgenticColor}]{Markup.Escape(FormatSkillSourceHeader(sourceRepo))}[/]";

    internal static string FormatRecommendationPluginHeaderMarkup(string plugin)
        => $"    [bold {ToolHeader.AgenticColor}]{Markup.Escape(FormatSkillPluginHeader(plugin))}[/]";

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
            ConsoleKey.Tab => new(SkillSelectionCommand.Specialize),
            ConsoleKey.F1 => new(SkillSelectionCommand.OpenHelp),
            ConsoleKey.F2 => new(SkillSelectionCommand.ToggleView),
            ConsoleKey.PageUp => new(SkillSelectionCommand.PageUp),
            ConsoleKey.PageDown => new(SkillSelectionCommand.PageDown),
            ConsoleKey.Home => new(SkillSelectionCommand.Home),
            ConsoleKey.End => new(SkillSelectionCommand.End),
            _ => new(SkillSelectionCommand.Character, key.KeyChar)
        };

    void Render(string heading, RecommendationSelectionState state)
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
        MarkupLine($"[bold]{Markup.Escape(heading)}[/]");
        MarkupLine(FormatSpecializationHelpLine(state));
        MarkupLine(FormatViewHelpLine(state));
        MarkupLine(FormatMoveHelpLine());
        MarkupLine(FormatFilterHelpLine());

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

        var (visibleStartIndex, visibleItems) = GetVisibleItems(
            state.FilteredItems,
            state.CursorIndex,
            item => 1 + (ShouldShowDuplicateDetails(state, item) ? 1 + state.GetDuplicateLocations(item).Count : 0));
        int visibleEndIndex = visibleStartIndex + visibleItems.Count;
        if (visibleItems.Count < state.FilteredItems.Count)
        {
            MarkupLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[grey]Items {visibleStartIndex + 1}–{visibleEndIndex} of {state.FilteredItems.Count}[/]"));
        }

        RecommendationSelectionKind? lastKind = null;
        string? lastSkillSourceRepo = null;
        string? lastSkillPlugin = null;
        bool showVersionColumn = visibleItems.Any(item => !string.IsNullOrWhiteSpace(item.Version));
        int versionColumnStart = showVersionColumn ? CalculateVersionColumnStart(state.FilteredItems) : 0;
        if (showVersionColumn)
        {
            MarkupLine(FormatColumnHeader(versionColumnStart));
        }

        if (visibleStartIndex > 0)
            MarkupLine(FormatOverflowIndicator(visibleStartIndex, above: true));

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
                MarkupLine(FormatRecommendationKindHeaderMarkup(item.Kind));

                lastKind = item.Kind;
                lastSkillSourceRepo = null;
                lastSkillPlugin = null;
            }

            if (item.Skill is { IsCompanion: false, IsDna: false, IsCodexRules: false })
            {
                string skillSourceRepo = item.Skill.SourceRepo;
                bool showPluginHeaders = !visibleSkillSourceReposWithoutPluginHeaders.Contains(skillSourceRepo, StringComparer.OrdinalIgnoreCase);
                if (!skillSourceRepo.Equals(lastSkillSourceRepo, StringComparison.OrdinalIgnoreCase))
                {
                    MarkupLine(FormatRecommendationSourceHeaderMarkup(skillSourceRepo));
                    lastSkillSourceRepo = skillSourceRepo;
                    lastSkillPlugin = null;
                }

                string skillPlugin = item.Skill.Plugin;
                if (showPluginHeaders && !skillPlugin.Equals(lastSkillPlugin, StringComparison.OrdinalIgnoreCase))
                {
                    MarkupLine(FormatRecommendationPluginHeaderMarkup(skillPlugin));
                    lastSkillPlugin = skillPlugin;
                }
            }

            string cursor = itemIndex == state.CursorIndex ? ">" : " ";
            string checkText = state.IsSelected(item) ? "[x]" : "[ ]";
            string check = Markup.Escape(checkText);
            string indent = showVersionColumn || item.Skill is not null ? "    " : string.Empty;
            string rowPrefix = $"{indent}{cursor} {checkText} ";
            string display = Markup.Escape(item.Display);
            if (showVersionColumn)
            {
                int padding = Math.Max(1, versionColumnStart - (rowPrefix.Length + item.Display.Length));
                MarkupLine(FormatItemLine(
                    $"{indent}{cursor} {check} {display}{new string(' ', padding)}{Markup.Escape(item.Version)}",
                    ShouldShowDuplicateWarning(state, item)));
            }
            else
            {
                MarkupLine(FormatItemLine($"{indent}{cursor} {check} {display}", ShouldShowDuplicateWarning(state, item)));
            }

            var duplicateLocations = state.GetDuplicateLocations(item);
            if (ShouldShowDuplicateDetails(state, item))
            {
                MarkupLine($"[yellow]{Markup.Escape($"{indent}    Duplicate(s) that prevent specialization:")}[/]");
                foreach (string location in duplicateLocations)
                {
                    MarkupLine($"[yellow]{Markup.Escape($"{indent}      {location}")}[/]");
                }
            }
        }

        int hiddenBelow = state.FilteredItems.Count - visibleEndIndex;
        if (hiddenBelow > 0)
            MarkupLine(FormatOverflowIndicator(hiddenBelow, above: false));

        FinishRender(lineCount);
    }

    static string FormatOverflowIndicator(int count, bool above)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[bold {ToolHeader.AgenticColor}]{(above ? "↑" : "↓")} {count} more {(count == 1 ? "item" : "items")} {(above ? "above" : "below")}[/]");

    static int CalculateVersionColumnStart(IReadOnlyList<RecommendationSelectionItem> visibleItems)
        => visibleItems
            .Select(item =>
            {
                string indent = "    ";
                return indent.Length + "> [x] ".Length + item.Display.Length + 2;
            })
            .DefaultIfEmpty(0)
            .Max();

    static string FormatColumnHeader(int versionColumnStart)
    {
        const string prefix = "          ";
        int padding = Math.Max(1, versionColumnStart - (prefix.Length + "Name".Length));
        return $"{prefix}[bold]Name[/]{new string(' ', padding)}[bold]Version[/]";
    }

    static string FormatItemLine(string markup, bool warning)
        => warning ? $"[yellow]{markup}[/]" : markup;

    static bool ShouldShowDuplicateWarning(RecommendationSelectionState state, RecommendationSelectionItem item)
        => state.IsSpecialized && state.GetDuplicateLocations(item).Count > 0;

    static bool ShouldShowDuplicateDetails(RecommendationSelectionState state, RecommendationSelectionItem item)
        => state.IsSpecialized
            && state.GetDuplicateLocations(item).Count > 0
            && (state.IsSelected(item) || state.GetDuplicateScopeCount(item) > 1);

    static string FormatSpecializationHelpLine(RecommendationSelectionState state)
    {
        string status = state.IsSpecialized ? "ON" : "OFF";
        return $"Target directory specialization: [bold]{status}[/] "
            + ToolHeader.KeyMarkup("Tab")
            + InfoText(" to toggle");
    }

    internal static string FormatViewHelpLine(RecommendationSelectionState state)
    {
        string counts = state.ShowSelectedOnly
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" ({state.SelectedCount} of {state.ItemCount})")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $" ({state.SelectedCount} of {state.ItemCount} selected)");
        string line = $"Show: [bold]{(state.ShowSelectedOnly ? "selected" : "all")}[/]{counts}";
        return state.CanToggleView
            ? line + " " + ToolHeader.KeyMarkup("F2") + InfoText(state.ShowSelectedOnly ? " to show all" : " to show selected")
            : line;
    }

    static string FormatMoveHelpLine()
        => InfoText("Use ")
            + ToolHeader.KeyMarkup("↑")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("↓")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("PgUp")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("PgDn")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("Home")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("End")
            + InfoText(" to move, ")
            + ToolHeader.KeyMarkup("space")
            + InfoText(" to select, ")
            + ToolHeader.KeyMarkup("←")
            + InfoText(" to none, ")
            + ToolHeader.KeyMarkup("→")
            + InfoText(" to all");

    static string FormatFilterHelpLine()
        => InfoText("Type to filter, ")
            + ToolHeader.KeyMarkup("Esc")
            + InfoText(" to clear, ")
            + ToolHeader.KeyMarkup("Enter")
            + InfoText(" to confirm");

    static string InfoText(string value)
        => $"[{SpectreReporter.InfoColor}]{Markup.Escape(value)}[/]";

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

    internal static (int StartIndex, IReadOnlyList<RecommendationSelectionItem> Items) GetVisibleItems(
        IReadOnlyList<RecommendationSelectionItem> items,
        int cursorIndex,
        Func<RecommendationSelectionItem, int> visualRowCount)
    {
        int totalRows = items.Sum(visualRowCount);
        if (totalRows <= MaxVisibleItems)
        {
            return (0, items);
        }

        int startIndex = Math.Clamp(cursorIndex - (MaxVisibleItems / 2), 0, Math.Max(0, items.Count - 1));
        while (startIndex > 0 && VisualRows(items, startIndex, cursorIndex, visualRowCount) < MaxVisibleItems / 2)
        {
            startIndex--;
        }

        List<RecommendationSelectionItem> visibleItems = [];
        int visibleRows = 0;
        for (int index = startIndex; index < items.Count; index++)
        {
            int itemRows = visualRowCount(items[index]);
            if (visibleItems.Count > 0 && visibleRows + itemRows > MaxVisibleItems)
            {
                break;
            }

            visibleItems.Add(items[index]);
            visibleRows += itemRows;
        }

        if (!visibleItems.Contains(items[cursorIndex]) && cursorIndex < items.Count)
        {
            var (_, fallbackItems) = GetVisibleItems([.. items.Skip(cursorIndex)], 0, visualRowCount);
            return (cursorIndex, fallbackItems);
        }

        return (startIndex, visibleItems);
    }

    static int VisualRows(
        IReadOnlyList<RecommendationSelectionItem> items,
        int startIndex,
        int endIndex,
        Func<RecommendationSelectionItem, int> visualRowCount)
        => items
            .Skip(startIndex)
            .Take(Math.Max(0, endIndex - startIndex + 1))
            .Sum(visualRowCount);

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
