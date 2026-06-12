namespace Agentic.Check;

sealed record SkillManifestEntry(
    string SourceRepo,
    string InstallArg,
    string LocalFolder,
    string Technology,
    IReadOnlyList<GateRequirement> GateRequirements)
{
    public string Display => $"{SourceRepo} {InstallArg}";
}

sealed record GateRequirement(string Gate, string Value);

static class StaticSkillManifest
{
    const string VincentRepo = "VincentH-Net/dotnet-agentic-engineering";
    const string UnoStudioRepo = "unoplatform/studio";
    const string MattRepo = "mtmattei/UnoPlatformSkills";

    internal static IReadOnlyList<SkillManifestEntry> All { get; } =
    [
        VincentDotnet("ensure-directives"),
        VincentDotnet("dotnet-livecharts2"),
        VincentDotnet("dotnet-modern-csharp-editorconfig"),
        VincentOrleans("orleans-result-pattern"),
        VincentOrleans("orleans-multiservice-pattern"),
        VincentUno("uno-agentic-support"),
        VincentUno("uno-mvvm", "uno-mvvm@main", "presentation", "mvvm"),
        VincentUno("uno-csharpmarkup2", "markup", "csharp2"),
        VincentUno("uno-xaml", "uno-xaml@main", "markup", "xaml"),
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

    static SkillManifestEntry VincentDotnet(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Dotnet);

    static SkillManifestEntry VincentOrleans(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Orleans);

    static SkillManifestEntry VincentUno(string skill)
        => Entry(VincentRepo, skill, TechnologyNames.Uno);

    static SkillManifestEntry VincentUno(string skill, string gate, string value)
        => Entry(VincentRepo, skill, TechnologyNames.Uno, [new(gate, value)]);

    static SkillManifestEntry VincentUno(string localFolder, string installArg, string gate, string value)
        => new(VincentRepo, installArg, localFolder, TechnologyNames.Uno, [new(gate, value)]);

    static SkillManifestEntry MattUno(string skill)
        => Entry(MattRepo, skill, TechnologyNames.Uno);

    static SkillManifestEntry MattUno(string skill, string gate, string value)
        => Entry(MattRepo, skill, TechnologyNames.Uno, [new(gate, value)]);

    static SkillManifestEntry UnoStudio(string skill)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno);

    static SkillManifestEntry UnoStudio(string skill, string gate, string value)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno, [new(gate, value)]);

    static SkillManifestEntry UnoStudio(string skill, IReadOnlyList<GateRequirement> gateRequirements)
        => Entry(UnoStudioRepo, skill, TechnologyNames.Uno, gateRequirements);

    static SkillManifestEntry Entry(
        string sourceRepo,
        string skill,
        string technology,
        IReadOnlyList<GateRequirement>? gateRequirements = null)
        => new(sourceRepo, skill, skill, technology, gateRequirements ?? []);

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
            .OrderBy(entry => entry.SourceRepo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.InstallArg, StringComparer.OrdinalIgnoreCase)];

    static bool HasGate(StackDetectionResult stack, GateRequirement requirement)
        => stack.UnoGates.Any(report => report.GetValues(requirement.Gate).Contains(requirement.Value, StringComparer.OrdinalIgnoreCase));
}
