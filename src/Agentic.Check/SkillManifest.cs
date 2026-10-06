namespace Agentic.Check;

sealed record SkillManifestEntry
{
    public SkillManifestEntry(
        string sourceRepo,
        string installArg,
        string localFolder,
        string technology,
        IReadOnlyList<GateRequirement> gateRequirements,
        string plugin = "",
        IReadOnlyList<SkillDependency>? dependencies = null,
        string sourceRef = "",
        string version = "",
        string recommendationAction = "install",
        bool forceInstall = false,
        ToolVersion? minimumRelease = null)
    {
        SourceRepo = sourceRepo;
        InstallArg = installArg;
        LocalFolder = localFolder;
        Technology = technology;
        GateRequirements = gateRequirements;
        Plugin = plugin;
        Dependencies = dependencies ?? [];
        SourceRef = sourceRef;
        Version = version;
        RecommendationAction = recommendationAction;
        ForceInstall = forceInstall;
        MinimumRelease = minimumRelease;
    }

    // The first release of the source repository that ships the skill. The stable channel installs from
    // the latest release, so it offers the skill only once that release is out; preview has it already.
    public ToolVersion? MinimumRelease { get; init; }

    public string SourceRepo { get; init; }

    public string InstallArg { get; init; }

    public string LocalFolder { get; init; }

    public string Technology { get; init; }

    public IReadOnlyList<GateRequirement> GateRequirements { get; init; }

    public string Plugin { get; init; }

    public IReadOnlyList<SkillDependency> Dependencies { get; init; }

    public string SourceSpec => string.IsNullOrWhiteSpace(SourceRef) ? SourceRepo : $"{SourceRepo}@{SourceRef}";

    public string Version { get; init; }

    // A tool row's version text when no selected row depends on it, if that differs from Version.
    public string? VersionWithoutConsumers { get; init; }

    public string SourceRef { get; init; }

    public SourceVersionInfo? ResolvedSource { get; init; }

    public string ResolvedSourceRef => ResolvedSource?.ContentRef ?? string.Empty;

    public bool IsCompanion => Key == CompanionDependency.Identity.Key;

    public bool IsDna => Key == DnaInstaller.Identity.Key;

    public bool IsCodexRules => Key == CodexRulesInstaller.Identity.Key;

    public bool IsReadmeBadge => Key == ReadmeBadgeInstaller.Identity.Key;

    public bool IsRequiredToolRepair { get; init; }

    // Shown in the action list under the row, for a choice the check leaves to the user.
    public IReadOnlyList<string> Notes { get; init; } = [];

    // A row the check does not select on its own: the user opts in.
    public bool SelectedByDefault { get; init; } = true;

    public string RecommendationAction { get; init; }

    public bool ForceInstall { get; init; }

    public string Display => $"{SourceSpec} {InstallArg}";

    public string Key => SkillDependency.CreateKey(SourceRepo, InstallArg);
}

sealed record GateRequirement(string Gate, string Value);

// A skill as removal and retirement know it: by source repository and folder name, whatever its path
// or commit, since names are unique within a repository.
readonly record struct SkillIdentity(string SourceRepo, string Name)
{
    public bool Equals(SkillIdentity other)
        => string.Equals(SourceRepo, other.SourceRepo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => HashCode.Combine(SourceRepo.ToUpperInvariant(), Name.ToUpperInvariant());
}

sealed record SkillDependency(string SourceRepo, string InstallArg)
{
    public string Key => CreateKey(SourceRepo, InstallArg);

    public static string CreateKey(string sourceRepo, string installArg)
        => $"{sourceRepo}\n{installArg}";
}

// Discovery baselines only; these do not pin installation or update versions.
sealed record SkillSourceReview(
    string SourceRepo,
    string CommitSha,
    DateTimeOffset CommittedAt,
    DateTimeOffset ReviewedAt);

static class StaticSkillManifest
{
    const string DotnetSkillsRepo = "dotnet/skills";
    const string VincentRepo = "VincentH-Net/dotnet-agentic-engineering";
    const string UnoStudioRepo = "unoplatform/studio";
    const string MattRepo = "mtmattei/UnoPlatformSkills";

    // Initial review dates are inferred from each repo's last skill-set change in this file.
    // SHAs identify the latest default-branch commits at those cutoffs (GitHub committer time).
    // Advance a baseline only after reviewing upstream additions, including rejected candidates.
    internal static IReadOnlyList<SkillSourceReview> SourceReviews { get; } =
    [
        new(
            VincentRepo,
            "fbf904d9e8c22dc8449e58d1d9a47170d82e5e8c",
            new(2026, 6, 29, 17, 25, 38, TimeSpan.Zero),
            new(2026, 6, 29, 17, 25, 38, TimeSpan.Zero)),
        new(
            MattRepo,
            "802045a45ae73eaae9b9ac70c01d0ff0e6c5d401",
            new(2026, 4, 4, 15, 14, 11, TimeSpan.Zero),
            new(2026, 6, 12, 14, 59, 14, TimeSpan.Zero)),
        new(
            UnoStudioRepo,
            "6874bb4c471b228cc17dd97fd73ce5659569cc92",
            new(2026, 9, 30, 20, 0, 37, TimeSpan.Zero),
            new(2026, 10, 1, 10, 37, 26, TimeSpan.Zero)),
        new(
            DotnetSkillsRepo,
            "973cffbcdbae02557cc68ad0d41b8f20d60cfa03",
            new(2026, 10, 1, 14, 49, 17, TimeSpan.Zero),
            new(2026, 10, 1, 15, 30, 28, TimeSpan.Zero))
    ];

    internal static IReadOnlyList<SkillManifestEntry> All { get; } =
    [
        VincentDotnet("cli-e2e-testing", [new("terminal", "interactive")]),
        VincentDotnet("dotnet-livecharts2"),
        VincentDotnet("dotnet-modern-csharp-editorconfig"),
        ..DotnetTestSkills(),
        VincentOrleans("orleans-result-pattern"),
        VincentOrleans("orleans-multiservice-pattern"),
        VincentOrleans("orleans-multitenant", minimumRelease: "2.5.0"),
        VincentUno("uno-agentic-support"),
        VincentUno("uno-mvvm", "presentation", "mvvm"),
        VincentUno("uno-csharpmarkup2", "markup", "csharp2"),
        VincentUno("uno-xaml", "markup", "xaml"),
        VincentUno("uno-fluent2", "theme", "fluent"),
        VincentUno("uno-hamburgermenu-databinding", "presentation", "mvvm"),
        VincentUno("uno-livecharts2-theme-switching"),
        VincentUno("uno-responsive-spanning-gridwrap-layout"),
        VincentUno("uno-test-resize-app-window"),
        MattUno("uno-extensions-services"),
        MattUno("uno-csharp-markup", "markup", "csharp"),
        // The latest Studio release still ships the per-topic skills; the hubs are preview only.
        ..UnoStudioMvux(),
        ..UnoStudioNavigation(),
        UnoStudio("uno-testing-assertions"),
        UnoStudio("uno-testing-ui"),
        UnoStudio("uno-themes-material", "theme", "material"),
        UnoStudio("uno-themes-simple", "theme", "simple"),
        UnoStudio("uno-themes-semantic-colors-brushes", [new("theme", "material"), new("theme", "simple")]),
        UnoStudio("uno-toolkit-material-theme", "theme", "material"),
        UnoStudio("uno-toolkit-csharp-markup", "markup", "csharp"),
        ..UnoStudioToolkitUngated()
    ];

    // Skills some release offered that neither set offers now. A manifest never forgets a skill: a check
    // removes an installed skill its channel no longer offers, and a maintenance test fails when an entry
    // leaves both sets without being listed here.
    internal static IReadOnlyList<SkillIdentity> Retired { get; } =
    [
        // Offered on preview by 2.2.0 and 2.3.0; dotnet/skills withdrew it.
        new(DotnetSkillsRepo, "minimal-api-file-upload")
    ];

    internal static IReadOnlyList<SkillManifestEntry> Preview { get; } =
    [
        ..All.Where(skill => (skill.SourceRepo != DotnetSkillsRepo || skill.Plugin != "dotnet-test") && skill.SourceRepo != UnoStudioRepo),
        ..DotnetTestSkills(preview: true),
        ..UnoStudioHubs(),
        DotnetAspNetCore("dotnet-webapi")
    ];

    internal static SkillIdentity Identity(SkillManifestEntry skill)
        => new(skill.SourceRepo, skill.LocalFolder);

    // What a channel's manifest leaves obsolete: every skill any manifest ever offered, by source repository
    // and folder name, that this manifest does not. The other channel's skills count as ever offered, so
    // switching channels never leaves both sets installed side by side.
    internal static IReadOnlySet<SkillIdentity> ObsoleteFor(IReadOnlyList<SkillManifestEntry> manifest)
    {
        HashSet<SkillIdentity> obsolete = [.. All.Concat(Preview).Select(Identity), .. Retired];
        obsolete.ExceptWith(manifest.Select(Identity));
        return obsolete;
    }

    // Whether this channel, resolved to this source version, offers the entry. Only an entry with a minimum
    // release can be left out: on the stable channel, when the latest release predates it. A default branch
    // has it, as does a release whose tag is not a version, which leaves the install to say so.
    internal static bool IsOffered(SkillManifestEntry skill, SourceVersionMode mode, SourceVersionInfo source)
    {
        if (skill.MinimumRelease is null || mode == SourceVersionMode.Preview || source.IsDefaultBranch)
        {
            return true;
        }

        try
        {
            return ToolVersion.Parse(source.Ref.TrimStart('v', 'V')).CompareTo(skill.MinimumRelease) >= 0;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    static SkillManifestEntry VincentDotnet(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Dotnet, plugin: "dotnet");

    static SkillManifestEntry VincentDotnet(string skill, IReadOnlyList<GateRequirement> gateRequirements)
        => Entry(VincentRepo, skill, TechnologyNames.Dotnet, gateRequirements, plugin: "dotnet");

    static SkillManifestEntry VincentOrleans(string skill, string? minimumRelease = null)
        => Entry(VincentRepo, skill, TechnologyNames.Orleans, plugin: "orleans", minimumRelease: minimumRelease is null ? null : ToolVersion.Parse(minimumRelease));

    static SkillManifestEntry VincentUno(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Uno, plugin: "uno-platform");

    static SkillManifestEntry VincentUno(string skill, string gate, string value)
        => Entry(VincentRepo, skill, TechnologyNames.Uno, [new(gate, value)], plugin: "uno-platform");

    static SkillManifestEntry MattUno(string skill)
        => Entry(MattRepo, skill, TechnologyNames.Uno, plugin: "UnoPlatformSkills");

    static SkillManifestEntry MattUno(string skill, string gate, string value)
        => Entry(MattRepo, skill, TechnologyNames.Uno, [new(gate, value)], plugin: "UnoPlatformSkills");

    static SkillManifestEntry UnoStudio(string skill)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno, plugin: "studio");

    static SkillManifestEntry UnoStudio(string skill, string gate, string value)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno, [new(gate, value)], plugin: "studio");

    static SkillManifestEntry UnoStudio(string skill, IReadOnlyList<GateRequirement> gateRequirements)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno, gateRequirements, plugin: "studio");

    static SkillManifestEntry DotnetTest(string skill, IReadOnlyList<SkillDependency>? dependencies = null)
        => new(
            DotnetSkillsRepo,
            DotnetSkillInstallArg("dotnet-test", skill),
            skill,
            TechnologyNames.Dotnet,
            [],
            "dotnet-test",
            dependencies);

    static SkillManifestEntry DotnetAspNetCore(string skill)
        => new(
            DotnetSkillsRepo,
            DotnetSkillInstallArg("dotnet-aspnetcore", skill),
            skill,
            TechnologyNames.AspNetCore,
            [],
            "dotnet-aspnetcore");

    static SkillDependency DotnetTestDependency(string skill)
        => new(DotnetSkillsRepo, DotnetSkillInstallArg("dotnet-test", skill));

    static string DotnetSkillInstallArg(string plugin, string skill)
        => $"plugins/{plugin}/skills/{skill}";

    static SkillManifestEntry Entry(
        string sourceRepo,
        string skill,
        string technology,
        IReadOnlyList<GateRequirement>? gateRequirements = null,
        string plugin = "",
        ToolVersion? minimumRelease = null)
        => new(sourceRepo, skill, skill, technology, gateRequirements ?? [], plugin, minimumRelease: minimumRelease);

    static IReadOnlyList<SkillManifestEntry> DotnetTestSkills(bool preview = false)
        =>
        [
            DotnetTest("crap-score"),
            DotnetTest("detect-static-dependencies"),
            // Upstream consolidated the reference on the default branch; stable still has the old skill.
            DotnetTest(preview ? "test-analysis-extensions" : "dotnet-test-frameworks"),
            DotnetTest("filter-syntax"),
            DotnetTest("generate-testability-wrappers"),
            DotnetTest("migrate-static-to-wrapper"),
            DotnetTest("mtp-hot-reload", [DotnetTestDependency("filter-syntax")]),
            DotnetTest("platform-detection"),
            DotnetTest(
                "run-tests",
                [
                    DotnetTestDependency("platform-detection"),
                    DotnetTestDependency("filter-syntax")
                ]),
            DotnetTest("test-anti-patterns", preview ? [DotnetTestDependency("test-analysis-extensions")] : []),
            DotnetTest("writing-mstest-tests")
        ];

    // Each hub folds its former per-topic skills into references/*.md, so a hub takes the union
    // of their gates (unoplatform/studio#157). Default branch only until a release ships them.
    static IReadOnlyList<SkillManifestEntry> UnoStudioHubs()
        =>
        [
            UnoStudio("uno-platform"),
            UnoStudio("uno-build-app"),
            UnoStudio("uno-mvux", "presentation", "mvux"),
            UnoStudio("uno-navigation"),
            UnoStudio("uno-toolkit"),
            UnoStudio("uno-themes", [new("theme", "material"), new("theme", "simple")]),
            UnoStudio("uno-testing")
        ];

    static IReadOnlyList<SkillManifestEntry> UnoStudioMvux()
        =>
        [
            UnoStudio("uno-mvux-commands", "presentation", "mvux"),
            UnoStudio("uno-mvux-feed-basics", "presentation", "mvux"),
            UnoStudio("uno-mvux-feedview", "presentation", "mvux"),
            UnoStudio("uno-mvux-listfeed", "presentation", "mvux"),
            UnoStudio("uno-mvux-liststate", "presentation", "mvux"),
            UnoStudio("uno-mvux-messaging", "presentation", "mvux"),
            UnoStudio("uno-mvux-overview", "presentation", "mvux"),
            UnoStudio("uno-mvux-pagination", "presentation", "mvux"),
            UnoStudio("uno-mvux-records", "presentation", "mvux"),
            UnoStudio("uno-mvux-selection", "presentation", "mvux"),
            UnoStudio("uno-mvux-state-basics", "presentation", "mvux")
        ];

    static IReadOnlyList<SkillManifestEntry> UnoStudioNavigation()
        =>
        [
            UnoStudio("uno-navigation-code"),
            UnoStudio("uno-navigation-contentcontrol"),
            UnoStudio("uno-navigation-data"),
            UnoStudio("uno-navigation-dialogs"),
            UnoStudio("uno-navigation-navigationview"),
            UnoStudio("uno-navigation-panel-visibility"),
            UnoStudio("uno-navigation-qualifiers"),
            UnoStudio("uno-navigation-regions"),
            UnoStudio("uno-navigation-responsive-shell"),
            UnoStudio("uno-navigation-routes"),
            UnoStudio("uno-navigation-setup"),
            UnoStudio("uno-navigation-tabbar"),
            UnoStudio("uno-navigation-troubleshooting"),
            UnoStudio("uno-navigation-xaml")
        ];

    static IReadOnlyList<SkillManifestEntry> UnoStudioToolkitUngated()
        =>
        [
            UnoStudio("uno-toolkit-ancestor-binding"),
            UnoStudio("uno-toolkit-autolayout"),
            UnoStudio("uno-toolkit-card"),
            UnoStudio("uno-toolkit-chip"),
            UnoStudio("uno-toolkit-command-extensions"),
            UnoStudio("uno-toolkit-cupertino-theme"),
            UnoStudio("uno-toolkit-divider"),
            UnoStudio("uno-toolkit-drawer"),
            UnoStudio("uno-toolkit-extendedsplashscreen"),
            UnoStudio("uno-toolkit-flipview-extensions"),
            UnoStudio("uno-toolkit-getting-started"),
            UnoStudio("uno-toolkit-input-extensions"),
            UnoStudio("uno-toolkit-itemsrepeater-extensions"),
            UnoStudio("uno-toolkit-lightweight-styling"),
            UnoStudio("uno-toolkit-loadingview"),
            UnoStudio("uno-toolkit-navigationbar"),
            UnoStudio("uno-toolkit-progress-extensions"),
            UnoStudio("uno-toolkit-resource-extensions"),
            UnoStudio("uno-toolkit-responsive"),
            UnoStudio("uno-toolkit-safearea"),
            UnoStudio("uno-toolkit-segmented-controls"),
            UnoStudio("uno-toolkit-selector-extensions"),
            UnoStudio("uno-toolkit-shadowcontainer"),
            UnoStudio("uno-toolkit-statusbar-extensions"),
            UnoStudio("uno-toolkit-system-theme-helper"),
            UnoStudio("uno-toolkit-tabbar"),
            UnoStudio("uno-toolkit-tabbaritem-extensions"),
            UnoStudio("uno-toolkit-visualstatemanager-extensions"),
            UnoStudio("uno-toolkit-zoomcontentcontrol")
        ];
}

static class SkillPlanner
{
    internal static IReadOnlyList<SkillManifestEntry> Plan(
        IReadOnlyList<SkillManifestEntry> manifest,
        StackDetectionResult stack)
        => [.. manifest
            .Where(entry => stack.Technologies.Contains(entry.Technology, StringComparer.OrdinalIgnoreCase))
            .Where(entry => entry.GateRequirements.Count == 0 || entry.GateRequirements.Any(requirement => HasGate(stack, requirement)))
            .DistinctBy(entry => (entry.SourceRepo, entry.InstallArg))
            .OrderBy(entry => SkillOrdering.GetSourceRepoOrder(entry.SourceRepo))
            .ThenBy(entry => entry.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => SkillOrdering.GetPluginOrder(entry.SourceRepo, entry.Plugin))
            .ThenBy(entry => entry.Plugin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.InstallArg, StringComparer.OrdinalIgnoreCase)];

    static bool HasGate(StackDetectionResult stack, GateRequirement requirement)
        => stack.InstallGates.Any(report => report.GetValues(requirement.Gate).Contains(requirement.Value, StringComparer.OrdinalIgnoreCase));
}

static class SkillOrdering
{
    internal static int GetSourceRepoOrder(string sourceRepo)
        => sourceRepo switch
        {
            "VincentH-Net/dotnet-agentic-engineering" => 0,
            "mtmattei/UnoPlatformSkills" => 1,
            "unoplatform/studio" => 2,
            "dotnet/skills" => 3,
            _ => 100
        };

    internal static int GetPluginOrder(string sourceRepo, string plugin)
        => sourceRepo switch
        {
            "VincentH-Net/dotnet-agentic-engineering" => GetVincentPluginOrder(plugin),
            "dotnet/skills" => GetDotnetSkillsPluginOrder(plugin),
            _ => 100
        };

    static int GetVincentPluginOrder(string plugin)
        => plugin switch
        {
            "uno-platform" => 0,
            "dotnet" => 1,
            "foundation" => 2,
            _ => 100
        };

    static int GetDotnetSkillsPluginOrder(string plugin)
        => plugin switch
        {
            "dotnet-aspnetcore" => 0,
            "dotnet-test" => 1,
            _ => 100
        };
}
