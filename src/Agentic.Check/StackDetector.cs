using System.Xml.Linq;

namespace Agentic.Check;

static class StackDetector
{
    static readonly string[] ExcludedDirectoryNames = [".git", ".vs", "bin", "obj", "node_modules"];

    internal static StackDetectionResult Detect(string repoRoot)
    {
        List<string> warnings = [];
        List<UnoGateReport> unoGateReports = [];
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase)
        {
            TechnologyNames.Foundation
        };

        var projectFiles = EnumerateFiles(repoRoot, "*.csproj");
        IReadOnlyList<string> propsTargetsFiles = [.. EnumerateFiles(repoRoot, "*.props")
, .. EnumerateFiles(repoRoot, "*.targets")];

        if (projectFiles.Count > 0)
        {
            _ = technologies.Add(TechnologyNames.Dotnet);
        }

        bool unoDetected = projectFiles.Concat(propsTargetsFiles).Any(file => FileContains(file, "Uno.Sdk"));
        if (unoDetected)
        {
            _ = technologies.Add(TechnologyNames.Uno);
            unoGateReports.AddRange(DetectUnoGates(projectFiles, warnings));
        }

        if (projectFiles.Any(HasOrleansReference))
        {
            _ = technologies.Add(TechnologyNames.Orleans);
        }

        if (projectFiles.Any(IsAspNetCoreProject))
        {
            _ = technologies.Add(TechnologyNames.AspNetCore);
        }

        AddMultiValueWarnings(unoGateReports, warnings);
        return new StackDetectionResult(technologies, unoGateReports, warnings);
    }

    static List<UnoGateReport> DetectUnoGates(IReadOnlyList<string> projectFiles, List<string> warnings)
    {
        List<UnoGateReport> reports = [];
        foreach (string projectFile in projectFiles)
        {
            string content = File.ReadAllText(projectFile);
            var document = TryParseProject(projectFile, content, warnings);
            if (!IsUnoProject(document, content))
            {
                continue;
            }

            HashSet<string> presentation = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> markup = new(StringComparer.OrdinalIgnoreCase)
            {
                "xaml"
            };
            HashSet<string> theme = new(StringComparer.OrdinalIgnoreCase);

            if (ContainsUnoFeature(document, "mvux") || HasPackageReference(document, "Uno.Extensions.Reactive.WinUI"))
            {
                _ = presentation.Add("mvux");
            }

            if (ContainsUnoFeature(document, "mvvm") || HasPackageReference(document, "CommunityToolkit.Mvvm"))
            {
                _ = presentation.Add("mvvm");
            }

            if (ContainsUnoFeature(document, "csharpmarkup") || HasPackageReference(document, "Uno.WinUI.Markup"))
            {
                _ = markup.Add("csharp");
            }

            if (HasPackageReference(document, "CSharpMarkup.WinUI"))
            {
                _ = markup.Add("csharp2");
            }

            if (ContainsUnoFeature(document, "cupertino") || HasPackageReference(document, "Uno.Cupertino.WinUI"))
            {
                _ = theme.Add("cupertino");
            }

            if (ContainsUnoFeature(document, "material") || HasPackageReference(document, "Uno.Material.WinUI"))
            {
                _ = theme.Add("material");
            }

            if (ContainsUnoFeature(document, "simpletheme"))
            {
                _ = theme.Add("simple");
            }

            if (theme.Count == 0)
            {
                _ = theme.Add("fluent");
            }

            reports.Add(new UnoGateReport(
                ToRelativePath(projectFile),
                [.. presentation.Order(StringComparer.OrdinalIgnoreCase)],
                [.. markup.Order(StringComparer.OrdinalIgnoreCase)],
                [.. theme.Order(StringComparer.OrdinalIgnoreCase)]));
        }

        return reports;
    }

    static bool IsUnoProject(XDocument? document, string content)
    {
        if (document is null)
        {
            return content.Contains("Uno.Sdk", StringComparison.OrdinalIgnoreCase);
        }

        string? projectSdk = document.Root?.Attribute("Sdk")?.Value;
        return ContainsSdk(projectSdk, "Uno.Sdk")
            || document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attribute("Name")?.Value)
                .OfType<string>()
                .Any(sdk => sdk.Equals("Uno.Sdk", StringComparison.OrdinalIgnoreCase));
    }

    static void AddMultiValueWarnings(IReadOnlyList<UnoGateReport> reports, List<string> warnings)
    {
        AddMultiValueWarning("presentation", reports.SelectMany(report => report.Presentation), reports, warnings);
        AddMarkupMultiValueWarning(reports, warnings);
        AddMultiValueWarning("theme", reports.SelectMany(report => report.Theme), reports, warnings);
    }

    static void AddMarkupMultiValueWarning(IReadOnlyList<UnoGateReport> reports, List<string> warnings)
    {
        string[] conflictingValues = [.. reports
            .SelectMany(report => report.Markup)
            .Where(value => !value.Equals("xaml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (conflictingValues.Length <= 1)
        {
            return;
        }

        string projectValues = string.Join(
            Environment.NewLine,
            reports
                .Select(report => new
                {
                    report.ProjectPath,
                    Values = report.Markup
                        .Where(value => !value.Equals("xaml", StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                })
                .Where(report => report.Values.Length > 0)
                .SelectMany(report => report.Values.Select(value => $"  {report.ProjectPath}: {value}")));
        warnings.Add($"{Environment.NewLine}Warning: multiple Uno markup gate values detected ({string.Join(", ", conflictingValues)}) - agents may become confused:{Environment.NewLine}{projectValues}");
    }

    static void AddMultiValueWarning(string gate, IEnumerable<string> values, IReadOnlyList<UnoGateReport> reports, List<string> warnings)
    {
        string[] distinctValues = [.. values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        if (distinctValues.Length <= 1)
        {
            return;
        }

        string projectValues = string.Join(
            Environment.NewLine,
            reports.SelectMany(report => report.GetValues(gate).Select(value => $"  {report.ProjectPath}: {value}")));
        warnings.Add($"Multiple Uno {gate} gate values detected ({string.Join(", ", distinctValues)}). Agents may become confused.{Environment.NewLine}{projectValues}");
    }

    static bool HasOrleansReference(string projectFile)
    {
        var document = TryParseProject(projectFile, File.ReadAllText(projectFile), []);
        return PackageReferences(document).Any(package => package.StartsWith("Microsoft.Orleans.", StringComparison.OrdinalIgnoreCase));
    }

    static bool IsAspNetCoreProject(string projectFile)
    {
        var document = TryParseProject(projectFile, File.ReadAllText(projectFile), []);
        if (document is null || IsTestProject(projectFile, document))
        {
            return false;
        }

        if (UsesWebSdk(document))
        {
            return true;
        }

        return FrameworkReferences(document).Any(reference => reference.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase))
            && HasAspNetCoreCodeSignal(Path.GetDirectoryName(projectFile) ?? ".");
    }

    static bool IsTestProject(string projectFile, XDocument document)
    {
        string fileName = Path.GetFileNameWithoutExtension(projectFile);
        string[] pathParts = projectFile.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fileName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || pathParts.Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase)
                || part.Equals("tests", StringComparison.OrdinalIgnoreCase))
            || PackageReferences(document).Any(IsTestPackageReference);
    }

    static bool IsTestPackageReference(string packageId)
    {
        string[] testPackages =
        [
            "Microsoft.NET.Test.Sdk",
            "MSTest",
            "MSTest.TestAdapter",
            "MSTest.TestFramework",
            "NUnit",
            "NUnit3TestAdapter",
            "TUnit",
            "xunit",
            "xunit.runner.visualstudio"
        ];
        return testPackages.Any(package => packageId.Equals(package, StringComparison.OrdinalIgnoreCase)
            || packageId.StartsWith($"{package}.", StringComparison.OrdinalIgnoreCase));
    }

    static bool UsesWebSdk(XDocument document)
    {
        string? projectSdk = document.Root?.Attribute("Sdk")?.Value;
        return ContainsSdk(projectSdk, "Microsoft.NET.Sdk.Web")
            || document.Descendants()
                .Where(element => element.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attribute("Name")?.Value)
                .OfType<string>()
                .Any(sdk => sdk.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase));
    }

    static bool ContainsSdk(string? value, string sdk)
        => value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.Equals(sdk, StringComparison.OrdinalIgnoreCase)
                || part.StartsWith($"{sdk}/", StringComparison.OrdinalIgnoreCase)) == true;

    static bool HasAspNetCoreCodeSignal(string projectDirectory)
    {
        string[] signals =
        [
            "WebApplication.CreateBuilder",
            ".MapGet(",
            ".MapPost(",
            ".MapPut(",
            ".MapDelete(",
            ".MapGroup(",
            ".MapControllers(",
            ".MapControllerRoute(",
            ".MapRazorPages(",
            ".UseRouting(",
            ".UseEndpoints(",
            ".AddControllers(",
            ".AddControllersWithViews(",
            "ControllerBase"
        ];

        return EnumerateFiles(projectDirectory, "*.cs")
            .Any(file =>
            {
                string content = File.ReadAllText(file);
                return signals.Any(signal => content.Contains(signal, StringComparison.Ordinal));
            });
    }

    static bool ContainsUnoFeature(XDocument? document, string value)
        => document?.Descendants()
            .Where(element => element.Name.LocalName.Equals("UnoFeatures", StringComparison.OrdinalIgnoreCase))
            .Any(element => element.Value.Contains(value, StringComparison.OrdinalIgnoreCase)) == true;

    static bool HasPackageReference(XDocument? document, string packageId)
        => PackageReferences(document).Any(package => package.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<string> PackageReferences(XDocument? document)
        => document?.Descendants()
            .Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>() ?? [];

    static IEnumerable<string> FrameworkReferences(XDocument? document)
        => document?.Descendants()
            .Where(element => element.Name.LocalName.Equals("FrameworkReference", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value)
            .OfType<string>() ?? [];

    static XDocument? TryParseProject(string projectFile, string content, List<string> warnings)
    {
        try
        {
            return XDocument.Parse(content);
        }
        catch (System.Xml.XmlException exception)
        {
            warnings.Add($"Could not parse {projectFile}: {exception.Message}");
            return null;
        }
    }

    static List<string> EnumerateFiles(string root, string pattern)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        List<string> files = [];
        Stack<string> directories = new([root]);
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            foreach (string file in Directory.EnumerateFiles(directory, pattern))
            {
                files.Add(file);
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(childDirectory);
                if (!ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    directories.Push(childDirectory);
                }
            }
        }

        return files;
    }

    static bool FileContains(string path, string value)
        => File.ReadAllText(path).Contains(value, StringComparison.OrdinalIgnoreCase);

    static string ToRelativePath(string path)
        => Path.GetRelativePath(Environment.CurrentDirectory, path);
}

sealed record StackDetectionResult(
    IReadOnlySet<string> Technologies,
    IReadOnlyList<UnoGateReport> UnoGates,
    IReadOnlyList<string> Warnings);

sealed record UnoGateReport(
    string ProjectPath,
    IReadOnlyList<string> Presentation,
    IReadOnlyList<string> Markup,
    IReadOnlyList<string> Theme)
{
    public IReadOnlyList<string> GetValues(string gate)
        => gate switch
        {
            "presentation" => Presentation,
            "markup" => Markup,
            "theme" => Theme,
            _ => []
        };
}

static class TechnologyNames
{
    public const string Foundation = "foundation";
    public const string Dotnet = "dotnet";
    public const string AspNetCore = "aspnetcore";
    public const string Uno = "uno";
    public const string Orleans = "orleans";
}
