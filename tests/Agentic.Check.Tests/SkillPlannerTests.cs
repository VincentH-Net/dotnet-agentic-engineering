namespace Agentic.Check.Tests;

public sealed class SkillPlannerTests
{
    [Fact]
    public void PlansDotnetSkillsForDotnetStack()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet
            },
            [],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "dotnet-livecharts2");
        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-test/skills/run-tests");
        Assert.DoesNotContain(plan, skill => skill.InstallArg == "cli-e2e-testing");
        Assert.DoesNotContain(plan, skill => skill.Plugin == "dotnet-aspnetcore");
        Assert.DoesNotContain(plan, skill => skill.Technology == TechnologyNames.Uno);
    }

    // dotnet-breaking-changes is for code whose callers are outside the repository. dotnet/skills has it on
    // its default branch only, so it is a preview skill.
    [Fact]
    public void OffersBreakingChangeGuidanceOnPreviewOnlyToARepositoryThatPublishesALibrary()
    {
        const string breakingChanges = "plugins/dotnet/skills/dotnet-breaking-changes";
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation, TechnologyNames.Dotnet };
        StackDetectionResult published = new(
            technologies,
            [new InstallGateReport(TechnologyNames.Dotnet, "Lib.csproj", new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["library"] = ["published"] })],
            []);
        StackDetectionResult plain = new(technologies, [], []);

        var offered = Assert.Single(SkillPlanner.Plan(StaticSkillManifest.Preview, published), skill => skill.InstallArg == breakingChanges);
        Assert.Equal("dotnet-breaking-changes", offered.LocalFolder);
        Assert.Equal("dotnet/skills", offered.SourceRepo);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.Preview, plain), skill => skill.InstallArg == breakingChanges);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, published), skill => skill.InstallArg == breakingChanges);
    }

    // The .NET 11 Process APIs exist only for a project that targets net11.0 or later. dotnet/skills has the
    // skill on its default branch only, so it is a preview skill.
    [Fact]
    public void OffersNet11GuidanceOnPreviewOnlyToARepositoryThatTargetsNet11()
    {
        const string processApi = "plugins/dotnet11/skills/process-api-net11";
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation, TechnologyNames.Dotnet };
        StackDetectionResult net11 = new(
            technologies,
            [new InstallGateReport(TechnologyNames.Dotnet, "Api.csproj", new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["framework"] = ["net11"] })],
            []);
        StackDetectionResult net10 = new(technologies, [], []);

        var offered = Assert.Single(SkillPlanner.Plan(StaticSkillManifest.Preview, net11), skill => skill.InstallArg == processApi);
        Assert.Equal("process-api-net11", offered.LocalFolder);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.Preview, net10), skill => skill.InstallArg == processApi);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, net11), skill => skill.InstallArg == processApi);
    }

    // The migration to .NET 11 is for a project that still targets .NET 10; dotnet/skills has it in a release.
    [Fact]
    public void OffersTheNet11SkillsByTheDotnetVersionAProjectTargets()
    {
        const string migrate = "plugins/dotnet-upgrade/skills/migrate-dotnet10-to-dotnet11";
        const string json = "plugins/dotnet11/skills/system-text-json-net11";
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation, TechnologyNames.Dotnet };
        StackDetectionResult Targets(params string[] frameworks)
            => new(technologies, [new InstallGateReport(TechnologyNames.Dotnet, "App.csproj", new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["framework"] = frameworks })], []);

        foreach (var manifest in new[] { StaticSkillManifest.All, StaticSkillManifest.Preview })
        {
            Assert.Contains(SkillPlanner.Plan(manifest, Targets("net10")), skill => skill.InstallArg == migrate);
            Assert.DoesNotContain(SkillPlanner.Plan(manifest, Targets("net11")), skill => skill.InstallArg == migrate);
            Assert.DoesNotContain(SkillPlanner.Plan(manifest, new(technologies, [], [])), skill => skill.InstallArg == migrate);
        }

        Assert.Contains(SkillPlanner.Plan(StaticSkillManifest.Preview, Targets("net11")), skill => skill.InstallArg == json);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.Preview, Targets("net10")), skill => skill.InstallArg == json);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, Targets("net11")), skill => skill.InstallArg == json);
        Assert.Contains(SkillPlanner.Plan(StaticSkillManifest.Preview, Targets("net10", "net11")), skill => skill.InstallArg == migrate);
    }

    [Fact]
    public void OffersTheRefactoringSkillToEveryDotnetRepositoryOnBothChannels()
    {
        StackDetectionResult dotnet = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation, TechnologyNames.Dotnet }, [], []);

        foreach (var manifest in new[] { StaticSkillManifest.All, StaticSkillManifest.Preview })
        {
            var skill = Assert.Single(SkillPlanner.Plan(manifest, dotnet), skill => skill.InstallArg == "dotnet-refactoring");
            Assert.Equal("VincentH-Net/dotnet-agentic-engineering", skill.SourceRepo);
            Assert.Empty(skill.GateRequirements);
        }
    }

    [Fact]
    public void OffersTestValueEvidenceOnPreviewToEveryDotnetRepository()
    {
        const string testValueEvidence = "plugins/dotnet-test/skills/test-value-evidence";
        StackDetectionResult dotnet = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Foundation, TechnologyNames.Dotnet }, [], []);

        Assert.Contains(SkillPlanner.Plan(StaticSkillManifest.Preview, dotnet), skill => skill.InstallArg == testValueEvidence);
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, dotnet), skill => skill.InstallArg == testValueEvidence);
    }

    [Fact]
    public void PlansCliE2eTestingSkillForInteractiveTerminalGate()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet
            },
            [
                new InstallGateReport(
                    TechnologyNames.Dotnet,
                    "Tool.csproj",
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["terminal"] = ["interactive"]
                    })
            ],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "cli-e2e-testing");
    }

    [Fact]
    public void DoesNotPlanUnreleasedAspNetCoreSkillsForAspNetCoreStack()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet,
                TechnologyNames.AspNetCore
            },
            [],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.DoesNotContain(plan, skill => skill.Plugin == "dotnet-aspnetcore");
    }

    [Fact]
    public void PreviewPlansAspNetCoreDefaultBranchSkillsForAspNetCoreStack()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet,
                TechnologyNames.AspNetCore
            },
            [],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.Preview, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-aspnetcore/skills/dotnet-webapi");
        Assert.DoesNotContain(plan, skill => skill.InstallArg.Contains("minimal-api-file-upload", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewReplacesFrameworkReferenceWithoutChangingStableManifest()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TechnologyNames.Dotnet }, [], []);

        var stable = SkillPlanner.Plan(StaticSkillManifest.All, stack);
        var preview = SkillPlanner.Plan(StaticSkillManifest.Preview, stack);

        Assert.Contains(stable, skill => skill.InstallArg == "plugins/dotnet-test/skills/dotnet-test-frameworks");
        Assert.DoesNotContain(stable, skill => skill.LocalFolder == "test-analysis-extensions");
        Assert.DoesNotContain(preview, skill => skill.LocalFolder == "dotnet-test-frameworks");
        var reference = Assert.Single(preview, skill => skill.LocalFolder == "test-analysis-extensions");
        Assert.Equal("dotnet/skills", reference.SourceRepo);
        Assert.Equal("plugins/dotnet-test/skills/test-analysis-extensions", reference.InstallArg);
        Assert.Equal("dotnet-test", reference.Plugin);
        Assert.Equal(TechnologyNames.Dotnet, reference.Technology);
        Assert.Empty(reference.GateRequirements);

        var dependent = Assert.Single(preview, skill => skill.LocalFolder == "test-anti-patterns");
        Assert.Equal(reference.Key, Assert.Single(dependent.Dependencies).Key);
        Assert.Empty(Assert.Single(stable, skill => skill.LocalFolder == "test-anti-patterns").Dependencies);
    }

    [Fact]
    public void PreviewPreservesOtherStableSkillsAndDependencyKeys()
    {
        // Preview replaces the dotnet-test reference skill and every per-topic Studio skill.
        foreach (var stable in StaticSkillManifest.All.Where(skill =>
            skill.SourceRepo != "unoplatform/studio"
            && (skill.SourceRepo != "dotnet/skills" || skill.LocalFolder is not ("dotnet-test-frameworks" or "test-anti-patterns"))))
        {
            var preview = Assert.Single(StaticSkillManifest.Preview, skill => skill.Key == stable.Key);
            Assert.Equal(stable.LocalFolder, preview.LocalFolder);
            Assert.Equal(stable.Technology, preview.Technology);
            Assert.Equal(stable.Plugin, preview.Plugin);
            Assert.Equal(stable.GateRequirements, preview.GateRequirements);
            Assert.Equal(stable.Dependencies, preview.Dependencies);
        }

        Assert.Equal(StaticSkillManifest.Preview.Count, StaticSkillManifest.Preview.Select(skill => skill.Key).Distinct().Count());
        foreach (var dependency in StaticSkillManifest.Preview.SelectMany(skill => skill.Dependencies))
        {
            Assert.Contains(StaticSkillManifest.Preview, skill => skill.Key == dependency.Key);
        }
    }

    [Fact]
    public void DotnetTestRunTestsDeclaresDependencies()
    {
        var runTests = Assert.Single(
            StaticSkillManifest.All,
            skill => skill.InstallArg == "plugins/dotnet-test/skills/run-tests");

        Assert.Contains(runTests.Dependencies, dependency => dependency.InstallArg == "plugins/dotnet-test/skills/platform-detection");
        Assert.Contains(runTests.Dependencies, dependency => dependency.InstallArg == "plugins/dotnet-test/skills/filter-syntax");
    }

    [Fact]
    public void OrdersSkillsByDependencyStack()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet,
                TechnologyNames.AspNetCore,
                TechnologyNames.Uno
            },
            [
                new UnoGateReport(
                    "App.csproj",
                    ["mvvm"],
                    ["xaml", "csharp"],
                    ["material"])
            ],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.True(IndexOf(plan, "VincentH-Net/dotnet-agentic-engineering", "uno-platform") < IndexOf(plan, "VincentH-Net/dotnet-agentic-engineering", "dotnet"));
        Assert.True(IndexOf(plan, "VincentH-Net/dotnet-agentic-engineering", "dotnet") < IndexOf(plan, "noknowscode/UnoPlatformSkills", "UnoPlatformSkills"));
        Assert.True(IndexOf(plan, "noknowscode/UnoPlatformSkills", "UnoPlatformSkills") < IndexOf(plan, "dotnet/skills", "dotnet-test"));

        static int IndexOf(IReadOnlyList<SkillManifestEntry> skills, string sourceRepo, string plugin)
            => Array.FindIndex(
                [.. skills],
                skill => skill.SourceRepo.Equals(sourceRepo, StringComparison.OrdinalIgnoreCase)
                    && skill.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlansUnoMvuxMaterialAndCsharpSkillsFromGates()
    {
        var plan = SkillPlanner.Plan(StaticSkillManifest.All, UnoMvuxMaterialStack("6.7.49"));

        Assert.Contains(plan, skill => skill.InstallArg == "uno-mvux-overview");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-toolkit-csharp-markup");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-themes-material");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-csharp-markup");
        Assert.DoesNotContain(plan, skill => skill.InstallArg == "uno-mvvm");
        Assert.DoesNotContain(plan, skill => skill.InstallArg == "uno-themes-simple");
    }

    // Studio writes its skills for one Uno.Sdk line: per-topic skills until its 7.0 release, hubs from then on.
    [Theory]
    [InlineData("6.7.49")]
    [InlineData("6.8.0-dev.70")]
    [InlineData("5.2.161")]
    public void ARepositoryBeforeUnoSdk7GetsThePerTopicStudioSkillsPinnedToTheirRelease(string sdk)
    {
        foreach (var manifest in new[] { StaticSkillManifest.All, StaticSkillManifest.Preview })
        {
            var studio = SkillPlanner.Plan(manifest, UnoMvuxMaterialStack(sdk)).Where(skill => skill.SourceRepo == Studio).ToArray();

            Assert.Contains(studio, skill => skill.InstallArg == "uno-mvux-overview");
            Assert.Contains(studio, skill => skill.InstallArg == "uno-toolkit-navigationbar");
            Assert.DoesNotContain(studio, skill => skill.InstallArg is "uno-mvux" or "uno-toolkit" or "uno-platform");
            Assert.All(studio, skill => Assert.Equal("1.1.0", skill.PinnedRef));
            Assert.All(studio, skill => Assert.Equal("1.1.0", skill.SourceRef));
        }
    }

    // An Uno.Sdk version the check cannot find counts as the latest, which is what a new project has.
    [Theory]
    [InlineData("7.0.5")]
    [InlineData("7.0.0-dev.77")]
    [InlineData("8.1.2")]
    [InlineData(null)]
    public void ARepositoryFromUnoSdk7GetsTheStudioHubsFromItsChannel(string? sdk)
    {
        foreach (var manifest in new[] { StaticSkillManifest.All, StaticSkillManifest.Preview })
        {
            var studio = SkillPlanner.Plan(manifest, UnoMvuxMaterialStack(sdk)).Where(skill => skill.SourceRepo == Studio).ToArray();

            Assert.Equal(
                ["uno-authentication", "uno-build-app", "uno-kiota", "uno-mvux", "uno-navigation", "uno-platform", "uno-testing", "uno-themes", "uno-toolkit"],
                studio.Select(skill => skill.InstallArg));
            Assert.All(studio, skill => Assert.Empty(skill.PinnedRef));
        }
    }

    [Fact]
    public void ARepositoryWithUnoProjectsOnBothSidesOfUnoSdk7GetsBothStudioSets()
    {
        var older = UnoMvuxMaterialStack("6.7.49");
        StackDetectionResult stack = new(older.Technologies, [.. older.InstallGates, new UnoGateReport("New.csproj", [], ["xaml"], ["fluent"], "7.0.5")], []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "uno-mvux-overview");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-mvux");
    }

    [Fact]
    public void TheUnoSdkOfASkillNarrowsItsOtherGates()
    {
        // uno-mvux needs MVUX and Uno.Sdk 7.0 or later: neither alone is enough.
        StackDetectionResult mvvmOn7 = new(UnoMvuxMaterialStack("7.0.5").Technologies, [new UnoGateReport("App.csproj", ["mvvm"], ["xaml"], ["material"], "7.0.5")], []);

        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, mvvmOn7), skill => skill.InstallArg == "uno-mvux");
        Assert.DoesNotContain(SkillPlanner.Plan(StaticSkillManifest.All, UnoMvuxMaterialStack("6.7.49")), skill => skill.InstallArg == "uno-mvux");
    }

    const string Studio = "unoplatform/studio";

    static StackDetectionResult UnoMvuxMaterialStack(string? sdk)
        => new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Dotnet,
                TechnologyNames.Uno
            },
            [
                new UnoGateReport(
                    "App.csproj",
                    ["mvux"],
                    ["xaml", "csharp"],
                    ["material"],
                    sdk)
            ],
            []);

    [Fact]
    public void PlansUnoFluentForDefaultTheme()
    {
        StackDetectionResult stack = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TechnologyNames.Foundation,
                TechnologyNames.Uno
            },
            [
                new UnoGateReport(
                    "App.csproj",
                    [],
                    ["xaml"],
                    ["fluent"])
            ],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "uno-fluent2");
        Assert.Contains(plan, skill => skill.LocalFolder == "uno-xaml");
    }
}
