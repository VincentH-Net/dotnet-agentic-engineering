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

    // A published library: it tracks its public API, or it is explicitly packaged.
    [Theory]
    [InlineData("<PropertyGroup><PackageId>Contoso.Core</PackageId></PropertyGroup>", null)]
    [InlineData("<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>", null)]
    [InlineData("<PropertyGroup><GeneratePackageOnBuild>true</GeneratePackageOnBuild></PropertyGroup>", null)]
    [InlineData("<PropertyGroup><EnablePackageValidation>true</EnablePackageValidation></PropertyGroup>", null)]
    [InlineData("<ItemGroup><PackageReference Include=\"Microsoft.CodeAnalysis.PublicApiAnalyzers\" Version=\"4.14.0\" /></ItemGroup>", null)]
    [InlineData("", "PublicAPI.Shipped.txt")]
    [InlineData("", "PublicAPI/net10.0/PublicAPI.Unshipped.txt")]
    public void DetectsAPublishedLibraryFromPackagingOrPublicApiTracking(string projectContent, string? file)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Lib/Lib.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">{projectContent}</Project>");
        if (file is not null)
        {
            tempDirectory.Write("Lib/" + file, "#nullable enable\n");
        }

        var result = StackDetector.Detect(tempDirectory.Path);

        var gate = Assert.Single(result.InstallGates, gate => gate.Technology == TechnologyNames.Dotnet);
        Assert.Equal(["published"], gate.GetValues("library"));
        Assert.DoesNotContain("terminal", gate.Values.Keys);
        Assert.Equal(Path.Combine("Lib", "Lib.csproj"), gate.ProjectPath);
    }

    // A class library is packable by default and often multi-targets or has friend assemblies: none of
    // that says its callers are outside the repository. A tool's users call its commands, not its API.
    [Theory]
    [InlineData("")]
    [InlineData("<PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>")]
    [InlineData("<ItemGroup><InternalsVisibleTo Include=\"Lib.Tests\" /></ItemGroup>")]
    [InlineData("<PropertyGroup><PackAsTool>true</PackAsTool><PackageId>Contoso.Tool</PackageId></PropertyGroup>")]
    [InlineData("<PropertyGroup><IsPackable>false</IsPackable><PackageId>Contoso.Tests</PackageId></PropertyGroup>")]
    public void AnInternalLibraryOrAToolIsNotAPublishedLibrary(string projectContent)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Lib/Lib.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\">{projectContent}</Project>");
        tempDirectory.Write("Lib/notes.txt", "PublicAPI.Shipped.txt is not here");

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.DoesNotContain(result.InstallGates, gate => gate.GetValues("library").Count > 0);
    }

    [Fact]
    public void PackagingInADirectoryBuildFileCountsUnlessTheProjectOptsOut()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Directory.Build.props", "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>");
        tempDirectory.Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tempDirectory.Write("tests/Lib.Tests/Lib.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");

        var result = StackDetector.Detect(tempDirectory.Path);

        var gate = Assert.Single(result.InstallGates);
        Assert.Equal(Path.Combine("src", "Lib", "Lib.csproj"), gate.ProjectPath);
        Assert.Equal(["published"], gate.GetValues("library"));
    }

    [Fact]
    public void AProjectCanBeBothInteractiveAndAPublishedLibrary()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Cli/Cli.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><PackageId>Contoso.Cli.Core</PackageId></PropertyGroup>
              <ItemGroup><PackageReference Include="Spectre.Console" Version="0.57.1" /></ItemGroup>
            </Project>
            """);

        var gate = Assert.Single(StackDetector.Detect(tempDirectory.Path).InstallGates);

        Assert.Equal(["interactive"], gate.GetValues("terminal"));
        Assert.Equal(["published"], gate.GetValues("library"));
    }

    // "net10" for a project that still targets .NET 10, "net11" for one on .NET 11 or later; both when it has both.
    [Theory]
    [InlineData("<TargetFramework>net11.0</TargetFramework>", "net11")]
    [InlineData("<TargetFrameworks>net10.0;net11.0-windows10.0.19041</TargetFrameworks>", "net10,net11")]
    [InlineData("<TargetFramework>net12.0-android</TargetFramework>", "net11")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>", "net10")]
    [InlineData("<TargetFrameworks>net10.0-android;net10.0-desktop</TargetFrameworks>", "net10")]
    [InlineData("<TargetFrameworks>net48;netstandard2.0;netcoreapp3.1;net9.0</TargetFrameworks>", "")]
    [InlineData("<TargetFramework>$(AppTargetFramework)</TargetFramework>", "")]
    [InlineData("", "")]
    public void DetectsWhichDotnetVersionAProjectTargets(string targetFrameworks, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("App/App.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{targetFrameworks}</PropertyGroup></Project>");

        var result = StackDetector.Detect(tempDirectory.Path);

        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), result.InstallGates.SelectMany(gate => gate.GetValues("framework")));
    }

    [Fact]
    public void ATargetFrameworkInADirectoryBuildFileCountsUnlessTheProjectNamesItsOwn()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>");
        tempDirectory.Write("src/New/New.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tempDirectory.Write("src/Old/Old.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var gates = StackDetector.Detect(tempDirectory.Path).InstallGates.ToDictionary(gate => gate.ProjectPath, gate => gate.GetValues("framework"));

        Assert.Equal(["net11"], gates[Path.Combine("src", "New", "New.csproj")]);
        Assert.Equal(["net10"], gates[Path.Combine("src", "Old", "Old.csproj")]);
    }

    const string UnoGlobalJson = """{ "msbuild-sdks": { "Uno.Sdk": "6.7.49" } }""";

    [Fact]
    public void ReadsTheUnoSdkVersionFromTheNearestGlobalJsonThatNamesIt()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("global.json", UnoGlobalJson);
        tempDirectory.Write("src/global.json", """
            {
              // Comments and trailing commas are allowed in global.json.
              "sdk": { "version": "10.0.100" },
              "msbuild-sdks": { "Other.Sdk": "1.0.0", },
            }
            """);
        tempDirectory.Write("src/App/App.csproj", "<Project Sdk=\"Uno.Sdk\" />");

        var result = StackDetector.Detect(tempDirectory.Path);

        var gate = Assert.Single(result.UnoGates);
        Assert.Equal("6.7.49", gate.Sdk);
        Assert.DoesNotContain("sdk", gate.Values.Keys);
        Assert.Equal(new ToolVersion(6, 7, 49, string.Empty), gate.SdkVersion);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Uno.Sdk/7.0.5\" />", "7.0.5")]
    [InlineData("<Project Sdk=\"Uno.Sdk/7.1.0-dev.11\" />", "7.1.0-dev.11")]
    [InlineData("<Project><Sdk Name=\"Uno.Sdk\" Version=\"7.0.5\" /></Project>", "7.0.5")]
    public void TheUnoSdkVersionOnTheProjectWinsOverGlobalJson(string project, string expected)
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write("global.json", UnoGlobalJson);
        tempDirectory.Write("App.csproj", project);

        var gate = Assert.Single(StackDetector.Detect(tempDirectory.Path).UnoGates);

        Assert.Equal(expected, gate.Sdk);
    }

    [Fact]
    public void ReadsGlobalJsonAboveTheTargetUpToTheRepositoryRoot()
    {
        using TempDirectory tempDirectory = new();
        tempDirectory.Write(".git/HEAD", "ref: refs/heads/main");
        tempDirectory.Write("global.json", UnoGlobalJson);
        tempDirectory.Write("apps/mobile/App.csproj", "<Project Sdk=\"Uno.Sdk\" />");

        var gate = Assert.Single(StackDetector.Detect(Path.Combine(tempDirectory.Path, "apps", "mobile")).UnoGates);

        Assert.Equal("6.7.49", gate.Sdk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ \"msbuild-sdks\": { \"Uno.Sdk\": \"$(UnoSdkVersion)\" } }")]
    [InlineData("{ \"msbuild-sdks\": { \"Uno.Sdk\": 7 } }")]
    [InlineData("{ \"sdk\": { \"version\": \"10.0.100\" } }")]
    [InlineData("not json")]
    public void AnUnoSdkVersionThatCannotBeFoundIsUnknownAndSaidSo(string? globalJson)
    {
        using TempDirectory tempDirectory = new();
        if (globalJson is not null)
        {
            tempDirectory.Write("global.json", globalJson);
        }

        tempDirectory.Write("App.csproj", "<Project Sdk=\"Uno.Sdk\" />");

        var result = StackDetector.Detect(tempDirectory.Path);

        var gate = Assert.Single(result.UnoGates);
        Assert.Null(gate.Sdk);
        Assert.Null(gate.SdkVersion);
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith("Could not find the Uno.Sdk version of App.csproj:", warning, StringComparison.Ordinal);
        Assert.EndsWith("Skills for the latest Uno.Sdk are offered.", warning, StringComparison.Ordinal);
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
        Assert.DoesNotContain(result.InstallGates, gate => gate.GetValues("terminal").Count > 0);
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
        tempDirectory.Write("global.json", UnoGlobalJson);
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
        tempDirectory.Write("global.json", UnoGlobalJson);
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
