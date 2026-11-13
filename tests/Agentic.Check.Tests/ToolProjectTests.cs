using System.Xml.Linq;

namespace Agentic.Check.Tests;

public sealed class ToolProjectTests
{
    // A dotnet tool targets one .NET major and by default does not start on a machine that has only a newer
    // runtime, such as .NET 11 without .NET 10. Every tool of this repository opts in to rolling forward, so a
    // tool project that is added later is held to the same.
    [Fact]
    public void EveryToolStartsOnANewerMajorRuntime()
    {
        string source = Path.Combine(CompanionTests.CheckoutRoot(), "src");
        string[] tools = [.. Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories)
            .Where(project => Property(project, "PackAsTool") == "true")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)];

        Assert.Equal(["Agentic.Check.csproj", "Agentic.csproj", "Dna.csproj"], tools.Select(Path.GetFileName));
        Assert.All(tools, project => Assert.Equal("Major", Property(project, "RollForward")));
    }

    static string? Property(string project, string name)
        => XDocument.Load(project).Descendants(name).SingleOrDefault()?.Value;
}
