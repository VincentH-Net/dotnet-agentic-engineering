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

        Assert.DoesNotContain(plan, skill => skill.InstallArg == "ensure-directives");
        Assert.Contains(plan, skill => skill.InstallArg == "dotnet-livecharts2");
        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-test/skills/run-tests@main");
        Assert.DoesNotContain(plan, skill => skill.Plugin == "dotnet-aspnetcore");
        Assert.DoesNotContain(plan, skill => skill.Technology == TechnologyNames.Uno);
    }

    [Fact]
    public void PlansAspNetCoreSkillsForAspNetCoreStack()
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

        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-aspnetcore/skills/dotnet-webapi@main");
        Assert.Contains(plan, skill => skill.InstallArg == "plugins/dotnet-aspnetcore/skills/minimal-api-file-upload@main");
    }

    [Fact]
    public void DotnetTestRunTestsDeclaresDependencies()
    {
        var runTests = Assert.Single(
            StaticSkillManifest.All,
            skill => skill.InstallArg == "plugins/dotnet-test/skills/run-tests@main");

        Assert.Contains(runTests.Dependencies, dependency => dependency.InstallArg == "plugins/dotnet-test/skills/platform-detection@main");
        Assert.Contains(runTests.Dependencies, dependency => dependency.InstallArg == "plugins/dotnet-test/skills/filter-syntax@main");
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
        Assert.True(IndexOf(plan, "mtmattei/UnoPlatformSkills", "UnoPlatformSkills") < IndexOf(plan, "dotnet/skills", "dotnet-aspnetcore"));
        Assert.True(IndexOf(plan, "dotnet/skills", "dotnet-aspnetcore") < IndexOf(plan, "dotnet/skills", "dotnet-test"));

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
