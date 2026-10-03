using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Agentic.PackageFixtures;

sealed record CandidateBuild(string Branch, string Commit, string Configuration, DateTimeOffset PackedAtUtc, PackageArtifact Check, PackageArtifact Companion, PackageArtifact Dna)
{
    // Ids of the packages whose project version is already on nuget.org and whose sources are unchanged
    // since that publication. Their candidate is the published package itself: nothing to upload.
    public IReadOnlyList<string> Published { get; init; } = [];
}

static partial class CandidateInputs
{
    internal static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Explicit verification requires {name}.");

    internal static async Task<(string Branch, string Commit)> VerifySourceAsync(string checkout)
    {
        RealProcess process = new();
        string origin = await process.SuccessAsync("git", ["remote", "get-url", "origin"], checkout).ConfigureAwait(false);
        FixtureFiles.Require(OriginRegex().IsMatch(origin), $"Expected origin {SourceOracle.OwnRepository}; the configured origin differs.");
        string branch = await process.SuccessAsync("git", ["symbolic-ref", "--quiet", "--short", "HEAD"], checkout).ConfigureAwait(false);
        string sha = await process.SuccessAsync("git", ["rev-parse", "HEAD"], checkout).ConfigureAwait(false);
        string remote = await process.SuccessAsync("git", ["ls-remote", "--exit-code", "origin", "refs/heads/" + branch], checkout).ConfigureAwait(false);
        FixtureFiles.Require(remote.Split('\t')[0] == sha, $"SOURCE NOT READY: origin/{branch} must point to committed candidate {sha}.");
        var branchResult = await GitHubFixtureRun.RunGhAsync(["api", "--hostname", "github.com", $"repos/{SourceOracle.OwnRepository}", "--jq", ".default_branch"], checkout).ConfigureAwait(false);
        branchResult.RequireSuccess("Candidate default branch");
        string defaultBranch = branchResult.Output.Trim();
        FixtureFiles.Require(branch != defaultBranch, "Candidate source must be on a non-default development branch.");
        string status = await process.SuccessAsync("git", ["status", "--porcelain", "--untracked-files=all", "--", "src", "skills", "plugins", "directives", ".agents/skills", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "nuget.config", ".editorconfig"], checkout).ConfigureAwait(false);
        FixtureFiles.Require(status.Length == 0, "Candidate build/content inputs must be committed and clean:\n" + status);
        return (branch, sha);
    }

    internal static async Task<CandidateBuild> LoadAsync()
    {
        string checkPath = Required("AGENTIC_E2E_CHECK_PACKAGE");
        string companionPath = Required("AGENTIC_E2E_COMPANION_PACKAGE");
        string dnaPath = Required("AGENTIC_E2E_DNA_PACKAGE");
        string manifestPath = Environment.GetEnvironmentVariable("AGENTIC_E2E_BUILD_MANIFEST") ?? Path.Combine(Path.GetDirectoryName(checkPath)!, "candidate-build.json");
        var build = FixtureFiles.ReadJson<CandidateBuild>(manifestPath);
        var check = PackageArtifact.Read(checkPath, "Agentic.Check");
        var companion = PackageArtifact.Read(companionPath, "InnoWvate.Agentic");
        var dna = PackageArtifact.Read(dnaPath, "InnoWvate.Dna");
        FixtureFiles.Require(check.Sha256 == build.Check.Sha256 && companion.Sha256 == build.Companion.Sha256 && dna.Sha256 == build.Dna.Sha256, "Candidate hashes differ from build provenance.");
        FixtureFiles.Require(!build.Published.Contains(check.Id, StringComparer.Ordinal) && check.RepositoryCommit == build.Commit, "Agentic.Check must be packed from the candidate commit.");
        foreach (var package in new[] { companion, dna })
        {
            if (build.Published.Contains(package.Id, StringComparer.Ordinal))
            {
                // An unchanged package is tested as the bytes nuget.org serves, not as a rebuild under the same number.
                var published = await PackageArtifact.TryDownloadPublishedAsync(package.Id, package.Version).ConfigureAwait(false);
                FixtureFiles.Require(published?.Sha256 == package.Sha256, $"{package.Id} {package.Version} is recorded as published but differs from the package on nuget.org.");
            }
            else
            {
                FixtureFiles.Require(package.RepositoryCommit == build.Commit, "Package repository commits differ from build provenance.");
            }
        }
        var (branch, commit) = await VerifySourceAsync(FixtureFiles.Checkout).ConfigureAwait(false);
        FixtureFiles.Require(branch == build.Branch && commit == build.Commit, "Candidate build and pushed source identities differ.");
        FixtureFiles.Require(build.Configuration is "Release" or "Debug", "Unsupported package configuration.");
        return build with { Check = check, Companion = companion, Dna = dna };
    }

    internal static async Task PackAsync(string outputDirectory, string configuration)
    {
        FixtureFiles.Require(configuration is "Release" or "Debug", "Specify Release or Debug.");
        var (branch, sha) = await VerifySourceAsync(FixtureFiles.Checkout).ConfigureAwait(false);
        string output = Path.GetFullPath(outputDirectory);
        FixtureFiles.Require(!Directory.Exists(output), "Use a new output directory; never overwrite verified candidates.");
        _ = Directory.CreateDirectory(output);
        RealProcess process = new();
        List<string> published = [];
        foreach (var (id, project) in Projects)
        {
            string version = ProjectVersion(Path.Combine(FixtureFiles.Checkout, project));
            // A version names published bytes. A number nuget.org already has is not packed again: the
            // candidate is the published package, which is what users have.
            var existing = await PackageArtifact.TryDownloadPublishedAsync(id, version).ConfigureAwait(false);
            if (existing is null)
            {
                _ = await process.SuccessAsync("dotnet", ["pack", project, "-c", configuration, "-o", output, "-p:RepositoryCommit=" + sha], FixtureFiles.Checkout).ConfigureAwait(false);
                continue;
            }

            // Agentic.Check names the release and is published with every one, so it always moves.
            FixtureFiles.Require(id != CheckId, $"{CheckId} {version} is already on nuget.org. Its version names the release: set the next version in {project} before packing.");
            await RequireUnchangedSinceAsync(process, FixtureFiles.Checkout, existing, project).ConfigureAwait(false);
            existing.CopyInto(output);
            published.Add(id);
        }

        var check = PackageArtifact.Read(Directory.GetFiles(output, "Agentic.Check.*.nupkg").Single(), CheckId);
        var companion = PackageArtifact.Read(Directory.GetFiles(output, "InnoWvate.Agentic.*.nupkg").Single(), "InnoWvate.Agentic");
        var dna = PackageArtifact.Read(Directory.GetFiles(output, "InnoWvate.Dna.*.nupkg").Single(), "InnoWvate.Dna");
        // The tool projects map Release source paths to /_/; this guards that setting for anything that could be published.
        if (configuration == "Release")
        {
            foreach (var artifact in new[] { check, companion, dna }.Where(artifact => !published.Contains(artifact.Id, StringComparer.Ordinal)))
                RequireRepositoryRelativeSymbols(artifact.Path, FixtureFiles.Checkout);
        }
        var after = await VerifySourceAsync(FixtureFiles.Checkout).ConfigureAwait(false);
        FixtureFiles.Require(after == (branch, sha), "Source changed while packing.");
        FixtureFiles.WriteJson(Path.Combine(output, "candidate-build.json"), new CandidateBuild(branch, sha, configuration, DateTimeOffset.UtcNow, check, companion, dna) { Published = published });
    }

    const string CheckId = "Agentic.Check";

    static IReadOnlyList<(string Id, string Project)> Projects { get; } =
    [
        (CheckId, "src/Agentic.Check/Agentic.Check.csproj"),
        ("InnoWvate.Agentic", "src/Agentic/Agentic.csproj"),
        ("InnoWvate.Dna", "src/Dna/Dna.csproj")
    ];

    // Files that apply to every project build, wherever they exist.
    static IReadOnlyList<string> BuildFiles { get; } = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "nuget.config"];

    internal static string ProjectVersion(string projectFile)
        => XDocument.Load(projectFile).Descendants().Single(element => element.Name.LocalName == "Version").Value.Trim();

    // Everything that goes into a tool package: its project folder, the files it links from outside
    // that folder, and the build files that apply to every project. Paths are relative to the checkout.
    internal static IReadOnlyList<string> SourcePaths(string checkout, string project)
    {
        string folder = Path.GetDirectoryName(project)!.Replace('\\', '/');
        var linked = XDocument.Load(Path.Combine(checkout, project)).Descendants()
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(include => include is not null && include.StartsWith("../", StringComparison.Ordinal))
            .Select(include => Path.GetRelativePath(checkout, Path.GetFullPath(Path.Combine(checkout, folder, include!))).Replace('\\', '/'));
        return [folder, .. linked.Order(StringComparer.Ordinal), .. BuildFiles];
    }

    // The given paths that differ between a commit and HEAD.
    internal static async Task<IReadOnlyList<string>> ChangedSinceAsync(RealProcess process, string checkout, string commit, IReadOnlyList<string> paths)
    {
        var diff = await process.RunAsync("git", ["diff", "--name-only", commit, "HEAD", "--", .. paths], checkout).ConfigureAwait(false);
        diff.RequireSuccess("git diff");
        return diff.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // A package keeps its published number only while nothing that goes into it changed since the commit
    // it was published from; otherwise one number would name two different packages.
    internal static async Task RequireUnchangedSinceAsync(RealProcess process, string checkout, PackageArtifact published, string project)
    {
        FixtureFiles.Require(published.RepositoryCommit is { Length: > 0 },
            $"{published.Id} {published.Version} on nuget.org records no source commit, so it cannot be shown unchanged. Set the next version in {project}.");
        var known = await process.RunAsync("git", ["cat-file", "-e", published.RepositoryCommit + "^{commit}"], checkout).ConfigureAwait(false);
        FixtureFiles.Require(known.ExitCode == 0,
            $"{published.Id} {published.Version} was published from {published.RepositoryCommit}, which this checkout does not have. Fetch the release tags, then pack again.");
        var changed = await ChangedSinceAsync(process, checkout, published.RepositoryCommit!, SourcePaths(checkout, project)).ConfigureAwait(false);
        FixtureFiles.Require(changed.Count == 0,
            $"{published.Id} changed since {published.Version} was published, but {project} still carries that version. Set its next version, or undo the change:\n{string.Join('\n', changed)}");
    }

    internal static void RequireRepositoryRelativeSymbols(string packagePath, string checkout)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var symbols = archive.Entries.Where(entry => entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)).ToList();
        FixtureFiles.Require(symbols.Count > 0, $"No symbols found in {packagePath}.");
        foreach (var entry in symbols)
        {
            using var stream = entry.Open();
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            string text = Encoding.UTF8.GetString(buffer.ToArray());
            FixtureFiles.Require(!text.Contains(checkout, StringComparison.Ordinal), $"{entry.FullName} in {packagePath} contains the local checkout path; Release builds must keep ContinuousIntegrationBuild enabled.");
            FixtureFiles.Require(text.Contains("/_/", StringComparison.Ordinal), $"{entry.FullName} in {packagePath} has no repository-relative source paths.");
        }
    }

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com(?::22)?/)VincentH-Net/dotnet-agentic-engineering(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginRegex();
}
