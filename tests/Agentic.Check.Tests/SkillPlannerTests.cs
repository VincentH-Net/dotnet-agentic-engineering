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

    [Fact]
    public void PlansCliE2eTestingSkillForDotnetCliGate()
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
                        ["cli"] = ["cli"]
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
        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-aspnetcore/skills/minimal-api-file-upload");
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
        foreach (var stable in StaticSkillManifest.All.Where(skill =>
            skill.SourceRepo != "dotnet/skills" || skill.LocalFolder is not ("dotnet-test-frameworks" or "test-anti-patterns")))
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
        Assert.True(IndexOf(plan, "VincentH-Net/dotnet-agentic-engineering", "dotnet") < IndexOf(plan, "mtmattei/UnoPlatformSkills", "UnoPlatformSkills"));
        Assert.True(IndexOf(plan, "mtmattei/UnoPlatformSkills", "UnoPlatformSkills") < IndexOf(plan, "dotnet/skills", "dotnet-test"));

        static int IndexOf(IReadOnlyList<SkillManifestEntry> skills, string sourceRepo, string plugin)
            => Array.FindIndex(
                [.. skills],
                skill => skill.SourceRepo.Equals(sourceRepo, StringComparison.OrdinalIgnoreCase)
                    && skill.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlansUnoMvuxMaterialAndCsharpSkillsFromGates()
    {
        StackDetectionResult stack = new(
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
                    ["material"])
            ],
            []);

        var plan = SkillPlanner.Plan(StaticSkillManifest.All, stack);

        Assert.Contains(plan, skill => skill.InstallArg == "uno-mvux-overview");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-toolkit-csharp-markup");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-themes-material");
        Assert.Contains(plan, skill => skill.InstallArg == "uno-themes-semantic-colors-brushes");
        Assert.DoesNotContain(plan, skill => skill.InstallArg == "uno-mvvm");
        Assert.DoesNotContain(plan, skill => skill.InstallArg == "uno-themes-simple");
    }

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
