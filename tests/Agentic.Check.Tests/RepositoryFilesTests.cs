namespace Agentic.Check.Tests;

public sealed class RepositoryFilesTests
{
    [Fact]
    public async Task ReturnsNullOutsideARepository()
    {
        using TempDirectory temp = new();
        temp.Write("App.csproj", "<Project />");
        FakeCommandRunner runner = new();

        Assert.Null(await RepositoryFiles.ListAsync(runner, temp.Path, CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ReturnsNullWithoutAGitignore()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        temp.Write("App.csproj", "<Project />");
        FakeCommandRunner runner = new();

        Assert.Null(await RepositoryFiles.ListAsync(runner, temp.Path, CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ListsGitFilesBelowTheTargetAsFullPaths()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        temp.Write(".gitignore", "bin/\n");
        temp.Write("src/App/App.csproj", "<Project />");
        string target = temp.CreateDirectory("src");
        FakeCommandRunner runner = new();
        runner.Enqueue(new CommandResult(0, "App/App.csproj\0App/Deleted.cs\0", string.Empty));

        var files = await RepositoryFiles.ListAsync(runner, target, CancellationToken.None);

        Assert.Equal([Path.Combine(target, "App", "App.csproj")], files);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("git", call.FileName);
        Assert.Equal(["ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "."], call.Arguments);
        Assert.Equal(Path.GetFullPath(target), call.WorkingDirectory);
    }

    [Fact]
    public async Task ReturnsNullWhenGitFails()
    {
        using TempDirectory temp = new();
        temp.Write(".git/HEAD", "ref: refs/heads/main");
        temp.Write(".gitignore", "bin/\n");
        FakeCommandRunner runner = new();
        runner.Enqueue(new CommandResult(127, string.Empty, "git: command not found"));

        Assert.Null(await RepositoryFiles.ListAsync(runner, temp.Path, CancellationToken.None));
    }
}
