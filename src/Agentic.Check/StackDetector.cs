using System.Xml.Linq;

namespace Agentic.Check;

static class StackDetector
{
    internal static readonly string[] ExcludedDirectoryNames = [".git", ".vs", "bin", "obj", "node_modules", "TestResults"];

    internal static StackDetectionResult Detect(string repoRoot) => Detect(repoRoot, null);

    // files: the repository's own file list (see RepositoryFiles); null walks the folder instead.
    internal static StackDetectionResult Detect(string repoRoot, IReadOnlyCollection<string>? files)
    {
        FileUniverse universe = new(files);
        List<string> warnings = [];
        List<InstallGateReport> installGateReports = [];
        HashSet<string> technologies = new(StringComparer.OrdinalIgnoreCase)
        {
            TechnologyNames.Foundation
        };

        var projectFiles = universe.Enumerate(repoRoot, "*.csproj");
        IReadOnlyList<string> propsTargetsFiles = [.. universe.Enumerate(repoRoot, "*.props"), .. universe.Enumerate(repoRoot, "*.targets")];

        if (projectFiles.Count > 0)
        {
            _ = technologies.Add(TechnologyNames.Dotnet);
            installGateReports.AddRange(DetectDotnetGates(projectFiles, repoRoot, universe, warnings));
        }

        bool unoDetected = projectFiles.Concat(propsTargetsFiles).Any(file => FileContains(file, "Uno.Sdk"));
        if (unoDetected)
        {
            _ = technologies.Add(TechnologyNames.Uno);
            installGateReports.AddRange(DetectUnoGates(projectFiles, repoRoot, warnings));
        }

        if (projectFiles.Any(HasOrleansReference))
        {
            _ = technologies.Add(TechnologyNames.Orleans);
        }

        if (projectFiles.Any(projectFile => IsAspNetCoreProject(projectFile, universe)))
        {
            _ = technologies.Add(TechnologyNames.AspNetCore);
        }

        AddMultiValueWarnings([.. installGateReports.OfType<UnoGateReport>()], warnings);
        return new StackDetectionResult(technologies, installGateReports, warnings);
    }

    static List<InstallGateReport> DetectDotnetGates(IReadOnlyList<string> projectFiles, string repoRoot, FileUniverse universe, List<string> warnings)
    {
        List<InstallGateReport> reports = [];
        foreach (string projectFile in projectFiles)
        {
            var document = TryParseProject(projectFile, File.ReadAllText(projectFile), warnings);
            if (document is null || !IsInteractiveTerminalProject(document, universe, Path.GetDirectoryName(projectFile) ?? "."))
            {
                continue;
            }

            reports.Add(new InstallGateReport(
                TechnologyNames.Dotnet,
                ToRelativePath(repoRoot, projectFile),
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["terminal"] = ["interactive"]
                }));
        }

        return reports;
    }

    static List<InstallGateReport> DetectUnoGates(IReadOnlyList<string> projectFiles, string repoRoot, List<string> warnings)
    {
        List<InstallGateReport> reports = [];
        foreach (string projectFile in projectFiles)
        {
            string content = File.ReadAllText(projectFile);
            var document = TryParseProject(projectFile, content, warnings);
            if (!IsUnoProject(document, content))
            {
                continue;
            }

            // Features and packages can also be declared in Directory.Build files above the project.
            XDocument?[] documents = [document, .. AncestorBuildFiles(projectFile, repoRoot, warnings)];

            HashSet<string> presentation = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> markup = new(StringComparer.OrdinalIgnoreCase)
            {
                "xaml"
            };
            HashSet<string> theme = new(StringComparer.OrdinalIgnoreCase);

            if (ContainsUnoFeature(documents,"mvux") || HasPackageReference(documents,"Uno.Extensions.Reactive.WinUI"))
            {
                _ = presentation.Add("mvux");
            }

            if (ContainsUnoFeature(documents,"mvvm") || HasPackageReference(documents,"CommunityToolkit.Mvvm"))
            {
                _ = presentation.Add("mvvm");
            }

            if (ContainsUnoFeature(documents,"csharpmarkup") || HasPackageReference(documents,"Uno.WinUI.Markup"))
            {
                _ = markup.Add("csharp");
            }

            if (HasPackageReference(documents,"CSharpMarkup.WinUI"))
            {
                _ = markup.Add("csharp2");
            }

            if (ContainsUnoFeature(documents,"cupertino") || HasPackageReference(documents,"Uno.Cupertino.WinUI"))
            {
                _ = theme.Add("cupertino");
            }

            if (ContainsUnoFeature(documents,"material") || HasPackageReference(documents,"Uno.Material.WinUI"))
            {
                _ = theme.Add("material");
            }

            if (ContainsUnoFeature(documents,"simpletheme"))
            {
                _ = theme.Add("simple");
            }

            if (theme.Count == 0)
            {
                _ = theme.Add("fluent");
            }

            reports.Add(new UnoGateReport(
                ToRelativePath(repoRoot, projectFile),
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

    static bool IsAspNetCoreProject(string projectFile, FileUniverse universe)
    {
        var document = TryParseProject(projectFile, File.ReadAllText(projectFile), []);
        if (document is null)
        {
            return false;
        }

        if (UsesWebSdk(document))
        {
            return true;
        }

        return FrameworkReferences(document).Any(reference => reference.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase))
            && HasAspNetCoreCodeSignal(universe, Path.GetDirectoryName(projectFile) ?? ".");
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

    // The terminal gate serves cli-e2e-testing, so it needs positive evidence of terminal interaction.
    // An Exe alone is not enough: Uno, MAUI and Microsoft.Testing.Platform projects are Exe too.
    static readonly string[] TerminalPackages =
    [
        "System.CommandLine",
        "Spectre.Console",
        "Spectre.Console.Cli",
        "Terminal.Gui",
        "McMaster.Extensions.CommandLineUtils",
        "CommandLineParser",
        "ConsoleAppFramework",
        "Cocona",
        "CliFx",
        "Hex1b"
    ];

    static readonly string[] ConsoleInputSignals = ["Console.ReadKey(", "Console.ReadLine(", "Console.In.", "Console.KeyAvailable"];

    static bool IsInteractiveTerminalProject(XDocument document, FileUniverse universe, string projectDirectory)
        => HasTrueProperty(document, "PackAsTool")
            || document.Descendants().Any(element => element.Name.LocalName.Equals("ToolCommandName", StringComparison.OrdinalIgnoreCase))
            || PackageReferences(document).Any(package => TerminalPackages.Contains(package, StringComparer.OrdinalIgnoreCase))
            || universe.Enumerate(projectDirectory, "*.cs").Any(file => ConsoleInputSignals.Any(signal => FileContains(file, signal)));

    static bool HasTrueProperty(XDocument document, string name)
        => document.Descendants()
            .Where(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Any(element => element.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

    static IEnumerable<XDocument?> AncestorBuildFiles(string projectFile, string repoRoot, List<string> warnings)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectFile))!); directory is not null; directory = directory.Parent)
        {
            foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                string path = Path.Combine(directory.FullName, name);
                if (File.Exists(path))
                {
                    yield return TryParseProject(path, File.ReadAllText(path), warnings);
                }
            }

            if (string.Equals(Path.TrimEndingDirectorySeparator(directory.FullName), root, StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }

    static bool ContainsSdk(string? value, string sdk)
        => value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.Equals(sdk, StringComparison.OrdinalIgnoreCase)
                || part.StartsWith($"{sdk}/", StringComparison.OrdinalIgnoreCase)) == true;

    static bool HasAspNetCoreCodeSignal(FileUniverse universe, string projectDirectory)
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

        return universe.Enumerate(projectDirectory, "*.cs")
            .Any(file =>
            {
                string content = File.ReadAllText(file);
                return signals.Any(signal => content.Contains(signal, StringComparison.Ordinal));
            });
    }

    static bool ContainsUnoFeature(IEnumerable<XDocument?> documents, string value)
        => documents.Any(document => document?.Descendants()
            .Where(element => element.Name.LocalName.Equals("UnoFeatures", StringComparison.OrdinalIgnoreCase))
            .Any(element => element.Value.Contains(value, StringComparison.OrdinalIgnoreCase)) == true);

    static bool HasPackageReference(IEnumerable<XDocument?> documents, string packageId)
        => documents.Any(document => PackageReferences(document).Any(package => package.Equals(packageId, StringComparison.OrdinalIgnoreCase)));

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

    // The files a scan may see: the repository's own list when there is one, else a walk of the folder.
    sealed class FileUniverse(IReadOnlyCollection<string>? files)
    {
        internal List<string> Enumerate(string directory, string pattern)
        {
            if (files is null)
            {
                return EnumerateFiles(directory, pattern);
            }

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            string extension = pattern.TrimStart('*');
            return [.. files.Where(file => file.StartsWith(root, StringComparison.Ordinal) && file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))];
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

    // Reported like every other path: relative to the target, not to where the command was started.
    static string ToRelativePath(string repoRoot, string path)
        => Path.GetRelativePath(repoRoot, path);
}

sealed record StackDetectionResult(
    IReadOnlySet<string> Technologies,
    IReadOnlyList<InstallGateReport> InstallGates,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<UnoGateReport> UnoGates { get; } = [.. InstallGates.OfType<UnoGateReport>()];
}

record InstallGateReport(
    string Technology,
    string ProjectPath,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values)
{
    public IReadOnlyList<string> GetValues(string gate)
        => Values.TryGetValue(gate, out var values) ? values : [];
}

sealed record UnoGateReport(
    string ProjectPath,
    IReadOnlyList<string> Presentation,
    IReadOnlyList<string> Markup,
    IReadOnlyList<string> Theme) : InstallGateReport(
        TechnologyNames.Uno,
        ProjectPath,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["presentation"] = Presentation,
            ["markup"] = Markup,
            ["theme"] = Theme
        });

static class TechnologyNames
{
    public const string Foundation = "foundation";
    public const string Dotnet = "dotnet";
    public const string AspNetCore = "aspnetcore";
    public const string Uno = "uno";
    public const string Orleans = "orleans";
}
