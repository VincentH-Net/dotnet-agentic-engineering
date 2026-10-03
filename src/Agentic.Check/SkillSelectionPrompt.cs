using System.Text;
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
    ToggleView,
    Preview
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
    string Version = "",
    string? VersionWithoutConsumers = null);

sealed class RecommendationSelectionState(IReadOnlyList<RecommendationSelectionItem> items)
{
    readonly IReadOnlyList<RecommendationSelectionItem> items = items;
    readonly Dictionary<string, IReadOnlyList<string>> dependencyKeysByKey = BuildDependencyKeysByKey(items);
    readonly Dictionary<string, IReadOnlyList<string>> dependentKeysByKey = BuildDependentKeysByKey(items);
    readonly HashSet<string> selectedKeys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
    readonly Dictionary<string, int> ordinalByKey = BuildOrdinalByKey(items);
    readonly HashSet<string> automaticallySelectedKeys = new(StringComparer.Ordinal);
    // Rows present above or below the target that specialization deselects; target-local repairs stay.
    readonly HashSet<string> specializationKeys = new(StringComparer.Ordinal);

    public IReadOnlyList<RecommendationSelectionItem> FilteredItems { get; private set; } = items;

    public string Filter { get; private set; } = string.Empty;

    // The selected view hides unselected rows. An empty selection always shows every row, so the
    // view is never empty. The toggle is offered only while the two views differ: with nothing or
    // everything selected they show the same rows, and F2 does nothing.
    public bool ShowSelectedOnly { get; private set; } = items.Count > 0;

    public bool CanToggleView => selectedKeys.Count > 0 && selectedKeys.Count < items.Count;

    // A typed filter hides part of the selection, so confirming waits until it is cleared and the
    // full selection is back in view. Enter does nothing while a filter is typed.
    public bool CanConfirm => Filter.Length == 0;

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

    // A tool row can carry two requirements: the one its selected consumers bring, and the one
    // already installed, shown while no selected row depends on it. The shorthand is not a consumer.
    public string VersionOf(RecommendationSelectionItem item)
        => item.VersionWithoutConsumers is { } alternative && !HasSelectedConsumer(item) ? alternative : item.Version;

    bool HasSelectedConsumer(RecommendationSelectionItem item)
        => dependentKeysByKey.GetValueOrDefault(item.Key, [])
            .Any(key => selectedKeys.Contains(key) && items.FirstOrDefault(candidate => candidate.Key == key)?.Skill?.IsDna != true);

    public IReadOnlyList<string> GetDuplicateLocations(RecommendationSelectionItem item)
        => DuplicateLocationsByKey.GetValueOrDefault(item.Key, []);

    public int GetDuplicateScopeCount(RecommendationSelectionItem item)
        => DuplicateScopeCountsByKey.GetValueOrDefault(item.Key);

    public bool HasSpecializationRows => specializationKeys.Count > 0;

    public void ApplySpecializationScanResult(ScopeDuplicateScanResult result)
    {
        DuplicateLocationsByKey = result.LocationsByKey;
        DuplicateScopeCountsByKey = result.ScopeCountsByKey;
        specializationKeys.Clear();
        specializationKeys.UnionWith(items
            .Where(item => DuplicateLocationsByKey.ContainsKey(item.Key) && !IsTargetLocalRepair(item))
            .Select(item => item.Key));
        EnableSpecialization();
        Refresh();
    }

    // Tab acts only on the rows present above or below: ON deselects them, OFF selects them back.
    // Every other row keeps the choice the user made, and without such rows Tab does nothing.
    public void ToggleCachedSpecialization()
    {
        if (!HasSpecializationRows)
        {
            return;
        }

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
            case SkillSelectionCommand.ToggleView:
                if (CanToggleView)
                {
                    ShowSelectedOnly = !ShowSelectedOnly;
                }

                break;
            case SkillSelectionCommand.Confirm:
            case SkillSelectionCommand.Specialize:
            case SkillSelectionCommand.OpenHelp:
            case SkillSelectionCommand.Preview:
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
            SelectRow(key);
        }
    }

    // Selects a row as the space bar does: with its dependencies, the shorthand when the companion
    // comes along, and the tools it pulled in remembered as automatic so they leave with it again.
    void SelectRow(string key)
    {
        // A row the user selects on purpose is no longer an automatic one.
        _ = automaticallySelectedKeys.Remove(key);
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

        automaticallySelectedKeys.UnionWith(items
            .Where(item => item.Kind == RecommendationSelectionKind.Tool && item.Skill?.IsRequiredToolRepair != true
                && item.Key != key && selectedKeys.Contains(item.Key) && !previouslySelectedKeys.Contains(item.Key))
            .Select(item => item.Key));
    }

    // Without a filter the arrows act on every row. With one typed they act on the matching rows
    // only, each as the space bar would: selecting brings dependencies along, deselecting takes
    // dependents away.
    void SetAllSelection(bool selected)
    {
        if (Filter.Length == 0)
        {
            automaticallySelectedKeys.Clear();
            if (!selected)
            {
                selectedKeys.Clear();
                return;
            }

            foreach (var item in items)
            {
                SelectWithDependencies(item.Key);
            }

            return;
        }

        foreach (var item in items.Where(item => MatchesFilter(item, Filter)))
        {
            if (selected)
            {
                SelectRow(item.Key);
            }
            else
            {
                DeselectWithDependents(item.Key);
            }
        }
    }

    void EnableSpecialization()
    {
        foreach (string key in specializationKeys)
        {
            DeselectWithDependents(key);
        }

        IsSpecialized = true;
    }

    void DisableSpecialization()
    {
        var previouslySelectedKeys = selectedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (string key in specializationKeys)
        {
            SelectRow(key);
        }

        // Dependencies these rows pulled in are remembered like automatic tools, so the next ON drops
        // them again unless something else still needs them, and a round trip leaves other rows as they were.
        automaticallySelectedKeys.UnionWith(selectedKeys.Except(previouslySelectedKeys).Except(specializationKeys));
        IsSpecialized = false;
    }

    static bool IsTargetLocalRepair(RecommendationSelectionItem item)
        => item.Skill is { IsCompanion: true } or { IsDna: true } or { IsCodexRules: true } or { IsReadmeBadge: true }
            || item.Directive?.Status == DirectiveStatuses.Outdated
            || item.Skill?.ForceInstall == true;

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
        PruneAutomaticSelections(selectedKeys);
        automaticallySelectedKeys.IntersectWith(selectedKeys);
    }

    void PruneAutomaticSelections(HashSet<string> selection)
    {
        var retained = selection.Except(automaticallySelectedKeys).ToHashSet(StringComparer.Ordinal);
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

        selection.ExceptWith(automaticallySelectedKeys.Except(retained));
    }

    // The filter is a lens over the whole list, so typing its first character switches to the all
    // view: every match shows with its real state, and the arrows act on exactly what is shown.
    // The view stays on all after the filter is cleared, until F2.
    void AddFilterCharacter(char character)
    {
        if (char.IsControl(character))
        {
            return;
        }

        if (Filter.Length == 0)
        {
            ShowSelectedOnly = false;
        }

        SetFilter(Filter + character);
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

sealed class RecommendationSelectionPrompt(IAnsiConsole console, IRecommendationPreviewSource? previewSource = null)
{
    // Five rows under their kind, repository and plugin headers: what the list still shows in a very short window.
    internal const int MinListRows = 8;

    // The blank line, title, position and key help above the preview content, and the cursor line below it.
    internal const int PreviewChromeRows = 5;

    int previousRenderLineCount;

    // One fetch per row for the life of the prompt; a failed fetch is retried on the next F3.
    readonly Dictionary<string, RecommendationPreview> previews = new(StringComparer.Ordinal);

    // A one-line message under the key help, shown until the next key.
    string? notice;

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
            notice = null;
            if (input.Command == SkillSelectionCommand.Confirm)
            {
                if (!state.CanConfirm)
                {
                    continue;
                }

                return new RecommendationSelectionResult(state.SelectedDirectives, state.SelectedSkills);
            }

            if (input.Command == SkillSelectionCommand.Preview)
            {
                await PreviewAsync(heading, state, cancellationToken).ConfigureAwait(false);
                continue;
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
            skill.IsCompanion || skill.IsDna || skill.IsCodexRules || skill.IsReadmeBadge ? RecommendationSelectionKind.Tool : RecommendationSelectionKind.Skill,
            null,
            skill,
            skill.Version,
            skill.VersionWithoutConsumers)));
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

    internal static string FormatRecommendationSourceHeaderMarkup(string sourceRepo, string filter = "")
        => $"  [bold {ToolHeader.AgenticColor}]{HighlightMatches(FormatSkillSourceHeader(sourceRepo), filter, MatchColor)}[/]";

    internal static string FormatRecommendationPluginHeaderMarkup(string plugin, string filter = "")
        => $"    [bold {ToolHeader.AgenticColor}]{HighlightMatches(FormatSkillPluginHeader(plugin), filter, MatchColor)}[/]";

    // The typed filter and its matches share one colour, which is also the colour of warning rows.
    const string MatchColor = "yellow";

    // Shows where a typed filter matched by swapping the row's own colour: on plain rows and headers
    // the matches take the filter colour, on warning rows the rest of the text keeps the warning
    // colour and the matches keep the default text colour. The inverted style stays reserved for keys.
    internal static string HighlightMatches(string text, string filter, string? matchStyle, string? restStyle = null)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return StyleSegment(Markup.Escape(text), restStyle);
        }

        StringBuilder markup = new();
        int index = 0;
        while (index < text.Length)
        {
            int match = text.IndexOf(filter, index, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                _ = markup.Append(StyleSegment(Markup.Escape(text[index..]), restStyle));
                break;
            }

            _ = markup.Append(StyleSegment(Markup.Escape(text[index..match]), restStyle));
            _ = markup.Append(StyleSegment(Markup.Escape(text[match..(match + filter.Length)]), matchStyle));
            index = match + filter.Length;
        }

        return markup.ToString();
    }

    static string StyleSegment(string markup, string? style)
        => markup.Length == 0 || style is null ? markup : $"[{style}]{markup}[/]";

    internal static SkillSelectionInput MapKey(ConsoleKeyInfo key)
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
            ConsoleKey.F3 => new(SkillSelectionCommand.Preview),
            _ => new(SkillSelectionCommand.Character, key.KeyChar)
        };

    static RecommendationSelectionItem? CurrentItem(RecommendationSelectionState state)
        => state.CursorIndex < state.FilteredItems.Count ? state.FilteredItems[state.CursorIndex] : null;

    bool CanPreview(RecommendationSelectionState state)
        => previewSource is not null && CurrentItem(state) is { } item && RecommendationPreviewSource.CanPreview(item);

    async Task PreviewAsync(string heading, RecommendationSelectionState state, CancellationToken cancellationToken)
    {
        if (!CanPreview(state) || CurrentItem(state) is not { } item)
        {
            return;
        }

        if (!previews.TryGetValue(item.Key, out var preview))
        {
            notice = $"Loading preview of {Markup.Remove(item.Display)}…";
            Render(heading, state);
            notice = null;
            preview = await previewSource!.LoadAsync(item, cancellationToken).ConfigureAwait(false);
            if (preview.Success)
            {
                previews[item.Key] = preview;
            }
        }

        if (!preview.Success)
        {
            notice = $"Preview unavailable: {preview.Error}";
            return;
        }

        await ShowPreviewAsync(preview, cancellationToken).ConfigureAwait(false);
    }

    // Scrolls the preview on the alternate screen, so closing it leaves the list and everything above it untouched.
    // A terminal without one shows the preview over the list instead, growing that screen region to the window.
    async Task ShowPreviewAsync(RecommendationPreview preview, CancellationToken cancellationToken)
    {
        int width = Math.Max(20, console.Profile.Width - 1);
        string[] lines = [.. WrapLines(preview.Lines, width)];
        int listRenderLineCount = previousRenderLineCount;
        using var alternateScreen = console.Profile.Capabilities is { Ansi: true, AlternateBuffer: true } ? new AlternateScreen(console) : null;
        if (alternateScreen is not null)
        {
            previousRenderLineCount = 0;
        }

        try
        {
            int offset = 0;
            while (true)
            {
                // Read for every render, so a resized window is used from the next key on.
                int pageSize = PreviewPageSize(console.Profile.Height);
                offset = ClampPreviewOffset(offset, lines.Length, pageSize);
                RenderPreview(preview.Title, lines, offset, pageSize);
                var key = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
                if (key is null)
                {
                    continue;
                }

                int? next = NextPreviewOffset(key.Value.Key, offset, pageSize);
                if (next is null)
                {
                    return;
                }

                offset = next.Value;
            }
        }
        finally
        {
            if (alternateScreen is not null)
            {
                previousRenderLineCount = listRenderLineCount;
            }
        }
    }

    // What a pager does: the preview takes the whole window, and leaving puts the screen back as it was.
    sealed class AlternateScreen : IDisposable
    {
        internal const string Enter = "\u001b[?1049h\u001b[H";
        internal const string Leave = "\u001b[?1049l";

        readonly IAnsiConsole console;
        int entered = 1;

        internal AlternateScreen(IAnsiConsole console)
        {
            this.console = console;
            console.Write(new ControlCode(Enter));
            // Ctrl+C ends the process without unwinding to Dispose, which would leave the shell on this screen.
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public void Dispose()
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            LeaveOnce();
        }

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => LeaveOnce();

        void LeaveOnce()
        {
            if (Interlocked.Exchange(ref entered, 0) == 1)
            {
                console.Write(new ControlCode(Leave));
            }
        }
    }

    // A page fills the window. A taller one would scroll the preview's own title off the top.
    internal static int PreviewPageSize(int windowHeight)
        => Math.Max(1, windowHeight - PreviewChromeRows);

    // null closes the preview; any other key keeps it open, scrolling when the key says so.
    // Space and b page like less does, because macOS terminals keep Page Up/Down and Home/End for
    // their own scrollback; those keys still work where the terminal passes them through.
    internal static int? NextPreviewOffset(ConsoleKey key, int offset, int pageSize)
        => key switch
        {
            ConsoleKey.Escape or ConsoleKey.F3 or ConsoleKey.Enter or ConsoleKey.Q => null,
            ConsoleKey.UpArrow => offset - 1,
            ConsoleKey.DownArrow => offset + 1,
            ConsoleKey.PageUp or ConsoleKey.B => offset - pageSize,
            ConsoleKey.PageDown or ConsoleKey.Spacebar => offset + pageSize,
            ConsoleKey.Home => 0,
            ConsoleKey.End => int.MaxValue,
            _ => offset
        };

    internal static int ClampPreviewOffset(int offset, int lineCount, int pageSize)
        => Math.Clamp(offset, 0, Math.Max(0, lineCount - pageSize));

    // Lines longer than the console wrap on screen, which would break the in-place line count;
    // wrapping them up front keeps every rendered row one line.
    internal static IEnumerable<string> WrapLines(IEnumerable<string> lines, int width)
    {
        foreach (string line in lines)
        {
            if (line.Length <= width)
            {
                yield return line;
                continue;
            }

            for (int start = 0; start < line.Length; start += width)
            {
                yield return line.Substring(start, Math.Min(width, line.Length - start));
            }
        }
    }

    internal static string FormatPreviewPositionLine(int offset, int lineCount, int pageSize)
        => lineCount <= pageSize
            ? string.Empty
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Lines {offset + 1}–{Math.Min(lineCount, offset + pageSize)} of {lineCount}");

    internal static string FormatPreviewKeyHelpLine(int lineCount, int pageSize)
        => (lineCount <= pageSize
                ? string.Empty
                : ToolHeader.KeyMarkup("↑") + InfoText(" ") + ToolHeader.KeyMarkup("↓") + InfoText(" scroll, ")
                    + ToolHeader.KeyMarkup("space") + InfoText(" ") + ToolHeader.KeyMarkup("b") + InfoText(" page, "))
            + ToolHeader.KeyMarkup("Esc") + InfoText(" close preview");

    void RenderPreview(string title, string[] lines, int offset, int pageSize)
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
        MarkupLine($"[bold]{Markup.Escape($"Preview: {title}")}[/]");
        string position = FormatPreviewPositionLine(offset, lines.Length, pageSize);
        if (position.Length > 0)
        {
            MarkupLine($"[{SpectreReporter.InfoColor}]{Markup.Escape(position)}[/]");
        }

        MarkupLine(FormatPreviewKeyHelpLine(lines.Length, pageSize));
        if (lines.Length == 0)
        {
            MarkupLine("[grey]The preview is empty.[/]");
        }

        for (int index = offset; index < Math.Min(lines.Length, offset + pageSize); index++)
        {
            MarkupLine(lines[index].Length == 0 ? " " : Markup.Escape(lines[index]));
        }

        FinishRender(lineCount);
    }

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
        foreach (string row in FormatFilterRows(state))
        {
            MarkupLine(row);
        }

        MarkupLine(FormatKeyHelpLine(state, CanPreview(state)));
        if (notice is not null)
        {
            MarkupLine($"[yellow]{Markup.Escape(notice)}[/]");
        }

        if (state.FilteredItems.Count == 0)
        {
            MarkupLine("[grey]No recommendations match the current filter.[/]");
            FinishRender(lineCount);
            return;
        }

        // The list gets the rows the window has left under the lines above, less the cursor line below it.
        var (visibleStartIndex, visibleItems) = GetVisibleItems(
            state.FilteredItems,
            state.CursorIndex,
            Math.Max(MinListRows, console.Profile.Height - 1 - lineCount),
            item => 1 + (ShouldShowDuplicateDetails(state, item) ? 1 + state.GetDuplicateLocations(item).Count : 0));
        int visibleEndIndex = visibleStartIndex + visibleItems.Count;
        if (visibleItems.Count < state.FilteredItems.Count)
        {
            MarkupLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[grey]Items {visibleStartIndex + 1}–{visibleEndIndex} of {state.FilteredItems.Count}[/]"));
        }

        bool showVersionColumn = visibleItems.Any(item => !string.IsNullOrWhiteSpace(item.Version));
        int versionColumnStart = showVersionColumn ? CalculateVersionColumnStart(state.FilteredItems) : 0;
        if (showVersionColumn)
        {
            MarkupLine(FormatColumnHeader(versionColumnStart));
        }

        if (visibleStartIndex > 0)
            MarkupLine(FormatOverflowIndicator(visibleStartIndex, above: true));

        GroupHeaders groupHeaders = new(visibleItems);
        for (int index = 0; index < visibleItems.Count; index++)
        {
            int itemIndex = visibleStartIndex + index;
            var item = visibleItems[index];
            var (kindHeader, sourceHeader, pluginHeader) = groupHeaders.Next(item);
            if (kindHeader)
            {
                MarkupLine(FormatRecommendationKindHeaderMarkup(item.Kind));
            }

            if (sourceHeader)
            {
                MarkupLine(FormatRecommendationSourceHeaderMarkup(item.Skill!.SourceRepo, state.Filter));
            }

            if (pluginHeader)
            {
                MarkupLine(FormatRecommendationPluginHeaderMarkup(item.Skill!.Plugin, state.Filter));
            }

            string cursor = itemIndex == state.CursorIndex ? ">" : " ";
            string checkText = state.IsSelected(item) ? "[x]" : "[ ]";
            string indent = showVersionColumn || item.Skill is not null ? "    " : string.Empty;
            string rowPrefix = $"{indent}{cursor} {checkText} ";
            bool warning = ShouldShowDuplicateWarning(state, item);
            string? matchStyle = warning ? null : MatchColor;
            string? restStyle = warning ? MatchColor : null;
            string prefix = StyleSegment(Markup.Escape(rowPrefix), restStyle);
            string display = HighlightMatches(item.Display, state.Filter, matchStyle, restStyle);
            if (showVersionColumn)
            {
                int padding = Math.Max(1, versionColumnStart - (rowPrefix.Length + item.Display.Length));
                MarkupLine(prefix + display + new string(' ', padding) + HighlightMatches(state.VersionOf(item), state.Filter, matchStyle, restStyle));
            }
            else
            {
                MarkupLine(prefix + display);
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

    static bool ShouldShowDuplicateWarning(RecommendationSelectionState state, RecommendationSelectionItem item)
        => state.IsSpecialized && state.GetDuplicateLocations(item).Count > 0;

    static bool ShouldShowDuplicateDetails(RecommendationSelectionState state, RecommendationSelectionItem item)
        => state.IsSpecialized
            && state.GetDuplicateLocations(item).Count > 0
            && (state.IsSelected(item) || state.GetDuplicateScopeCount(item) > 1);

    const string FiltersLabel = "Filters";

    // The three filter rows share a label column and a key column. The widest text any state can
    // produce sets the key column, so toggling specialization or the view never shifts the keys;
    // only a typed filter longer than that moves them. A key that would do nothing is not offered:
    // Tab without rows above or below, F2 while nothing or everything is selected, Esc while no
    // filter is typed.
    internal static IReadOnlyList<string> FormatFilterRows(RecommendationSelectionState state)
    {
        string specializationState = !state.HasSpecializationRows ? "n/a" : state.IsSpecialized ? "ON" : "OFF";
        string specialization = $"Target directory specialization: {specializationState}";
        string specializationMarkup = $"Target directory specialization: [bold]{specializationState}[/]";
        string specializationHint = state.HasSpecializationRows
            ? ToolHeader.KeyMarkup("Tab") + InfoText(state.IsSpecialized ? " OFF" : " ON")
            : InfoText("(none above or below)");
        string view = FormatViewText(state, state.ShowSelectedOnly);
        string viewWord = state.ShowSelectedOnly ? "selected" : "all";
        string viewMarkup = $"Show: [bold]{viewWord}[/]" + Markup.Escape(view[("Show: " + viewWord).Length..]);
        string filter = state.Filter.Length == 0 ? "Type to filter" : $"Filter: {state.Filter}";
        string filterMarkup = state.Filter.Length == 0 ? filter : $"Filter: [yellow]{Markup.Escape(state.Filter)}[/]";
        int textWidth = new[]
        {
            "Target directory specialization: OFF".Length,
            FormatViewText(state, selectedOnly: true).Length,
            FormatViewText(state, selectedOnly: false).Length,
            filter.Length
        }.Max() + 3;
        string label = $"[bold {ToolHeader.CheckColor}]{FiltersLabel}[/]  ";
        string indent = new(' ', FiltersLabel.Length + 2);
        return
        [
            label + PadPlain(specializationMarkup, specialization.Length, textWidth) + specializationHint,
            indent + PadPlain(viewMarkup, view.Length, textWidth)
                + (state.CanToggleView ? ToolHeader.KeyMarkup("F2") + InfoText(state.ShowSelectedOnly ? " show all" : " show selected") : string.Empty),
            indent + PadPlain(filterMarkup, filter.Length, textWidth)
                + (state.Filter.Length == 0 ? string.Empty : ToolHeader.KeyMarkup("Esc") + InfoText(" clear"))
        ];
    }

    static string FormatViewText(RecommendationSelectionState state, bool selectedOnly)
        => selectedOnly
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Show: selected ({state.SelectedCount} of {state.ItemCount})")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Show: all ({state.SelectedCount} of {state.ItemCount} selected)");

    // Pads by the visible length, since markup tags take no columns.
    static string PadPlain(string markup, int plainLength, int width)
        => markup + new string(' ', Math.Max(1, width - plainLength));

    internal const string ClearFilterToConfirm = "clear the filter before confirming";

    // F3 is offered only while the highlighted row has something to show.
    internal static string FormatKeyHelpLine(RecommendationSelectionState state, bool canPreview = false)
        => ToolHeader.KeyMarkup("↑")
            + InfoText(" ")
            + ToolHeader.KeyMarkup("↓")
            + InfoText(" move, ")
            + ToolHeader.KeyMarkup("space")
            + InfoText(" toggle, ")
            + ToolHeader.KeyMarkup("←")
            + InfoText(state.Filter.Length == 0 ? " deselect all, " : " deselect matching, ")
            + ToolHeader.KeyMarkup("→")
            + InfoText(state.Filter.Length == 0 ? " select all, " : " select matching, ")
            + (canPreview ? ToolHeader.KeyMarkup("F3") + InfoText(" view, ") : string.Empty)
            + (state.CanConfirm ? ToolHeader.KeyMarkup("Enter") + InfoText(" confirm") : InfoText(ClearFilterToConfirm));

    static string InfoText(string value)
        => $"[{SpectreReporter.InfoColor}]{Markup.Escape(value)}[/]";

    // The widest run of items around the cursor whose rows, group headers and paging lines fit the row budget.
    internal static (int StartIndex, IReadOnlyList<RecommendationSelectionItem> Items) GetVisibleItems(
        IReadOnlyList<RecommendationSelectionItem> items,
        int cursorIndex,
        int rowBudget,
        Func<RecommendationSelectionItem, int> itemRows)
    {
        if (ListRows(items, 0, items.Count, itemRows) <= rowBudget)
        {
            return (0, items);
        }

        // Growing in turn below and above the cursor keeps it centered while scrolling.
        int start = Math.Clamp(cursorIndex, 0, items.Count - 1);
        int end = start + 1;
        bool grew = true;
        while (grew)
        {
            grew = false;
            if (end < items.Count && ListRows(items, start, end + 1, itemRows) <= rowBudget)
            {
                end++;
                grew = true;
            }

            if (start > 0 && ListRows(items, start - 1, end, itemRows) <= rowBudget)
            {
                start--;
                grew = true;
            }
        }

        return (start, [.. items.Skip(start).Take(end - start)]);
    }

    // The rows Render writes for these items: the range and column header lines, the overflow indicators,
    // the group headers and the items themselves.
    internal static int ListRows(
        IReadOnlyList<RecommendationSelectionItem> items,
        int start,
        int end,
        Func<RecommendationSelectionItem, int> itemRows)
    {
        RecommendationSelectionItem[] visibleItems = [.. items.Skip(start).Take(end - start)];
        int rows = (visibleItems.Length < items.Count ? 1 : 0)
            + (visibleItems.Any(item => !string.IsNullOrWhiteSpace(item.Version)) ? 1 : 0)
            + (start > 0 ? 1 : 0)
            + (end < items.Count ? 1 : 0);
        GroupHeaders groupHeaders = new(visibleItems);
        foreach (var item in visibleItems)
        {
            var (kindHeader, sourceHeader, pluginHeader) = groupHeaders.Next(item);
            rows += (kindHeader ? 1 : 0) + (sourceHeader ? 1 : 0) + (pluginHeader ? 1 : 0) + itemRows(item);
        }

        return rows;
    }

    // Which group headers each visible item needs above it, for rendering and for measuring alike.
    sealed class GroupHeaders(IReadOnlyList<RecommendationSelectionItem> visibleItems)
    {
        readonly string[] sourceReposWithoutPluginHeaders = [.. visibleItems
            .Select(item => item.Skill)
            .OfType<SkillManifestEntry>()
            .GroupBy(skill => skill.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .Where(group => !SkillGroupHeaderPolicy.ShouldShowPluginHeaders(group.Key, group.Select(skill => skill.Plugin)))
            .Select(group => group.Key)];

        RecommendationSelectionKind? lastKind;
        string? lastSourceRepo;
        string? lastPlugin;

        internal (bool Kind, bool Source, bool Plugin) Next(RecommendationSelectionItem item)
        {
            bool kind = item.Kind != lastKind;
            if (kind)
            {
                lastKind = item.Kind;
                lastSourceRepo = null;
                lastPlugin = null;
            }

            if (item.Skill is not { IsCompanion: false, IsDna: false, IsCodexRules: false, IsReadmeBadge: false } skill)
            {
                return (kind, false, false);
            }

            bool source = !skill.SourceRepo.Equals(lastSourceRepo, StringComparison.OrdinalIgnoreCase);
            if (source)
            {
                lastSourceRepo = skill.SourceRepo;
                lastPlugin = null;
            }

            bool plugin = !sourceReposWithoutPluginHeaders.Contains(skill.SourceRepo, StringComparer.OrdinalIgnoreCase)
                && !skill.Plugin.Equals(lastPlugin, StringComparison.OrdinalIgnoreCase);
            if (plugin)
            {
                lastPlugin = skill.Plugin;
            }

            return (kind, source, plugin);
        }
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
