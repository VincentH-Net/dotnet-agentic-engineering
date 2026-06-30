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
        string version = "")
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
    }

    public string SourceRepo { get; init; }

    public string InstallArg { get; init; }

    public string LocalFolder { get; init; }

    public string Technology { get; init; }

    public IReadOnlyList<GateRequirement> GateRequirements { get; init; }

    public string Plugin { get; init; }

    public IReadOnlyList<SkillDependency> Dependencies { get; init; }

    public string SourceSpec => string.IsNullOrWhiteSpace(SourceRef) ? SourceRepo : $"{SourceRepo}@{SourceRef}";

    public string Version { get; init; }

    public string SourceRef { get; init; }

    public string Display => $"{SourceSpec} {InstallArg}";

    public string Key => SkillDependency.CreateKey(SourceRepo, InstallArg);
}

sealed record GateRequirement(string Gate, string Value);

sealed record SkillDependency(string SourceRepo, string InstallArg)
{
    public string Key => CreateKey(SourceRepo, InstallArg);

    public static string CreateKey(string sourceRepo, string installArg)
        => $"{sourceRepo}\n{installArg}";
}

static class StaticSkillManifest
{
    const string DotnetSkillsRepo = "dotnet/skills";
    const string VincentRepo = "VincentH-Net/dotnet-agentic-engineering";
    const string UnoStudioRepo = "unoplatform/studio";
    const string MattRepo = "mtmattei/UnoPlatformSkills";

    internal static IReadOnlyList<SkillManifestEntry> All { get; } =
    [
        VincentDotnet("cli-e2e-testing", [new("cli", "cli")]),
        VincentDotnet("dotnet-livecharts2"),
        VincentDotnet("dotnet-modern-csharp-editorconfig"),
        ..DotnetTestSkills(),
        VincentOrleans("orleans-result-pattern"),
        VincentOrleans("orleans-multiservice-pattern"),
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

    internal static IReadOnlyList<SkillManifestEntry> Preview { get; } =
    [
        ..All,
        DotnetAspNetCore("dotnet-webapi"),
        DotnetAspNetCore("minimal-api-file-upload")
    ];

    static SkillManifestEntry VincentDotnet(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Dotnet, plugin: "dotnet");

    static SkillManifestEntry VincentDotnet(string skill, IReadOnlyList<GateRequirement> gateRequirements)
        => Entry(VincentRepo, skill, TechnologyNames.Dotnet, gateRequirements, plugin: "dotnet");

    static SkillManifestEntry VincentOrleans(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Orleans, plugin: "orleans");

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
        string plugin = "")
        => new(sourceRepo, skill, skill, technology, gateRequirements ?? [], plugin);

    static IReadOnlyList<SkillManifestEntry> DotnetTestSkills()
        =>
        [
            DotnetTest("crap-score"),
            DotnetTest("detect-static-dependencies"),
            DotnetTest("dotnet-test-frameworks"),
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
            DotnetTest("test-anti-patterns"),
            DotnetTest("writing-mstest-tests")
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
