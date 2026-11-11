using System.Text.Json;
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
            if (document is null)
            {
                continue;
            }

            string projectDirectory = Path.GetDirectoryName(projectFile) ?? ".";
            Dictionary<string, IReadOnlyList<string>> values = new(StringComparer.OrdinalIgnoreCase);
            if (IsInteractiveTerminalProject(document, universe, projectDirectory))
            {
                values["terminal"] = ["interactive"];
            }

            // A Directory.Build file that does not parse is reported once, by the scan that owns its technology.
            XDocument?[] documents = [document, .. AncestorBuildFiles(projectFile, repoRoot, [])];
            if (IsPublishedLibraryProject(document, documents, universe, projectDirectory))
            {
                values["library"] = ["published"];
            }

            string[] frameworks = [.. TargetFrameworkGates(documents)];
            if (frameworks.Length > 0)
            {
                values["framework"] = frameworks;
            }

            if (values.Count > 0)
            {
                reports.Add(new InstallGateReport(TechnologyNames.Dotnet, ToRelativePath(repoRoot, projectFile), values));
            }
        }

        return reports;
    }

    // The library gate serves dotnet-breaking-changes, which is for code whose callers live outside the
    // repository. A class library alone is no evidence of that: most are used only by the projects next
    // to them, where the build and the tests catch a break. A project counts when it tracks its public
    // API, or when it is explicitly packaged and is not a tool, whose users call commands, not an API.
    static readonly string[] PublicApiPackages = ["Microsoft.CodeAnalysis.PublicApiAnalyzers", "Microsoft.DotNet.ApiCompat", "Microsoft.DotNet.ApiCompat.Task"];

    static readonly string[] PublicApiFiles = ["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"];

    static bool IsPublishedLibraryProject(XDocument document, XDocument?[] documents, FileUniverse universe, string projectDirectory)
    {
        if (documents.Any(candidate => candidate is not null && HasProperty(candidate, "EnablePackageValidation", "true"))
            || PublicApiPackages.Any(package => HasPackageReference(documents, package))
            || universe.Enumerate(projectDirectory, "*.txt").Any(file => PublicApiFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        // The project's own setting wins over a Directory.Build file, as it does in MSBuild.
        if (HasTrueProperty(document, "PackAsTool") || HasProperty(document, "IsPackable", "false"))
        {
            return false;
        }

        return documents.Any(candidate => candidate is not null
            && (HasProperty(candidate, "IsPackable", "true")
                || HasProperty(candidate, "GeneratePackageOnBuild", "true")
                || candidate.Descendants().Any(element => element.Name.LocalName.Equals("PackageId", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(element.Value))));
    }

    static List<InstallGateReport> DetectUnoGates(IReadOnlyList<string> projectFiles, string repoRoot, List<string> warnings)
    {
        List<InstallGateReport> reports = [];
        string globalJsonRoot = GlobalJsonRoot(repoRoot);
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

            string? sdk = UnoSdkVersion(document, projectFile, globalJsonRoot);
            if (sdk is null)
            {
                warnings.Add($"Could not find the Uno.Sdk version of {ToRelativePath(repoRoot, projectFile)}: it is not on the project's Sdk and no global.json above it names Uno.Sdk. Skills for the latest Uno.Sdk are offered.");
            }

            reports.Add(new UnoGateReport(
                ToRelativePath(repoRoot, projectFile),
                [.. presentation.Order(StringComparer.OrdinalIgnoreCase)],
                [.. markup.Order(StringComparer.OrdinalIgnoreCase)],
                [.. theme.Order(StringComparer.OrdinalIgnoreCase)],
                sdk));
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

    const string UnoSdkName = "Uno.Sdk";

    // The Uno.Sdk version a project builds with: the one on its Sdk reference, else the one in the nearest
    // global.json that names Uno.Sdk. Null when neither holds a literal version.
    static string? UnoSdkVersion(XDocument? document, string projectFile, string globalJsonRoot)
    {
        string? version = document?.Root?.Attribute("Sdk")?.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.StartsWith(UnoSdkName + "/", StringComparison.OrdinalIgnoreCase))
            .Select(part => part[(UnoSdkName.Length + 1)..])
            .FirstOrDefault()
            ?? document?.Descendants()
                .Where(element => element.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(element.Attribute("Name")?.Value, UnoSdkName, StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Attribute("Version")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (IsLiteralVersion(version))
        {
            return version!.Trim();
        }

        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectFile))!); directory is not null; directory = directory.Parent)
        {
            if (GlobalJsonUnoSdkVersion(Path.Combine(directory.FullName, "global.json")) is { } pinned)
            {
                return pinned;
            }

            if (string.Equals(Path.TrimEndingDirectorySeparator(directory.FullName), globalJsonRoot, StringComparison.Ordinal))
            {
                break;
            }
        }

        return null;
    }

    static string? GlobalJsonUnoSdkVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("msbuild-sdks", out var sdks) || sdks.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var sdk in sdks.EnumerateObject())
            {
                if (sdk.Name.Equals(UnoSdkName, StringComparison.OrdinalIgnoreCase) && sdk.Value.ValueKind == JsonValueKind.String && IsLiteralVersion(sdk.Value.GetString()))
                {
                    return sdk.Value.GetString()!.Trim();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool IsLiteralVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = ToolVersion.Parse(value.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // MSBuild reads global.json from the folders above a project, which can lie above the target folder:
    // the search stops at the repository root when the target is inside one, else at the target.
    static string GlobalJsonRoot(string repoRoot)
    {
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        for (var directory = new DirectoryInfo(target); directory is not null; directory = directory.Parent)
        {
            string git = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return Path.TrimEndingDirectorySeparator(directory.FullName);
            }
        }

        return target;
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

    // The framework gate serves the skills that depend on the .NET version a project targets: "net10" for a
    // project that still targets net10.0, which has the migration to .NET 11 ahead of it, and "net11" for one
    // that targets net11.0 or later, where the .NET 11 APIs exist. The platform suffix does not matter. A
    // project's own target frameworks win over a Directory.Build file's, as they do in MSBuild; one set
    // through a property is not followed.
    static IEnumerable<string> TargetFrameworkGates(XDocument?[] documents)
    {
        int[] majors = [.. (documents
            .Select(candidate => candidate?.Descendants()
                .Where(element => element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase)
                    || element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray() ?? [])
            .FirstOrDefault(monikers => monikers.Length > 0) ?? [])
            .Select(NetMajor)
            .OfType<int>()];
        if (majors.Contains(10))
        {
            yield return "net10";
        }

        if (majors.Any(major => major >= 11))
        {
            yield return "net11";
        }
    }

    // 11 for net11.0, net11.0-windows10.0.19041; null for net48, netstandard2.0 or netcoreapp3.1.
    static int? NetMajor(string moniker)
    {
        if (!moniker.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string version = moniker[3..].Split('-')[0];
        int dot = version.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && int.TryParse(version[..dot], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int major)
            ? major
            : null;
    }

    static bool HasTrueProperty(XDocument document, string name)
        => HasProperty(document, name, "true");

    static bool HasProperty(XDocument document, string name, string value)
        => document.Descendants()
            .Where(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Any(element => element.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));

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

// Sdk is the project's Uno.Sdk version, null when the check could not find it. It is not one of the gate
// values: a skill names the Uno.Sdk versions it is written for as a range, next to its gates.
sealed record UnoGateReport(
    string ProjectPath,
    IReadOnlyList<string> Presentation,
    IReadOnlyList<string> Markup,
    IReadOnlyList<string> Theme,
    string? Sdk = null) : InstallGateReport(
        TechnologyNames.Uno,
        ProjectPath,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["presentation"] = Presentation,
            ["markup"] = Markup,
            ["theme"] = Theme
        })
{
    public ToolVersion? SdkVersion => Sdk is null ? null : ToolVersion.Parse(Sdk);
}

static class TechnologyNames
{
    public const string Foundation = "foundation";
    public const string Dotnet = "dotnet";
    public const string AspNetCore = "aspnetcore";
    public const string Uno = "uno";
    public const string Orleans = "orleans";
}
