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
