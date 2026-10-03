using System.IO.Compression;
using System.Text;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class PackageArtifactTests
{
    [Fact]
    public void ValidationRejectsWrongIdentityAndChangedBytes()
    {
        using FixtureWorkspace workspace = new();
        string path = Path.Combine(workspace.Root, "validation-only.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (StreamWriter writer = new(archive.CreateEntry("fixture.nuspec").Open(), Encoding.UTF8))
                writer.Write("""<package><metadata><id>Validation.Only</id><version>1.0.0</version></metadata></package>""");
            using StreamWriter settings = new(archive.CreateEntry("tools/net10.0/any/DotnetToolSettings.xml").Open(), Encoding.UTF8);
            settings.Write("<DotNetCliTool />");
        }
        // This deliberately minimal archive tests rejection paths, never package installation or historical upgrades.
        var package = PackageArtifact.Read(path, "Validation.Only", "1.0.0");
        _ = Assert.Throws<InvalidDataException>(() => PackageArtifact.Read(path, "Wrong.Identity"));
        _ = Assert.Throws<InvalidDataException>(() => PackageArtifact.Read(path, "Validation.Only", "2.0.0"));
        File.AppendAllText(path, "changed");
        _ = Assert.Throws<InvalidDataException>(package.Verify);
    }

    [Theory]
    [InlineData("/_/src/Agentic/Program.cs", null)]
    [InlineData("<checkout>/src/Agentic/Program.cs", "contains the local checkout path")]
    [InlineData("C:/elsewhere/Program.cs", "no repository-relative source paths")]
    public void CandidateSymbolsMustUseRepositoryRelativePaths(string documentPath, string? failure)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        using FixtureWorkspace workspace = new();
        string checkout = Path.Combine(workspace.Root, "checkout");
        string path = Path.Combine(workspace.Root, "symbols-only.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using StreamWriter symbols = new(archive.CreateEntry("tools/net10.0/any/Agentic.pdb").Open(), Encoding.UTF8);
            symbols.Write("fixture portable pdb bytes " + documentPath.Replace("<checkout>", checkout, StringComparison.Ordinal));
        }
        if (failure is null)
        {
            CandidateInputs.RequireRepositoryRelativeSymbols(path, checkout);
        }
        else
        {
            var exception = Assert.Throws<InvalidDataException>(() => CandidateInputs.RequireRepositoryRelativeSymbols(path, checkout));
            Assert.Contains(failure, exception.Message, StringComparison.Ordinal);
        }
        string empty = Path.Combine(workspace.Root, "no-symbols.nupkg");
        using (var archive = ZipFile.Open(empty, ZipArchiveMode.Create))
            _ = archive.CreateEntry("fixture.nuspec");
        _ = Assert.Throws<InvalidDataException>(() => CandidateInputs.RequireRepositoryRelativeSymbols(empty, checkout));
    }

    [Fact]
    public void FeedTakesTheSameBytesTwiceButNotOtherBytesUnderTheSameName()
    {
        using FixtureWorkspace workspace = new();
        string feed = Path.Combine(workspace.Root, "feed-under-test");
        string publishedPath = Path.Combine(workspace.Root, "published", "Sample.Tool.1.0.0.nupkg");
        string rebuiltPath = Path.Combine(workspace.Root, "rebuilt", "Sample.Tool.1.0.0.nupkg");
        foreach (string folder in new[] { feed, Path.GetDirectoryName(publishedPath)!, Path.GetDirectoryName(rebuiltPath)! })
            _ = Directory.CreateDirectory(folder);
        File.WriteAllText(publishedPath, "published bytes");
        File.WriteAllText(rebuiltPath, "rebuilt bytes");
        PackageArtifact published = new(publishedPath, "Sample.Tool", "1.0.0", FixtureFiles.Hash(publishedPath), "published-commit");
        PackageArtifact rebuilt = new(rebuiltPath, "Sample.Tool", "1.0.0", FixtureFiles.Hash(rebuiltPath), "later-commit");

        // An unchanged package is its own published package, so a baseline may bring the same file again.
        published.CopyInto(feed);
        published.CopyInto(feed);
        var error = Assert.Throws<InvalidDataException>(() => rebuilt.CopyInto(feed));

        Assert.Contains("A changed package needs a new version", error.Message, StringComparison.Ordinal);
        Assert.Equal("published bytes", File.ReadAllText(Path.Combine(feed, "Sample.Tool.1.0.0.nupkg")));
    }

    [Fact]
    public void PackageSourcesAreItsProjectFolderTheFilesItLinksAndTheBuildFiles()
    {
        var companion = CandidateInputs.SourcePaths(FixtureFiles.Checkout, "src/Agentic/Agentic.csproj");
        Assert.Equal("src/Agentic", companion[0]);
        Assert.Contains("src/Shared/ToolVersion.cs", companion);
        Assert.Contains("src/Shared/ToolLauncher.cs", companion);
        Assert.Contains("Directory.Build.props", companion);

        var shorthand = CandidateInputs.SourcePaths(FixtureFiles.Checkout, "src/Dna/Dna.csproj");
        Assert.Equal("src/Dna", shorthand[0]);
        Assert.Contains("src/Shared/DnaLauncherContract.cs", shorthand);
        // The shorthand does not compile the version type, so a change there is no change to the shorthand.
        Assert.DoesNotContain("src/Shared/ToolVersion.cs", shorthand);
        Assert.DoesNotContain(shorthand, path => path.StartsWith("src/Agentic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APublishedNumberIsKeptOnlyWhileThePackageSourcesAreUnchanged()
    {
        using FixtureWorkspace workspace = new();
        string repository = Path.Combine(workspace.Root, "history");
        RealProcess git = new();
        Write("src/Tool/Tool.csproj", """<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup><ItemGroup><Compile Include="../Shared/Linked.cs" /></ItemGroup></Project>""");
        Write("src/Tool/Program.cs", "published");
        Write("src/Shared/Linked.cs", "published");
        Write("src/Other/Program.cs", "published");
        _ = await git.SuccessAsync("git", ["init", "--initial-branch=fixture"], repository).ConfigureAwait(true);
        string publishedCommit = await CommitAsync("published").ConfigureAwait(true);
        PackageArtifact published = new(Path.Combine(repository, "unused.nupkg"), "Sample.Tool", "1.0.0", "unused", publishedCommit);
        Assert.Equal("1.0.0", CandidateInputs.ProjectVersion(Path.Combine(repository, "src/Tool/Tool.csproj")));

        // A change outside the package's sources leaves its number valid.
        Write("src/Other/Program.cs", "changed");
        _ = await CommitAsync("change elsewhere").ConfigureAwait(true);
        Assert.Equal(["src/Other/Program.cs"], await CandidateInputs.ChangedSinceAsync(git, repository, publishedCommit, ["src/Other"]).ConfigureAwait(true));
        await CandidateInputs.RequireUnchangedSinceAsync(git, repository, published, "src/Tool/Tool.csproj").ConfigureAwait(true);

        // A change to a file the project links from outside its folder is a change to the package.
        Write("src/Shared/Linked.cs", "changed");
        _ = await CommitAsync("change a linked file").ConfigureAwait(true);
        var changed = await Assert.ThrowsAsync<InvalidDataException>(() => CandidateInputs.RequireUnchangedSinceAsync(git, repository, published, "src/Tool/Tool.csproj")).ConfigureAwait(true);
        Assert.Contains("Sample.Tool changed since 1.0.0 was published, but src/Tool/Tool.csproj still carries that version.", changed.Message, StringComparison.Ordinal);
        Assert.Contains("src/Shared/Linked.cs", changed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Other/Program.cs", changed.Message, StringComparison.Ordinal);

        // Without the published commit there is no way to show the package unchanged.
        var unknown = await Assert.ThrowsAsync<InvalidDataException>(() => CandidateInputs.RequireUnchangedSinceAsync(git, repository,
            published with { RepositoryCommit = "0123456789012345678901234567890123456789" }, "src/Tool/Tool.csproj")).ConfigureAwait(true);
        Assert.Contains("which this checkout does not have", unknown.Message, StringComparison.Ordinal);
        var unrecorded = await Assert.ThrowsAsync<InvalidDataException>(() => CandidateInputs.RequireUnchangedSinceAsync(git, repository,
            published with { RepositoryCommit = null }, "src/Tool/Tool.csproj")).ConfigureAwait(true);
        Assert.Contains("records no source commit", unrecorded.Message, StringComparison.Ordinal);

        void Write(string path, string content)
        {
            string file = Path.Combine(repository, path);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        async Task<string> CommitAsync(string message)
        {
            _ = await git.SuccessAsync("git", ["add", "--all"], repository).ConfigureAwait(false);
            _ = await git.SuccessAsync("git", ["-c", "user.name=fixture", "-c", "user.email=fixture@example.test", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", message], repository).ConfigureAwait(false);
            return await git.SuccessAsync("git", ["rev-parse", "HEAD"], repository).ConfigureAwait(false);
        }
    }

    [Fact]
    public void ArchiveCopiesPreserveExecutableAssetsAndRemainIndependent()
    {
        using FixtureWorkspace source = new();
        string asset = Path.Combine(source.Target, ".agents/skills/example/run.sh");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        File.WriteAllText(asset, "#!/bin/sh\nprintf 'fixture data only'\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(asset, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string archive = Path.Combine(source.Root, "snapshot.zip");
        FixtureFiles.CaptureSnapshot(source.Target, archive);
        string hash = FixtureFiles.Hash(archive);
        using FixtureWorkspace first = new();
        using FixtureWorkspace second = new();
        FixtureFiles.ExtractSnapshot(archive, first.Target);
        FixtureFiles.ExtractSnapshot(archive, second.Target);
        Assert.True(FixtureFiles.EqualInventory(FixtureFiles.Inventory(source.Target), FixtureFiles.Inventory(first.Target)));
        string firstAsset = Path.Combine(first.Target, ".agents/skills/example/run.sh");
        if (!OperatingSystem.IsWindows())
            Assert.Equal(File.GetUnixFileMode(asset), File.GetUnixFileMode(firstAsset));
        File.WriteAllText(firstAsset, "changed copy");
        Assert.True(FixtureFiles.EqualInventory(FixtureFiles.Inventory(source.Target), FixtureFiles.Inventory(second.Target)));
        Assert.Equal(hash, FixtureFiles.Hash(archive));
    }
}
