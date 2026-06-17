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
        Assert.DoesNotContain(plan, skill => skill.Technology == TechnologyNames.Uno);
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
