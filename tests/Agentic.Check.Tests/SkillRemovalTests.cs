namespace Agentic.Check.Tests;

public sealed class SkillRemovalTests
{
    const string Repository = "VincentH-Net/dotnet-agentic-engineering";
    const string Studio = "unoplatform/studio";
    const string OtherRepository = "someone/else";
    const string SkillName = "uno-navigation-code";

    static readonly SkillIdentity Obsolete = new(Studio, SkillName);

    static string Stamped(string repository, string path)
        => $"---\nname: {Path.GetFileName(path)}\nmetadata:\n  github-path: {path}\n  github-ref: refs/tags/1.1.0\n  github-repo: https://github.com/{repository}\n  github-tree-sha: 1eefa3e7\n---\n# Skill\n";

    const string Unstamped = "---\nname: skill\ndescription: Hand-copied skill.\n---\n# Skill\n";

    [Fact]
    public void EachChannelLeavesTheOtherChannelsSkillsAndTheRetiredOnesObsolete()
    {
        var preview = StaticSkillManifest.ObsoleteFor(StaticSkillManifest.Preview);
        var stable = StaticSkillManifest.ObsoleteFor(StaticSkillManifest.All);

        // The per-topic Studio skills are stable only; the hubs are preview only; the retired skill is in neither.
        Assert.Contains(new SkillIdentity(Studio, SkillName), preview);
        Assert.DoesNotContain(new SkillIdentity(Studio, "uno-mvux"), preview);
        Assert.Contains(new SkillIdentity(Studio, "uno-mvux"), stable);
        Assert.DoesNotContain(new SkillIdentity(Studio, SkillName), stable);
        Assert.Contains(new SkillIdentity("dotnet/skills", "minimal-api-file-upload"), preview);
        Assert.Contains(new SkillIdentity("dotnet/skills", "minimal-api-file-upload"), stable);
        Assert.DoesNotContain(new SkillIdentity(Repository, "dotnet-livecharts2"), preview);
        Assert.DoesNotContain(new SkillIdentity(Repository, "dotnet-livecharts2"), stable);
    }

    [Fact]
    public void RetiredSkillsAreInNeitherSetAndIdentityIgnoresCase()
    {
        var offered = StaticSkillManifest.All.Concat(StaticSkillManifest.Preview).Select(StaticSkillManifest.Identity).ToHashSet();

        Assert.All(StaticSkillManifest.Retired, retired => Assert.DoesNotContain(retired, offered));
        Assert.Equal(new SkillIdentity("Owner/Repo", "Skill"), new SkillIdentity("owner/repo", "skill"));
        Assert.Contains(new SkillIdentity("OWNER/REPO", "SKILL"), new HashSet<SkillIdentity> { new("owner/repo", "skill") });
    }

    [Theory]
    [InlineData("https://github.com/unoplatform/studio", Studio)]
    [InlineData("https://github.com/unoplatform/studio.git/", Studio)]
    [InlineData("unoplatform/studio", Studio)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheStampedRepositoryUrlNamesTheRepository(string? stamped, string? expected)
        => Assert.Equal(expected, InstalledSkills.SourceRepoOf(stamped));

    [Fact]
    public void ScanReadsTheOriginGhStampedIntoEachSkill()
    {
        using TempDirectory temp = new();
        temp.Write($".agents/skills/{SkillName}/SKILL.md", Stamped(Studio, "skills/" + SkillName));
        temp.Write(".agents/skills/hand-copied/SKILL.md", Unstamped);
        temp.Write(".agents/skills/not-a-skill/README.md", "no SKILL.md");

        var installed = InstalledSkills.Scan([Path.Combine(temp.Path, ".agents", "skills"), Path.Combine(temp.Path, ".claude", "skills")]);

        Assert.Equal(2, installed.Count);
        var stamped = Assert.Single(installed, skill => skill.Folder == SkillName);
        Assert.Equal(Studio, stamped.SourceRepo);
        Assert.Equal("skills/" + SkillName, stamped.SourcePath);
        var unstamped = Assert.Single(installed, skill => skill.Folder == "hand-copied");
        Assert.Null(unstamped.SourceRepo);
    }

    [Fact]
    public void RemovalRowsCoverObsoleteStampedSkillsNameClashesAndUnstampedRetiredNames()
    {
        InstalledSkill[] installed =
        [
            new("a", SkillName, Studio, "skills/" + SkillName),
            new("b", SkillName, Studio, "skills/" + SkillName),
            new("a", "dotnet-livecharts2", OtherRepository, "skills/dotnet-livecharts2"),
            new("a", "minimal-api-file-upload", null, null),
            new("a", "dotnet-modern-csharp-editorconfig", Repository, "dotnet-modern-csharp-editorconfig"),
            new("a", "my-own-skill", null, null)
        ];
        SkillManifestEntry[] recommended = [Entry(Repository, "dotnet-livecharts2"), Entry(Repository, "dotnet-modern-csharp-editorconfig")];
        HashSet<SkillIdentity> obsolete = [Obsolete, new("dotnet/skills", "minimal-api-file-upload")];

        var rows = SkillRemovalPlanner.PlanRemovals(installed, recommended, obsolete);

        Assert.Equal(3, rows.Count);
        var gone = Assert.Single(rows, row => row.LocalFolder == SkillName);
        Assert.Equal(Studio, gone.SourceRepo);
        Assert.True(gone.SelectedByDefault);
        Assert.Equal(SkillInstaller.RemoveAction, gone.RecommendationAction);
        Assert.Equal([$"No longer offered by {Studio}."], gone.Notes);
        var clash = Assert.Single(rows, row => row.LocalFolder == "dotnet-livecharts2");
        Assert.Equal(OtherRepository, clash.SourceRepo);
        Assert.False(clash.SelectedByDefault);
        Assert.Contains(clash.Notes, note => note.Contains($"now offered from {Repository}", StringComparison.Ordinal));
        var unstamped = Assert.Single(rows, row => row.LocalFolder == "minimal-api-file-upload");
        Assert.Equal("dotnet/skills", unstamped.SourceRepo);
        Assert.False(unstamped.SelectedByDefault);
        Assert.Contains(unstamped.Notes, note => note.StartsWith("No origin recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void AReplacementWaitsForTheUserAndBringsTheRemovalAlong()
    {
        InstalledSkill[] installed = [new("a", "dotnet-livecharts2", OtherRepository, "skills/dotnet-livecharts2")];
        var offered = Entry(Repository, "dotnet-livecharts2");
        var other = Entry(Repository, "dotnet-modern-csharp-editorconfig");

        var actions = SkillRemovalPlanner.WithReplacements([other], [offered, other], installed);

        Assert.Equal(2, actions.Count);
        Assert.Same(other, actions[0]);
        var replacement = actions[1];
        Assert.Equal(offered.Key, replacement.Key);
        Assert.False(replacement.SelectedByDefault);
        Assert.True(replacement.ForceInstall);
        Assert.Contains(new SkillDependency(OtherRepository, "dotnet-livecharts2"), replacement.Dependencies);
        Assert.Contains(replacement.Notes, note => note.StartsWith($"Replaces dotnet-livecharts2 from {OtherRepository}", StringComparison.Ordinal));
    }

    [Fact]
    public void ASkillAtAnotherPathThanTheManifestNamesIsReinstalledFromTheNewPath()
    {
        var pathEntry = new SkillManifestEntry("dotnet/skills", "plugins/dotnet-test/skills/crap-score", "crap-score", TechnologyNames.Dotnet, []);
        var nameEntry = Entry(Repository, "dotnet-livecharts2");
        InstalledSkill[] installed =
        [
            new("a", "crap-score", "dotnet/skills", "skills/crap-score"),
            new("a", "dotnet-livecharts2", Repository, "plugins/dotnet/skills/dotnet-livecharts2")
        ];

        Assert.Equal([pathEntry], SkillRemovalPlanner.FindMoved(installed, [pathEntry, nameEntry]));
        Assert.Empty(SkillRemovalPlanner.FindMoved([new("a", "crap-score", "dotnet/skills", "plugins/dotnet-test/skills/crap-score/")], [pathEntry]));
        Assert.Empty(SkillRemovalPlanner.FindMoved([new("a", "crap-score", OtherRepository, "skills/crap-score")], [pathEntry]));
    }

    [Fact]
    public void AnUnselectedRowStartsUnselectedAndSelectingTheInstallSelectsItsRemoval()
    {
        var removal = new SkillManifestEntry(OtherRepository, "foo", "foo", string.Empty, [], recommendationAction: SkillInstaller.RemoveAction)
        {
            SelectedByDefault = false,
            Notes = ["Installed from someone/else; a skill with this name is now offered from " + Repository + "."]
        };
        var install = Entry(Repository, "foo") with
        {
            SelectedByDefault = false,
            Dependencies = [new SkillDependency(OtherRepository, "foo")],
            Notes = ["Replaces foo from someone/else."]
        };
        var items = RecommendationSelectionPrompt.BuildItems([], [install, removal]);
        RecommendationSelectionState state = new(items);

        Assert.Empty(state.SelectedSkills);
        Assert.Equal(["Replaces foo from someone/else."], items[0].Notes);

        state.Apply(new SkillSelectionInput(SkillSelectionCommand.Toggle));

        Assert.Equal([install.Key, removal.Key], state.SelectedSkills.Select(skill => skill.Key));
    }

    [Fact]
    public void TheDefaultSelectionLeavesUnselectedRowsOut()
    {
        var removal = new SkillManifestEntry(Studio, SkillName, SkillName, string.Empty, [], recommendationAction: SkillInstaller.RemoveAction);
        var clash = new SkillManifestEntry(OtherRepository, "foo", "foo", string.Empty, [], recommendationAction: SkillInstaller.RemoveAction) { SelectedByDefault = false };

        var selection = RecommendationSelectionPrompt.DefaultSelection([], [removal, clash], NoDuplicates());

        Assert.Equal([removal.Key], selection.SelectedSkills.Select(skill => skill.Key));
    }

    [Fact]
    public async Task ThePromptShowsTheNotesUnderARow()
    {
        using var console = CreateConsole();
        var removal = new SkillManifestEntry(Studio, SkillName, SkillName, string.Empty, [], recommendationAction: SkillInstaller.RemoveAction)
        {
            Notes = [$"No longer offered by {Studio}."]
        };
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await new RecommendationSelectionPrompt(console).PromptAsync([], [removal], NoDuplicates(), new PresentElsewhere(0, 0, PresentElsewhere.AboveOrBelow), CancellationToken.None);

        Assert.Equal([removal], result.SelectedSkills);
        Assert.Contains($"[x] {SkillName} (remove)", console.Output, StringComparison.Ordinal);
        Assert.Contains($"No longer offered by {Studio}.", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunSaysWhatItWouldRemoveAndRemovesNothing()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        foreach (string agent in new[] { ".agents", ".claude" })
            temp.Write($"{agent}/skills/{SkillName}/SKILL.md", Stamped(Studio, "skills/" + SkillName));
        FakeCommandRunner runner = new();
        QueueChecks(runner);
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter, obsolete: [Obsolete]).RunAsync(new(temp.Path, true, false, null, null, "codex,claude-code", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var obsolete = Assert.Single(result.Report.ObsoleteSkills);
        Assert.Equal((Studio, SkillName), (obsolete.SourceRepo, obsolete.LocalFolder));
        foreach (string agent in new[] { ".agents", ".claude" })
        {
            Assert.Contains($"Would remove {SkillName} from {Path.Combine(temp.Path, agent, "skills")}: No longer offered by {Studio}.", result.Report.Actions);
            Assert.True(File.Exists(Path.Combine(temp.Path, agent, "skills", SkillName, "SKILL.md")));
        }

        Assert.Contains("Would remove skills from skills directories:", reporter.BoldMessages);
        Assert.Empty(result.Report.SkillRemovals);
    }

    [Fact]
    public async Task AnObsoleteSkillIsRemovedFromEveryDirectoryWhenApplied()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        foreach (string agent in new[] { ".agents", ".claude" })
        {
            temp.Write($"{agent}/skills/{SkillName}/SKILL.md", Stamped(Studio, "skills/" + SkillName));
            temp.Write($"{agent}/skills/{SkillName}/references/more.md", "reference");
        }

        FakeCommandRunner runner = new();
        QueueChecks(runner);
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [SkillName] };
        RecordingReporter reporter = new();

        var result = await Workflow(runner, reporter, prompts, obsolete: [Obsolete]).RunAsync(new(temp.Path, false, false, null, null, "codex,claude-code", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var row = Assert.Single(prompts.RecommendedSkillActions, skill => skill.LocalFolder == SkillName);
        Assert.Equal(SkillInstaller.RemoveAction, row.RecommendationAction);
        Assert.True(row.SelectedByDefault);
        Assert.Equal(2, result.Report.SkillRemovals.Count);
        Assert.All(result.Report.SkillRemovals, removal => Assert.True(removal.Success));
        foreach (string agent in new[] { ".agents", ".claude" })
            Assert.False(Directory.Exists(Path.Combine(temp.Path, agent, "skills", SkillName)));
        Assert.Equal(2, reporter.Successes.Count(message => message.StartsWith("  Removed skill", StringComparison.Ordinal)));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments is ["skill", "install", ..]);
    }

    [Fact]
    public async Task ANameClashIsLeftToTheUserAndAReplacementRemovesBeforeItInstalls()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        string skillsDirectory = Path.Combine(temp.Path, ".agents", "skills");
        temp.Write(".agents/skills/dotnet-livecharts2/SKILL.md", Stamped(OtherRepository, "skills/dotnet-livecharts2"));
        var offered = Entry(Repository, "dotnet-livecharts2");

        // Unattended: nothing is chosen, so nothing happens.
        FakeCommandRunner untouched = new();
        QueueChecks(untouched, directories: 1);
        var unattended = await Workflow(untouched, new RecordingReporter(), manifest: [offered]).RunAsync(new(temp.Path, false, true, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, unattended.ExitCode);
        Assert.Empty(unattended.Report.SkillRemovals);
        Assert.Empty(unattended.Report.InstallResults);
        Assert.Equal(Stamped(OtherRepository, "skills/dotnet-livecharts2"), await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "dotnet-livecharts2", "SKILL.md")));

        // Selecting the install brings the removal along, and the removal runs first.
        FakeCommandRunner runner = new()
        {
            OnRun = call =>
            {
                if (FakeGh.IsInstall(call))
                {
                    Assert.False(Directory.Exists(Path.Combine(skillsDirectory, "dotnet-livecharts2")));
                    FakeGh.WriteInstalledSkill(call, Stamped(Repository, "plugins/dotnet/skills/dotnet-livecharts2"));
                }
            }
        };
        QueueChecks(runner, directories: 1);
        runner.Enqueue(new(0, "installed", string.Empty));
        FakePrompts prompts = new() { SelectedDirectiveNames = [], SelectedSkillInstallArgs = ["dotnet-livecharts2"] };

        var result = await Workflow(runner, new RecordingReporter(), prompts, manifest: [offered]).RunAsync(new(temp.Path, false, false, null, null, "codex", false), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var removal = Assert.Single(prompts.RecommendedSkillActions, skill => skill.RecommendationAction == SkillInstaller.RemoveAction);
        Assert.Equal((OtherRepository, false), (removal.SourceRepo, removal.SelectedByDefault));
        var install = Assert.Single(prompts.RecommendedSkillActions, skill => skill.SourceRepo == Repository);
        Assert.Equal((false, true), (install.SelectedByDefault, install.ForceInstall));
        Assert.Contains(new SkillDependency(OtherRepository, "dotnet-livecharts2"), install.Dependencies);
        Assert.True(Assert.Single(result.Report.SkillRemovals).Success);
        Assert.True(Assert.Single(result.Report.InstallResults).Success);
        Assert.Equal(Stamped(Repository, "plugins/dotnet/skills/dotnet-livecharts2"), await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "dotnet-livecharts2", "SKILL.md")));
    }

    static SkillManifestEntry Entry(string repository, string skill)
        => new(repository, skill, skill, TechnologyNames.Dotnet, [], "dotnet");

    static CheckWorkflow Workflow(FakeCommandRunner runner, RecordingReporter reporter, FakePrompts? prompts = null, IReadOnlyList<SkillManifestEntry>? manifest = null, HashSet<SkillIdentity>? obsolete = null)
        => new(
            runner,
            prompts ?? new FakePrompts { SelectedDirectiveNames = [], SelectedSkillInstallArgs = [] },
            reporter,
            new FakeDirectiveSource(),
            new FakeSourceVersionResolver(),
            skillManifest: manifest ?? [new SkillManifestEntry(Repository, "orleans-result-pattern", "orleans-result-pattern", TechnologyNames.Orleans, [], "orleans")],
            obsoleteSkills: obsolete);

    // Prerequisite checks, then one gh skill update --dry-run per skills directory.
    static void QueueChecks(FakeCommandRunner runner, int directories = 2)
    {
        runner.Enqueue(new(0, "gh version 2.101.0", string.Empty));
        runner.Enqueue(new(0, "gh skill help", string.Empty));
        for (int index = 0; index < directories; index++)
            runner.Enqueue(new(0, string.Empty, "All skills are up to date.\n"));
    }

    static Spectre.Console.Testing.TestConsole CreateConsole()
    {
        Spectre.Console.Testing.TestConsole console = new();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        return console;
    }

    static ScopeDuplicateScanResult NoDuplicates()
        => new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}
