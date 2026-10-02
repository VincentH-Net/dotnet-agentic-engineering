using Spectre.Console.Testing;

namespace Agentic.Check.Tests;

public sealed class RecommendationPreviewTests
{
    const string Repository = "owner/repo";
    const string SkillName = "dotnet-livecharts2";
    const string WorkingDirectory = "/work";

    // What gh skill preview prints when its output is captured: a file tree, then rendered markdown
    // padded to the wrap width, plus a hidden-directory notice on stderr.
    const string GhPreviewOutput = "[plugins] dotnet/dotnet-livecharts2/\n└── SKILL.md\n\n# LiveCharts2 Development Guide      \n\x1b[1mUse\x1b[0m when implementing charts.\n\n\n";
    const string GhHiddenNotice = "! 3 skill(s) in hidden directories were excluded, use --allow-hidden-dirs to include them\n";

    [Theory]
    [InlineData(null, SkillName)]
    [InlineData("v1.2.3", SkillName + "@v1.2.3")]
    public void PreviewArgumentsAppendTheRefAnInstallWouldUse(string? resolvedRef, string expected)
        => Assert.Equal(["skill", "preview", Repository, expected], RecommendationPreviewSource.PreviewArguments(Skill(resolvedRef)));

    [Fact]
    public async Task DirectivePreviewUsesThePlannedContentWithoutCallingGh()
    {
        FakeCommandRunner runner = new();
        RecommendationPreviewSource source = new(runner, WorkingDirectory);

        var preview = await source.LoadAsync(DirectiveItem("foundation-prompt-log", "## Prompt Log\r\nBody line.   \n\n"), CancellationToken.None);

        Assert.True(preview.Success);
        Assert.Equal("foundation-prompt-log directive", preview.Title);
        Assert.Equal(["## Prompt Log", "Body line."], preview.Lines);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SkillPreviewCapturesGhSkillPreviewWithoutAPagerAndStripsEscapes()
    {
        FakeCommandRunner runner = new();
        runner.Enqueue(new(0, GhPreviewOutput, GhHiddenNotice));
        RecommendationPreviewSource source = new(runner, WorkingDirectory);

        var preview = await source.LoadAsync(SkillItem(Skill("v1.2.3")), CancellationToken.None);

        Assert.True(preview.Success);
        Assert.Equal($"{SkillName} from {Repository} at v1.2.3", preview.Title);
        Assert.Equal(["[plugins] dotnet/dotnet-livecharts2/", "└── SKILL.md", "", "# LiveCharts2 Development Guide", "Use when implementing charts."], preview.Lines);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("gh", call.FileName);
        Assert.Equal(["skill", "preview", Repository, $"{SkillName}@v1.2.3"], call.Arguments);
        Assert.Equal(WorkingDirectory, call.WorkingDirectory);
        Assert.Equal("cat", call.Environment?["GH_PAGER"]);
    }

    [Theory]
    [InlineData(1, "", "! 3 skill(s) in hidden directories were excluded\nskill \"dotnet-livecharts2\" not found in owner/repo\n", "skill \"dotnet-livecharts2\" not found in owner/repo")]
    [InlineData(2, "", "", "gh skill preview exited with code 2.")]
    public async Task SkillPreviewFailureReportsGhErrorWithoutNotices(int exitCode, string standardOutput, string standardError, string expectedError)
    {
        FakeCommandRunner runner = new();
        runner.Enqueue(new(exitCode, standardOutput, standardError));
        RecommendationPreviewSource source = new(runner, WorkingDirectory);

        var preview = await source.LoadAsync(SkillItem(Skill()), CancellationToken.None);

        Assert.False(preview.Success);
        Assert.Equal(expectedError, preview.Error);
        Assert.Equal($"{SkillName} from {Repository}", preview.Title);
        Assert.Empty(preview.Lines);
    }

    [Fact]
    public async Task ToolRowsHaveNoPreview()
    {
        var item = new RecommendationSelectionItem("tool", "InnoWvate.Agentic (install)", RecommendationSelectionKind.Tool, null, Skill());
        FakeCommandRunner runner = new();

        Assert.False(RecommendationPreviewSource.CanPreview(item));
        var preview = await new RecommendationPreviewSource(runner, WorkingDirectory).LoadAsync(item, CancellationToken.None);

        Assert.False(preview.Success);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void F3MapsToPreviewWhichLeavesTheSelectionAlone()
    {
        Assert.Equal(SkillSelectionCommand.Preview, RecommendationSelectionPrompt.MapKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false)).Command);
        RecommendationSelectionState state = new([SkillItem(Skill()), DirectiveItem("foundation-prompt-log", "content")]);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Preview));

        Assert.Equal(2, state.SelectedCount);
        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void KeyHelpLineOffersF3OnlyForRowsThatCanBePreviewed()
    {
        RecommendationSelectionState state = new([SkillItem(Skill())]);

        Assert.Contains("F3 view, Enter confirm", Spectre.Console.Markup.Remove(RecommendationSelectionPrompt.FormatKeyHelpLine(state, canPreview: true)), StringComparison.Ordinal);
        Assert.DoesNotContain("F3", Spectre.Console.Markup.Remove(RecommendationSelectionPrompt.FormatKeyHelpLine(state)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, 5, null)]
    [InlineData(ConsoleKey.F3, 5, null)]
    [InlineData(ConsoleKey.Enter, 5, null)]
    [InlineData(ConsoleKey.Q, 5, null)]
    [InlineData(ConsoleKey.UpArrow, 5, 4)]
    [InlineData(ConsoleKey.DownArrow, 5, 6)]
    [InlineData(ConsoleKey.PageUp, 30, 6)]
    [InlineData(ConsoleKey.PageDown, 5, 29)]
    [InlineData(ConsoleKey.Spacebar, 5, 29)]
    [InlineData(ConsoleKey.Home, 5, 0)]
    [InlineData(ConsoleKey.End, 5, int.MaxValue)]
    [InlineData(ConsoleKey.A, 5, 5)]
    public void PreviewKeysScrollOrClose(ConsoleKey key, int offset, int? expected)
        => Assert.Equal(expected, RecommendationSelectionPrompt.NextPreviewOffset(key, offset));

    [Theory]
    [InlineData(-3, 10, 0)]
    [InlineData(99, 10, 0)]
    [InlineData(99, 30, 6)]
    [InlineData(3, 30, 3)]
    public void PreviewOffsetStaysWithinTheContent(int offset, int lineCount, int expected)
        => Assert.Equal(expected, RecommendationSelectionPrompt.ClampPreviewOffset(offset, lineCount));

    [Fact]
    public void LongLinesWrapToTheConsoleWidthSoEveryRowIsOneLine()
        => Assert.Equal(
            ["short", "0123456789", "abcdefghij", "k", ""],
            RecommendationSelectionPrompt.WrapLines(["short", "0123456789abcdefghijk", ""], 10));

    [Fact]
    public void PositionAndKeyHelpAppearOnlyWhenThePreviewScrolls()
    {
        Assert.Equal(string.Empty, RecommendationSelectionPrompt.FormatPreviewPositionLine(0, RecommendationSelectionPrompt.PreviewPageSize));
        Assert.Equal("Lines 7–30 of 30", RecommendationSelectionPrompt.FormatPreviewPositionLine(6, 30));
        Assert.Equal("Esc close preview", Spectre.Console.Markup.Remove(RecommendationSelectionPrompt.FormatPreviewKeyHelpLine(3)));
        Assert.Equal("↑ ↓ scroll, PgUp PgDn page, Esc close preview", Spectre.Console.Markup.Remove(RecommendationSelectionPrompt.FormatPreviewKeyHelpLine(30)));
    }

    [Fact]
    public async Task PromptShowsTheSkillPreviewOnF3AndRedrawsTheListOnEscape()
    {
        using var console = CreateConsole();
        FakeCommandRunner runner = new();
        runner.Enqueue(new(0, GhPreviewOutput, GhHiddenNotice));
        var skill = Skill("v1.2.3");
        console.Input.PushKey(ConsoleKey.F3);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await Prompt(console, runner).PromptAsync([], [skill], NoDuplicates(), NothingElsewhere(), CancellationToken.None);

        Assert.Equal([skill], result.SelectedSkills);
        Assert.Contains("F3 view, Enter confirm", console.Output, StringComparison.Ordinal);
        Assert.Contains($"Loading preview of {SkillName} (install)…", console.Output, StringComparison.Ordinal);
        Assert.Contains($"Preview: {SkillName} from {Repository} at v1.2.3", console.Output, StringComparison.Ordinal);
        Assert.Contains("Esc close preview", console.Output, StringComparison.Ordinal);
        Assert.Contains("# LiveCharts2 Development Guide", console.Output, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(console.Output, "Recommend 1 action(s), select which to apply:"));
        Assert.True(console.Output.LastIndexOf("Recommend 1 action(s)", StringComparison.Ordinal) > console.Output.IndexOf("Preview:", StringComparison.Ordinal));
        _ = Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task PromptFetchesEachPreviewOnceAndScrollsLongContent()
    {
        using var console = CreateConsole();
        FakeCommandRunner runner = new();
        runner.Enqueue(new(0, string.Join('\n', Enumerable.Range(1, 30).Select(line => $"line {line}")), string.Empty));
        console.Input.PushKey(ConsoleKey.F3);
        console.Input.PushKey(ConsoleKey.End);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.F3);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        _ = await Prompt(console, runner).PromptAsync([], [Skill()], NoDuplicates(), NothingElsewhere(), CancellationToken.None);

        Assert.Contains("Lines 1–24 of 30", console.Output, StringComparison.Ordinal);
        Assert.Contains("Lines 7–30 of 30", console.Output, StringComparison.Ordinal);
        Assert.Contains("↑ ↓ scroll, PgUp PgDn page, Esc close preview", console.Output, StringComparison.Ordinal);
        _ = Assert.Single(runner.Calls);
        Assert.Equal(3, Occurrences(console.Output, "Preview: "));
    }

    [Fact]
    public async Task PromptShowsAnInlineNoticeWhenGhCannotPreviewTheSkill()
    {
        using var console = CreateConsole();
        FakeCommandRunner runner = new();
        runner.Enqueue(new(1, string.Empty, $"{GhHiddenNotice}skill \"{SkillName}\" not found in {Repository}\n"));
        console.Input.PushKey(ConsoleKey.F3);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await Prompt(console, runner).PromptAsync([], [Skill()], NoDuplicates(), NothingElsewhere(), CancellationToken.None);

        _ = Assert.Single(result.SelectedSkills);
        Assert.Contains($"Preview unavailable: skill \"{SkillName}\" not found in {Repository}", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Preview: ", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptShowsDirectiveContentWithoutGhAndIgnoresF3WithoutASource()
    {
        using var console = CreateConsole();
        FakeCommandRunner runner = new();
        DirectivePlanItem directive = new("foundation-prompt-log", DirectiveStatuses.Missing, "## Prompt Log\nBody line.");
        console.Input.PushKey(ConsoleKey.F3);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await Prompt(console, runner).PromptAsync([directive], [], NoDuplicates(), NothingElsewhere(), CancellationToken.None);

        Assert.Equal([directive], result.SelectedDirectives);
        Assert.Contains("Preview: foundation-prompt-log directive", console.Output, StringComparison.Ordinal);
        Assert.Contains("Body line.", console.Output, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);

        using var noSource = CreateConsole();
        noSource.Input.PushKey(ConsoleKey.F3);
        noSource.Input.PushKey(ConsoleKey.Enter);

        _ = await new RecommendationSelectionPrompt(noSource).PromptAsync([directive], [], NoDuplicates(), NothingElsewhere(), CancellationToken.None);

        Assert.DoesNotContain("F3", noSource.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Preview", noSource.Output, StringComparison.Ordinal);
    }

    static TestConsole CreateConsole()
    {
        TestConsole console = new();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 120;
        return console;
    }

    static RecommendationSelectionPrompt Prompt(TestConsole console, FakeCommandRunner runner)
        => new(console, new RecommendationPreviewSource(runner, WorkingDirectory));

    static ScopeDuplicateScanResult NoDuplicates()
        => new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    static PresentElsewhere NothingElsewhere()
        => new(0, 0, PresentElsewhere.AboveOrBelow);

    static int Occurrences(string text, string value)
        => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    static SkillManifestEntry Skill(string? resolvedRef = null)
        => new(Repository, SkillName, SkillName, TechnologyNames.Dotnet, [])
        {
            ResolvedSource = resolvedRef is null ? null : new(Repository, resolvedRef, DateTimeOffset.MinValue)
        };

    static RecommendationSelectionItem SkillItem(SkillManifestEntry skill)
        => new(RecommendationSelectionState.FormatSkillKey(skill.SourceRepo, skill.InstallArg), $"{skill.LocalFolder} (install)", RecommendationSelectionKind.Skill, null, skill);

    static RecommendationSelectionItem DirectiveItem(string name, string content)
        => new(RecommendationSelectionState.FormatDirectiveKey(name), $"{name} (install)", RecommendationSelectionKind.Directive, new DirectivePlanItem(name, DirectiveStatuses.Missing, content), null);
}
