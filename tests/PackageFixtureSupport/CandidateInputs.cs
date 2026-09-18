using System.Text.RegularExpressions;

namespace Agentic.PackageFixtures;

sealed record CandidateBuild(string Branch, string Commit, string Configuration, DateTimeOffset PackedAtUtc, PackageArtifact Check, PackageArtifact Companion, PackageArtifact Dna);

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
        FixtureFiles.Require(check.RepositoryCommit == build.Commit && companion.RepositoryCommit == build.Commit && dna.RepositoryCommit == build.Commit, "Package repository commits differ from build provenance.");
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
        foreach (string project in new[] { "src/Agentic.Check/Agentic.Check.csproj", "src/Agentic/Agentic.csproj", "src/Dna/Dna.csproj" })
            _ = await process.SuccessAsync("dotnet", ["pack", project, "-c", configuration, "-o", output, "-p:RepositoryCommit=" + sha], FixtureFiles.Checkout).ConfigureAwait(false);
        var check = PackageArtifact.Read(Directory.GetFiles(output, "Agentic.Check.*.nupkg").Single(), "Agentic.Check");
        var companion = PackageArtifact.Read(Directory.GetFiles(output, "InnoWvate.Agentic.*.nupkg").Single(), "InnoWvate.Agentic");
        var dna = PackageArtifact.Read(Directory.GetFiles(output, "InnoWvate.Dna.*.nupkg").Single(), "InnoWvate.Dna");
        var after = await VerifySourceAsync(FixtureFiles.Checkout).ConfigureAwait(false);
        FixtureFiles.Require(after == (branch, sha), "Source changed while packing.");
        FixtureFiles.WriteJson(Path.Combine(output, "candidate-build.json"), new CandidateBuild(branch, sha, configuration, DateTimeOffset.UtcNow, check, companion, dna));
    }

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com(?::22)?/)VincentH-Net/dotnet-agentic-engineering(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginRegex();
}
