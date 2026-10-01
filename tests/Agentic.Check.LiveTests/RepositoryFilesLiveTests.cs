using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

public sealed class RepositoryFilesLiveTests
{
    [Fact]
    public async Task GitIgnoredFoldersAreNotScanned()
    {
        string root = Directory.CreateTempSubdirectory("agentic-gitignore-").FullName;
        try
        {
            RealProcess git = new(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = root, ["GIT_CONFIG_GLOBAL"] = Path.Combine(root, "gitconfig"), ["GIT_CONFIG_NOSYSTEM"] = "1"
            });
            string target = Directory.CreateDirectory(Path.Combine(root, "repo")).FullName;
            _ = await git.SuccessAsync("git", ["init", "--initial-branch=main"], target).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(target, ".gitignore"), "out/\n").ConfigureAwait(true);
            _ = Directory.CreateDirectory(Path.Combine(target, "App"));
            await File.WriteAllTextAsync(Path.Combine(target, "App", "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />").ConfigureAwait(true);
            _ = Directory.CreateDirectory(Path.Combine(target, "out", "Stale"));
            await File.WriteAllTextAsync(Path.Combine(target, "out", "Stale", "Stale.csproj"), "<Project Sdk=\"Uno.Sdk\" />").ConfigureAwait(true);

            var files = await RepositoryFiles.ListAsync(new ProcessCommandRunner(), target, CancellationToken.None).ConfigureAwait(true);

            Assert.NotNull(files);
            Assert.Equal([Path.Combine(target, "App", "App.csproj")], files.Where(file => file.EndsWith(".csproj", StringComparison.Ordinal)));
            var detected = StackDetector.Detect(target, files);
            Assert.Contains(TechnologyNames.Dotnet, detected.Technologies);
            Assert.DoesNotContain(TechnologyNames.Uno, detected.Technologies);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
