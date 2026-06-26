namespace Agentic.Check.Tests;

public sealed class StackDetectorTests
{
    [Fact]
    public void DetectsDotnetWhenProjectExists()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.Foundation, result.Technologies);
        Assert.Contains(TechnologyNames.Dotnet, result.Technologies);
        Assert.DoesNotContain(TechnologyNames.Uno, result.Technologies);
    }

    [Fact]
    public void DetectsOrleansPackageReferences()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "App.csproj",
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Microsoft.Orleans.Server" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.Orleans, result.Technologies);
    }

    [Fact]
    public void DetectsAspNetCoreFromWebSdk()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "WebApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Fact]
    public void DetectsAspNetCoreFromFrameworkReferenceAndHostingCode()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "Host.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """);
        tempDirectory.Write(
            "Program.cs",
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => "ok");
            app.Run();
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Fact]
    public void DoesNotDetectAspNetCoreForTestProject()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "tests/WebApp.Tests/WebApp.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.DoesNotContain(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Fact]
    public void DetectsUnoGatesFromFeaturesAndPackages()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "App.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVUX;CSharpMarkup;Material</UnoFeatures>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="CSharpMarkup.WinUI" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);
        var gates = Assert.Single(result.UnoGates);

        Assert.Contains(TechnologyNames.Uno, result.Technologies);
        Assert.Contains("mvux", gates.Presentation);
        Assert.Contains("xaml", gates.Markup);
        Assert.Contains("csharp", gates.Markup);
        Assert.Contains("csharp2", gates.Markup);
        Assert.Contains("material", gates.Theme);
    }

    [Fact]
    public void WarnsWhenUnoGateHasMultipleValues()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "Mvvm.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVVM;Material</UnoFeatures>
              </PropertyGroup>
            </Project>
            """);
        tempDirectory.Write(
            "Mvux.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVUX;SimpleTheme</UnoFeatures>
              </PropertyGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(result.Warnings, warning => warning.Contains("presentation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("theme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DoesNotWarnWhenUnoMarkupHasXamlAndCSharp()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "Markup.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>CSharpMarkup</UnoFeatures>
              </PropertyGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("markup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WarnsWhenUnoMarkupHasCSharpAndCSharp2WithoutListingXaml()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "CSharpMarkup.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>CSharpMarkup</UnoFeatures>
              </PropertyGroup>
            </Project>
            """);
        tempDirectory.Write(
            "CSharpMarkup2.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <ItemGroup>
                <PackageReference Include="CSharpMarkup.WinUI" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        string warning = Assert.Single(result.Warnings, warning => warning.Contains("markup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("csharp", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csharp2", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xaml", warning, StringComparison.OrdinalIgnoreCase);
    }
}
