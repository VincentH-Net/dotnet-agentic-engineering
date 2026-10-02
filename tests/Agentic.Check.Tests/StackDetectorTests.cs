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

    [Theory]
    [InlineData("<PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup>", "")]
    [InlineData("<PropertyGroup><ToolCommandName>tool</ToolCommandName></PropertyGroup>", "")]
    [InlineData("<ItemGroup><PackageReference Include=\"System.CommandLine\" Version=\"2.0.9\" /></ItemGroup>", "")]
    [InlineData("<ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.3\" /><PackageReference Include=\"Hex1b\" Version=\"0.165.0\" /></ItemGroup>", "")]
    [InlineData("", "string? line = Console.ReadLine();")]
    [InlineData("", "while (!Console.KeyAvailable) { }")]
    public void DetectsInteractiveTerminalFromPositiveSignals(string projectContent, string code)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Tool/Tool.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">{projectContent}</Project>");
        tempDirectory.Write("Tool/Program.cs", code);

        var result = StackDetector.Detect(tempDirectory.Path);

        var gate = Assert.Single(result.InstallGates, gate => gate.Technology == TechnologyNames.Dotnet);
        Assert.Equal(["interactive"], gate.GetValues("terminal"));
        Assert.Equal(Path.Combine("Tool", "Tool.csproj"), gate.ProjectPath);
    }

    [Fact]
    public void GateProjectPathsAreRelativeToTheTargetNotTheWorkingDirectory()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("src/Cli/Cli.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup></Project>");
        tempDirectory.Write("src/App/App.csproj", "<Project Sdk=\"Uno.Sdk\"></Project>");
        Assert.NotEqual(tempDirectory.Path, Environment.CurrentDirectory);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Equal(
            [Path.Combine("src", "App", "App.csproj"), Path.Combine("src", "Cli", "Cli.csproj")],
            result.InstallGates.Select(gate => gate.ProjectPath).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(result.InstallGates, gate => gate.ProjectPath.StartsWith("..", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk", "")]
    [InlineData("Uno.Sdk", "<UnoFeatures>SkiaRenderer</UnoFeatures>")]
    [InlineData("Microsoft.NET.Sdk.Web", "")]
    [InlineData("Microsoft.NET.Sdk", "<UseMaui>true</UseMaui>")]
    [InlineData("Microsoft.NET.Sdk", "</PropertyGroup><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0.0\" /></ItemGroup><PropertyGroup>")]
    public void DoesNotDetectInteractiveTerminalFromExeAlone(string sdk, string extraContent)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "App/App.csproj",
            $"<Project Sdk=\"{sdk}\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>{extraContent}</PropertyGroup></Project>");
        tempDirectory.Write("App/Program.cs", "Console.WriteLine(\"Hello, World!\");");

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.Dotnet, result.Technologies);
        Assert.DoesNotContain(result.InstallGates, gate => gate.Technology == TechnologyNames.Dotnet);
    }

    [Fact]
    public void DetectScansOnlyTheRepositoryFiles()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tempDirectory.Write("App/Program.cs", "Console.ReadLine();");
        tempDirectory.Write("out/Stale/Stale.csproj", "<Project Sdk=\"Uno.Sdk\" />");

        var result = StackDetector.Detect(tempDirectory.Path, [Path.Combine(tempDirectory.Path, "App", "App.csproj")]);

        Assert.Contains(TechnologyNames.Dotnet, result.Technologies);
        Assert.DoesNotContain(TechnologyNames.Uno, result.Technologies);
        Assert.DoesNotContain(result.InstallGates, gate => gate.Technology == TechnologyNames.Dotnet);
    }

    [Fact]
    public void WalkSkipsTestResults()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App.csproj", "<Project />");
        tempDirectory.Write("TestResults/Old/Old.csproj", "<Project Sdk=\"Uno.Sdk\" />");

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.Dotnet, result.Technologies);
        Assert.DoesNotContain(TechnologyNames.Uno, result.Technologies);
    }

    [Fact]
    public void DetectsUnoGatesFromDirectoryBuildProps()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Directory.Build.props", "<Project><PropertyGroup><UnoFeatures>MVUX;Material</UnoFeatures></PropertyGroup></Project>");
        tempDirectory.Write("App/App.csproj", "<Project Sdk=\"Uno.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var result = StackDetector.Detect(tempDirectory.Path);

        var gates = Assert.Single(result.UnoGates);
        Assert.Equal(["mvux"], gates.Presentation);
        Assert.Equal(["material"], gates.Theme);
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

    [Theory]
    [InlineData("WebApp.csproj")]
    [InlineData("tests/WebApp/WebApp.csproj")]
    [InlineData("Test/WebApp/WebApp.csproj")]
    [InlineData("WebApp.Tests.csproj")]
    [InlineData("WebApp.Test.csproj")]
    public void DetectsAspNetCoreFromWebSdk(string projectPath)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            projectPath,
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
    public void DetectsAspNetCoreFromWebSdkInTestProject()
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

        Assert.Contains(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Theory]
    [InlineData("var builder = WebApplication.CreateBuilder(args);")]
    [InlineData("app.MapGet(\"/test\", () => \"ok\");")]
    public void DetectsAspNetCoreFromFrameworkReferenceAndHostingOrRoutingCodeInTestProject(string code)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            "tests/Host.Tests/Host.Tests.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
              </ItemGroup>
            </Project>
            """);
        tempDirectory.Write("tests/Host.Tests/HostingTests.cs", code);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Contains(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Theory]
    [InlineData("Host.csproj")]
    [InlineData("tests/Host.Tests/Host.Tests.csproj")]
    public void DoesNotDetectAspNetCoreFromFrameworkReferenceAlone(string projectPath)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """);

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.DoesNotContain(TechnologyNames.AspNetCore, result.Technologies);
    }

    [Fact]
    public void DoesNotDetectAspNetCoreFromHostingCodeWithoutFrameworkReference()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Host.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tempDirectory.Write("HostingTests.cs", "var builder = WebApplication.CreateBuilder(args);");

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
                <OutputType>Exe</OutputType>
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
        Assert.DoesNotContain(result.InstallGates, gate => gate.Technology == TechnologyNames.Dotnet);
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

        string presentationWarning = Assert.Single(result.Warnings, warning => warning.Contains("presentation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains($"{Environment.NewLine}  Mvvm.csproj: mvvm", presentationWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Environment.NewLine}  Mvux.csproj: mvux", presentationWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.NewLine, presentationWarning, StringComparison.Ordinal);

        string themeWarning = Assert.Single(result.Warnings, warning => warning.Contains("theme", StringComparison.OrdinalIgnoreCase));
        Assert.Contains($"{Environment.NewLine}  Mvvm.csproj: material", themeWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Environment.NewLine}  Mvux.csproj: simple", themeWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.NewLine, themeWarning, StringComparison.Ordinal);
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
        Assert.StartsWith(
            $"{Environment.NewLine}Warning: multiple Uno markup gate values detected (csharp, csharp2) - agents may become confused:",
            warning,
            StringComparison.Ordinal);
        Assert.Contains("csharp", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csharp2", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xaml", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Environment.NewLine}  CSharpMarkup.csproj: csharp", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Environment.NewLine}  CSharpMarkup2.csproj: csharp2", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.NewLine, warning, StringComparison.Ordinal);
    }
}
