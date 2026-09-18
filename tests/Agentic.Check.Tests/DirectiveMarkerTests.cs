namespace Agentic.Check.Tests;

public sealed class DirectiveMarkerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("dotnet-agentic-engineering:")]
    public async Task InstallsEitherSourceFormatWithPrefixFreeMarkers(string sourcePrefix)
    {
        using TempDirectory temp = new();
        temp.Write("AGENTS.md", "Keep my instructions.\n");
        DirectiveInstaller installer = new(Source(Block(sourcePrefix)), new NullReporter());

        var result = await installer.EnsureAsync(temp.Path, StackDetector.Detect(temp.Path), false, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Keep my instructions.\n\n" + Block("") + "\n", await File.ReadAllTextAsync(result.AgentsFile));
        Assert.Equal("@AGENTS.md\n", await File.ReadAllTextAsync(result.ClaudeFile));
        var next = await installer.PlanAsync(temp.Path, StackDetector.Detect(temp.Path), CancellationToken.None);
        Assert.Equal(DirectiveStatuses.Current, Assert.Single(next.Directives).Status);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "dotnet-agentic-engineering:")]
    [InlineData("dotnet-agentic-engineering:", "")]
    [InlineData("dotnet-agentic-engineering:", "dotnet-agentic-engineering:")]
    public async Task UpdatesEitherInstalledFormatInPlaceAndRemainsCurrent(string installedPrefix, string sourcePrefix)
    {
        using TempDirectory temp = new();
        string oldBlock = Block(installedPrefix, "Old body.");
        string before = "User prefix.\n\n" + oldBlock + "\n\nUser suffix.\n";
        temp.Write("AGENTS.md", before);
        DirectiveInstaller installer = new(Source(Block(sourcePrefix)), new NullReporter());

        var result = await installer.EnsureAsync(temp.Path, StackDetector.Detect(temp.Path), false, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(DirectiveStatuses.Outdated, Assert.Single(result.Directives).Status);
        string after = await File.ReadAllTextAsync(result.AgentsFile);
        Assert.Equal(before.Replace(oldBlock, Block(""), StringComparison.Ordinal), after);
        var again = await installer.EnsureAsync(temp.Path, StackDetector.Detect(temp.Path), false, CancellationToken.None);
        Assert.True(again.Success, again.Error);
        Assert.Equal(DirectiveStatuses.Current, Assert.Single(again.Directives).Status);
        Assert.Equal(after, await File.ReadAllTextAsync(result.AgentsFile));
        Assert.Empty(again.Actions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkerOnlyMigrationHonorsSelectionAndDryRun(bool dryRun)
    {
        using TempDirectory temp = new();
        string oldBlock = Block("dotnet-agentic-engineering:");
        string other = Block("dotnet-agentic-engineering:").Replace("foundation-example", "foundation-other", StringComparison.Ordinal);
        string before = "My instructions.\n\n" + oldBlock + "\n\n" + other + "\n";
        temp.Write("AGENTS.md", before);
        FakeDirectiveSource source = new(new Dictionary<string, string>
        {
            ["foundation-example.md"] = "~~~md\n" + Block("") + "\n~~~",
            ["foundation-other.md"] = "~~~md\n" + other + "\n~~~"
        });
        DirectiveInstaller installer = new(source, new NullReporter());
        var plan = await installer.PlanAsync(temp.Path, StackDetector.Detect(temp.Path), CancellationToken.None);
        Assert.True(plan.Success, plan.Error);
        Assert.Equal(2, plan.OutdatedCount);
        Assert.Equal(0, plan.MissingCount);

        var result = await installer.ApplyAsync(plan, ["foundation-example"], dryRun, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(dryRun ? before : before.Replace(oldBlock, Block(""), StringComparison.Ordinal), await File.ReadAllTextAsync(result.AgentsFile));
        Assert.Equal(!dryRun, File.Exists(result.ClaudeFile));
    }

    [Theory]
    [InlineData("<!-- foundation-example:start -->")]
    [InlineData("<!-- foundation-example:end -->")]
    [InlineData("<!-- dotnet-agentic-engineering:foundation-example:start -->")]
    [InlineData("<!-- foundation-example:end -->\n<!-- foundation-example:start -->")]
    [InlineData("<!-- dotnet-agentic-engineering:foundation-example:end -->\n<!-- dotnet-agentic-engineering:foundation-example:start -->")]
    [InlineData("<!-- foundation-example:start -->\n<!-- dotnet-agentic-engineering:foundation-example:end -->")]
    [InlineData("<!-- dotnet-agentic-engineering:foundation-example:start -->\n<!-- foundation-example:end -->")]
    [InlineData("<!-- foundation-example:start --><!-- foundation-example:end --><!-- foundation-example:start --><!-- foundation-example:end -->")]
    [InlineData("<!-- foundation-example:start --><!-- foundation-example:end --><!-- dotnet-agentic-engineering:foundation-example:start --><!-- dotnet-agentic-engineering:foundation-example:end -->")]
    public async Task AmbiguousOrIncompleteInstalledMarkersFailWithoutWrites(string markers)
    {
        using TempDirectory temp = new();
        string before = "User content.\n" + markers + "\nKeep this.\n";
        temp.Write("AGENTS.md", before);
        DirectiveInstaller installer = new(Source(Block("")), new NullReporter());

        var result = await installer.EnsureAsync(temp.Path, StackDetector.Detect(temp.Path), false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("marker is inconsistent", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "CLAUDE.md")));
    }

    [Theory]
    [InlineData("<!-- foundation-example:start -->")]
    [InlineData("<!-- foundation-example:start --><!-- dotnet-agentic-engineering:foundation-example:end -->")]
    [InlineData("<!-- foundation-other:start --><!-- foundation-other:end -->")]
    public async Task InvalidSourceMarkersFailWithoutWrites(string markers)
    {
        using TempDirectory temp = new();
        temp.Write("AGENTS.md", "Keep this.\n");
        DirectiveInstaller installer = new(Source(markers), new NullReporter());

        var result = await installer.EnsureAsync(temp.Path, StackDetector.Detect(temp.Path), false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Keep this.\n", await File.ReadAllTextAsync(Path.Combine(temp.Path, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "CLAUDE.md")));
    }

    static string Block(string prefix, string body = "Body with dotnet-agentic-engineering: kept verbatim.")
        => $"<!-- {prefix}foundation-example:start -->\n{body}\n<!-- {prefix}foundation-example:end -->";

    static FakeDirectiveSource Source(string block)
        => new(new Dictionary<string, string> { ["foundation-example.md"] = "~~~md\n" + block + "\n~~~" });
}
