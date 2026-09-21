using System.Globalization;

namespace Agentic.Tests;

public sealed class CompanionTests
{
    [Theory]
    [InlineData("2.2.9", false)]
    [InlineData("2.3.0", true)]
    [InlineData("2.3.99", true)]
    [InlineData("2.3.0-preview.1", true)]
    [InlineData("2.4.0-preview.1", true)]
    [InlineData("3.0.0", false)]
    public void CompatibilityMatrix(string version, bool compatible)
        => Assert.Equal(compatible, ToolVersion.Parse(version).Satisfies(ToolVersion.ParseMinimum("2.3")));

    [Theory]
    [InlineData("2")]
    [InlineData("2.3.0")]
    [InlineData("2.3-*")]
    [InlineData("-2.3")]
    [InlineData("2.*")]
    [InlineData("2.")]
    [InlineData("2.3 ")]
    [InlineData("2.3-4.0")]
    public async Task InvalidMinimumIsAnArgumentError(string minimum)
    {
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(2, await AgenticCli.InvokeAsync(["prompt-log", "show", "-m", minimum], error: error, output: TextWriter.Null).ConfigureAwait(true));
        Assert.Contains("major.minor", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrap", "-m", true)]
    [InlineData("wrap", "--minver", true)]
    [InlineData("wrap", "-m", false)]
    [InlineData("wrap", "--minver", false)]
    [InlineData("show", "-m", true)]
    [InlineData("show", "--minver", true)]
    [InlineData("show", "-m", false)]
    [InlineData("show", "--minver", false)]
    [InlineData("check", "-m", true)]
    [InlineData("check", "--minver", true)]
    [InlineData("check", "-m", false)]
    [InlineData("check", "--minver", false)]
    public async Task AliasesAndPlacementGuardEveryOperation(string command, string alias, bool leading)
    {
        using Workspace workspace = new();
        string destination = Path.Combine(workspace.Path, "block");
        string[] operation = command == "wrap" ? ["prompt-log", command, "--input", "-", "--prompt-log", destination] : ["prompt-log", command];
        string[] args = leading ? [alias, "2.3", .. operation] : [.. operation, alias, "2.3"];
        FakeGit git = new();
        using StringReader input = new("entry");
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(1, await AgenticCli.InvokeAsync(args, input, TextWriter.Null, error, git, workspace.Path, "3.0.0").ConfigureAwait(true));
        Assert.Empty(git.Calls);
        Assert.False(File.Exists(destination));
        Assert.Contains("major 2, minor 3 or later", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("agentic-check interactively", error.ToString(), StringComparison.Ordinal);
        using StringReader validInput = new("entry");
        CompatibilityContext? context = null;
        Assert.Equal(0, await AgenticCli.InvokeAsync(args, validInput, TextWriter.Null, error, git, workspace.Path, "2.4.0-preview.1", value => context = value).ConfigureAwait(true));
        Assert.NotNull(context);
        Assert.True(context.Explicit);
        Assert.True(context.Running.IsPrerelease);
        Assert.Equal("2.3", context.Required.Minimum);
    }

    [Theory]
    [InlineData(null, "dotnet agentic")]
    [InlineData("1.0.0", "dna")]
    public async Task HelpShowsTheCommandTheUserTyped(string? launcherVersion, string usage)
    {
        using StringWriter root = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(2, await AgenticCli.InvokeAsync([], output: root, error: error, readEnvironment: _ => launcherVersion).ConfigureAwait(true));
        string rootHelp = root.ToString().ReplaceLineEndings("\n");
        Assert.Contains($"\n  {usage} [command] [options]\n", rootHelp, StringComparison.Ordinal);
        Assert.Contains("dna is the shorthand for dotnet agentic", rootHelp, StringComparison.Ordinal);
        Assert.Contains("Keeps instructions and this tool versioned together", rootHelp, StringComparison.Ordinal);
        Assert.Contains("Required command was not provided.", error.ToString(), StringComparison.Ordinal);
        using StringWriter wrap = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--help"], output: wrap, readEnvironment: _ => launcherVersion).ConfigureAwait(true));
        Assert.Contains($"\n  {usage} prompt-log wrap [options]\n", wrap.ToString().ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultContextAndHelpRemainAvailable()
    {
        CompatibilityContext? context = null;
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "show"], output: TextWriter.Null, git: new FakeGit(), runningVersion: "3.7.4-preview.1", observeContext: value => context = value).ConfigureAwait(true));
        Assert.NotNull(context);
        Assert.False(context.Explicit);
        Assert.Equal("3.7", context.Required.Minimum);
        Assert.Equal(0, context.Required.Patch);
        Assert.False(context.Required.IsPrerelease);
        foreach (string command in new[] { "wrap", "show", "check" })
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", command, "--help", "-m", "99.0"], output: output).ConfigureAwait(true));
            Assert.Contains("--minver", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("-m", output.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, await AgenticCli.InvokeAsync(["--version", "-m", "99.0"], output: TextWriter.Null).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("prompt-log wrap")]
    [InlineData("prompt-log show --unknown")]
    [InlineData("prompt-log check --commit")]
    [InlineData("prompt-log wrap --input - --prompt-log")]
    public async Task MissingAndUnknownArguments(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Assert.Equal(2, await AgenticCli.InvokeAsync(command.Split(' '), output: TextWriter.Null, error: TextWriter.Null).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("quotes \" \\ café 漢字 😀 [red]markup[/]")]
    [InlineData("first\n\n\nthird\n\n")]
    [InlineData("first\r\n\r\nthird\r\n")]
    [InlineData("first\r\rthird\r")]
    [InlineData("prompt-log:\nprompt-log-end:\nnull\n123\ntrue")]
    [InlineData("\"prompt-log-end:\"\n\\prompt-log-end:\n\\\\prompt-log-end:")]
    [InlineData("prompt-log-format: raw-v1\n\"JSON-looking raw text\"")]
    [InlineData("\\\n\\text\n\\\\text\n\\\\\\text")]
    [InlineData(" prompt-log:\nprompt-log-end: \ninline prompt-log:")]
    [InlineData(" \t \n")]
    [InlineData("\n")]
    [InlineData("\n\n")]
    public async Task WrapPreservesCompleteRawTextAndReplacesPreviousOutput(string text)
    {
        using Workspace workspace = new();
        string block = Path.Combine(workspace.Path, "block");
        await File.WriteAllTextAsync(block, "old output that must not be accumulated").ConfigureAwait(true);
        using StringReader input = new(text);
        await PromptLogWrapper.WrapAsync("-", block, input, CancellationToken.None).ConfigureAwait(true);
        string content = await File.ReadAllTextAsync(block).ConfigureAwait(true);
        Assert.Equal(PromptBlock.Normalize(text), PromptLogReader.Parse(content, blockOnly: true)!.Text);
        Assert.StartsWith("prompt-log:\nprompt-log-format: raw-v1\n", content, StringComparison.Ordinal);
        Assert.EndsWith("\nprompt-log-end:\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', content);
        Assert.DoesNotContain("old output", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DelimiterEscapingHasAnExactReversibleRepresentation()
    {
        const string text = "literal\n\"prompt-log-end:\"\nprompt-log:\nprompt-log-end:\n\\prompt-log-end:\n\\\\server\n\n";
        const string block = "prompt-log:\nprompt-log-format: raw-v1\nliteral\n\"prompt-log-end:\"\n\\prompt-log:\n\\prompt-log-end:\n\\\\prompt-log-end:\n\\\\\\server\n\n\nprompt-log-end:\n";
        Assert.Equal(block, PromptBlock.Format(text));
        Assert.Equal(text, PromptLogReader.Parse(block, true)!.Text);
        Assert.Equal(1, block.Split('\n').Count(line => line == "prompt-log:"));
        Assert.Equal(1, block.Split('\n').Count(line => line == "prompt-log-end:"));
        Assert.Equal("valid prompt log", PromptLogReader.Parse(PromptBlock.Format("\"JSON-looking raw text\""))!.Description);
    }

    [Theory]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\nbody\n", "delimiters")]
    [InlineData("prompt-log-end:\nprompt-log:\nprompt-log-format: raw-v1\nbody\n", "delimiters")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\nbody\nprompt-log:\nprompt-log-end:\n", "delimiters")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\nbody\nprompt-log-end:\nprompt-log-end:\n", "delimiters")]
    [InlineData("outside\nprompt-log:\nprompt-log-format: raw-v1\nbody\nprompt-log-end:\n", "delimiters")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\nbody\nprompt-log-end:\noutside\n", "delimiters")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\nprompt-log-end:\n", "Regenerate")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v1\n\\oops\nprompt-log-end:\n", "do not escape it by hand")]
    [InlineData("prompt-log:\nprompt-log-format: raw-v2\nbody\nprompt-log-end:\n", "Unsupported")]
    public void MalformedRawBlocksReportActionableErrors(string block, string diagnostic)
    {
        var error = Assert.Throws<FormatException>(() => PromptLogReader.Parse(block, blockOnly: true));
        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FileAndStdioCombinationsProduceTheSameBlock(bool fileInput, bool fileOutput)
    {
        const string text = "Prompt one\n\nQ: why?\nA: yes\n\nprompt-log-end:\n\\raw path\n";
        using Workspace workspace = new();
        string inputPath = Path.Combine(workspace.Path, "input");
        string outputPath = Path.Combine(workspace.Path, "output");
        if (fileInput)
        {
            await File.WriteAllTextAsync(inputPath, text).ConfigureAwait(true);
        }

        using StringReader input = new(text);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        FakeGit git = new();
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", fileInput ? inputPath : "-", "--prompt-log", fileOutput ? outputPath : "-", "-m", "2.3"], input, output, error, git).ConfigureAwait(true));
        string actual = fileOutput ? await File.ReadAllTextAsync(outputPath).ConfigureAwait(true) : output.ToString();
        Assert.Equal(PromptBlock.Format(text), actual);
        Assert.Equal(text, PromptLogReader.Parse(actual)!.Text);
        Assert.Empty(error.ToString());
        Assert.Empty(git.Calls);
        if (fileOutput)
        {
            Assert.Empty(output.ToString());
        }
        else
        {
            Assert.Equal(fileInput ? 1 : 0, Directory.GetFiles(workspace.Path).Length);
        }
    }

    [Fact]
    public async Task StdoutIsDefaultAndEmptyInputClearsNamedOutput()
    {
        using Workspace workspace = new();
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringReader input = new("raw log");
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-"], input, output).ConfigureAwait(true));
        Assert.Equal("prompt-log:\nprompt-log-format: raw-v1\nraw log\nprompt-log-end:\n", output.ToString());
        _ = output.GetStringBuilder().Clear();
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-"], TextReader.Null, output).ConfigureAwait(true));
        Assert.Empty(output.ToString());
        Assert.Empty(Directory.GetFiles(workspace.Path));
        string block = Path.Combine(workspace.Path, "block");
        await File.WriteAllTextAsync(block, PromptBlock.Format("old log")).ConfigureAwait(true);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-", "--prompt-log", block], TextReader.Null, output).ConfigureAwait(true));
        Assert.Empty(await File.ReadAllBytesAsync(block).ConfigureAwait(true));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("# AGENTS.md instructions for /repo\n\n<INSTRUCTIONS>\n## Rules\n</INSTRUCTIONS>\n\nbuild the web api\n", "# AGENTS.md instructions for on line 1")]
    [InlineData("build it\n\n  <environment_context>\n  <cwd>/repo</cwd>\n</environment_context>\n", "<environment_context> on line 3")]
    [InlineData("done\n</INSTRUCTIONS>\n", "</INSTRUCTIONS> on line 2")]
    public async Task HarnessInjectedContextIsRejectedBeforeAnyOutput(string text, string diagnostic)
    {
        using Workspace workspace = new();
        string destination = Path.Combine(workspace.Path, "block.txt");
        await File.WriteAllTextAsync(destination, "previous block").ConfigureAwait(true);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        using StringReader input = new(text);
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-", "--prompt-log", destination, "-m", "2.3"], input, output, error).ConfigureAwait(true));
        Assert.Empty(output.ToString());
        Assert.Contains("harness-injected context (" + diagnostic + ")", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("log only what the user typed", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("previous block", await File.ReadAllTextAsync(destination).ConfigureAwait(true));

        using StringReader ordinary = new("please update the AGENTS.md instructions for the web project\n\nQ: Which one?\nA: Web\n");
        using StringWriter block = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-", "-m", "2.3"], ordinary, block, error).ConfigureAwait(true));
        Assert.Contains("please update the AGENTS.md instructions for the web project", block.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedInputAndIncompatibilityDoNotEmitPartialStdout()
    {
        using Workspace workspace = new();
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", Path.Combine(workspace.Path, "missing")], output: output, error: error).ConfigureAwait(true));
        Assert.Empty(output.ToString());
        using StringReader input = new("must not be consumed");
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", "-", "-m", "3.0"], input, output, error).ConfigureAwait(true));
        Assert.Equal('m', input.Peek());
        Assert.Empty(output.ToString());
        string invalid = Path.Combine(workspace.Path, "invalid-utf8");
        await File.WriteAllBytesAsync(invalid, [0xff, 0xff]).ConfigureAwait(true);
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "wrap", "--input", invalid], output: output, error: error).ConfigureAwait(true));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task FileInputCreatesOutputAndRejectsSymlinkAlias()
    {
        using Workspace workspace = new();
        string input = Path.Combine(workspace.Path, "input");
        string output = Path.Combine(workspace.Path, "output");
        await File.WriteAllTextAsync(input, "file entry\n").ConfigureAwait(true);
        await PromptLogWrapper.WrapAsync(input, output, TextReader.Null, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("file entry\n", PromptLogReader.Parse(await File.ReadAllTextAsync(output).ConfigureAwait(true), true)!.Text);
        if (!OperatingSystem.IsWindows())
        {
            string alias = Path.Combine(workspace.Path, "alias");
            _ = File.CreateSymbolicLink(alias, input);
            _ = await Assert.ThrowsAsync<IOException>(() => PromptLogWrapper.WrapAsync(input, alias, TextReader.Null, CancellationToken.None)).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task InterruptedReplacementAndDeniedDirectoryPreserveExistingBlock()
    {
        using Workspace workspace = new();
        string block = Path.Combine(workspace.Path, "block");
        string original = PromptBlock.Format("original");
        await File.WriteAllTextAsync(block, original).ConfigureAwait(true);
        using CancellationTokenSource cancellation = new();
        using StringReader input = new("entry");
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PromptLogWrapper.WrapAsync("-", block, input, cancellation.Token, cancellation.Cancel)).ConfigureAwait(true);
        Assert.Equal(original, await File.ReadAllTextAsync(block).ConfigureAwait(true));
        Assert.Empty(Directory.EnumerateFiles(workspace.Path, "*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(workspace.Path);
            try
            {
                File.SetUnixFileMode(workspace.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                using StringReader denied = new("entry");
                _ = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PromptLogWrapper.WrapAsync("-", block, denied, CancellationToken.None)).ConfigureAwait(true);
                Assert.Equal(original, await File.ReadAllTextAsync(block).ConfigureAwait(true));
            }
            finally
            {
                File.SetUnixFileMode(workspace.Path, mode);
            }
        }
    }

    [Fact]
    public async Task HardLinksAndParentAliasesCannotOverwriteInput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Workspace workspace = new();
        string input = Path.Combine(workspace.Path, "input");
        await File.WriteAllTextAsync(input, "original").ConfigureAwait(true);
        _ = await new GitCommandRunner("ln").RunAsync([input, "hard-link"], workspace.Path, CancellationToken.None).ConfigureAwait(true);
        _ = await Assert.ThrowsAsync<IOException>(() => PromptLogWrapper.WrapAsync(input, Path.Combine(workspace.Path, "hard-link"), TextReader.Null, CancellationToken.None)).ConfigureAwait(true);
        string alias = Path.Combine(workspace.Path, "alias");
        _ = Directory.CreateSymbolicLink(alias, workspace.Path);
        _ = await Assert.ThrowsAsync<IOException>(() => PromptLogWrapper.WrapAsync(input, Path.Combine(alias, "input"), TextReader.Null, CancellationToken.None)).ConfigureAwait(true);
        Directory.Delete(alias);
        Assert.Equal("original", await File.ReadAllTextAsync(input).ConfigureAwait(true));
    }

    [Fact]
    public async Task ProcessRunnerDrainsBothStreamsAndCancelsChild()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Workspace workspace = new();
        string text = await new GitCommandRunner("python3").RunAsync(["-c", "import sys; sys.stderr.write('e'*1000000); sys.stdout.write('o'*1000000)"], workspace.Path, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1000000, text.Length);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitCommandRunner("python3").RunAsync(["-c", "import time; time.sleep(60)"], workspace.Path, cancellation.Token)).ConfigureAwait(true);
    }

    [Fact]
    public async Task MissingIdenticalLockedAndCancelledInputsLeaveOutputIntact()
    {
        using Workspace workspace = new();
        string block = Path.Combine(workspace.Path, "block");
        string original = PromptBlock.Format("original");
        await File.WriteAllTextAsync(block, original).ConfigureAwait(true);
        _ = await Assert.ThrowsAsync<IOException>(() => PromptLogWrapper.WrapAsync(block, block, TextReader.Null, CancellationToken.None)).ConfigureAwait(true);
        _ = await Assert.ThrowsAsync<FileNotFoundException>(() => PromptLogWrapper.WrapAsync(Path.Combine(workspace.Path, "missing"), block, TextReader.Null, CancellationToken.None)).ConfigureAwait(true);
        using (FileStream gate = new(block + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using StringReader input = new("new");
            _ = await Assert.ThrowsAsync<IOException>(() => PromptLogWrapper.WrapAsync("-", block, input, CancellationToken.None)).ConfigureAwait(true);
        }

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(true);
        using StringReader interrupted = new("new");
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PromptLogWrapper.WrapAsync("-", block, interrupted, cancelled.Token)).ConfigureAwait(true);
        Assert.Equal(original, await File.ReadAllTextAsync(block).ConfigureAwait(true));
        Assert.Empty(Directory.EnumerateFiles(workspace.Path, "*.tmp"));
    }

    [Fact]
    public async Task RealGitHistoryDatesLegacyMalformedTrailersAndWorktrees()
    {
        using Workspace workspace = new();
        _ = await workspace.GitAsync("init").ConfigureAwait(true);
        _ = await workspace.GitAsync("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "-m", "ordinary").ConfigureAwait(true);
        Assert.Equal(0, await InvokeAsync("check").ConfigureAwait(true));
        string ordinary = await workspace.GitAsync("rev-parse", "HEAD").ConfigureAwait(true);
        await CommitAsync("subject\n\n" + PromptBlock.Format("[red]literal[/]\n\nUnicode 漢字\n\nsecond entry\n\nprompt-log-end:\n\\literal\n") + "\nSigned-off-by: Fixture <fixture@example.invalid>\n").ConfigureAwait(true);
        Assert.Equal(0, await InvokeAsync("check").ConfigureAwait(true));
        await CommitAsync("prompt-log:\n\n1. \"legacy text\"\n2. Q: \"question\" -> A: \"answer\"").ConfigureAwait(true);
        await CommitAsync("historical JSON\n\nprompt-log:\n\"original JSON text\"\n\"\"\n\"last line\"\n\nprompt-log-end:\n").ConfigureAwait(true);
        await CommitAsync("broken\n\nprompt-log:\ntrue\nprompt-log-end:").ConfigureAwait(true);
        await CommitAsync("final\n\n" + PromptBlock.Format("last valid entry")).ConfigureAwait(true);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using StringWriter error = new(CultureInfo.InvariantCulture);
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "show"], output: output, error: error, directory: workspace.Path).ConfigureAwait(true));
        Assert.Contains("[red]literal[/]\n\nUnicode 漢字\n", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("legacy text", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("original JSON text\n\nlast line", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("prompt-log-end:\n\\literal\n", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PromptBlock.FormatHeader, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Signed-off-by", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("last valid entry", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("malformed prompt log", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ordinary.Trim(), output.ToString(), StringComparison.Ordinal);
        using StringWriter dates = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "show", "--until", "2000-01-01"], output: dates, directory: workspace.Path).ConfigureAwait(true));
        Assert.Equal(string.Empty, dates.ToString());
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "show", "--since", "2037-01-01"], output: dates, directory: workspace.Path).ConfigureAwait(true));
        Assert.Equal(1, await InvokeAsync("check", "--commit", "missing-ref").ConfigureAwait(true));
        Assert.Equal(1, await InvokeAsync("check", "--commit=--all").ConfigureAwait(true));
        string child = Directory.CreateDirectory(Path.Combine(workspace.Path, "child")).FullName;
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "check"], directory: child, output: TextWriter.Null).ConfigureAwait(true));
        string worktree = Path.Combine(workspace.Path, "worktree");
        _ = await workspace.GitAsync("worktree", "add", "--detach", worktree).ConfigureAwait(true);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "check"], directory: worktree, output: TextWriter.Null).ConfigureAwait(true));

        async Task CommitAsync(string message)
            => _ = await workspace.GitAsync("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "--cleanup=verbatim", "-m", message).ConfigureAwait(true);

        Task<int> InvokeAsync(params string[] args)
            => AgenticCli.InvokeAsync(["prompt-log", .. args], directory: workspace.Path, output: TextWriter.Null, error: TextWriter.Null);
    }

    [Theory]
    [InlineData("first  \n\n\nlast\t", "first\n\nlast")]
    [InlineData("prompt-log: \t\nprompt-log-end:\t \n\\prompt-log-end:  ", "prompt-log:\nprompt-log-end:\n\\prompt-log-end:")]
    [InlineData(" \t\n\n", "")]
    public async Task NormalGitCleanupPreservesValidFraming(string body, string expected)
    {
        using Workspace workspace = new();
        _ = await workspace.GitAsync("init").ConfigureAwait(true);
        string block = PromptBlock.Format(body);
        Assert.Equal(PromptBlock.Normalize(body), PromptLogReader.Parse(block)!.Text);
        _ = await workspace.GitAsync("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.cleanup=default", "commit", "--allow-empty", "-m", "Subject  \n\nBody  \n\n\n" + block + "\nReviewed-by: Fixture  \n").ConfigureAwait(true);
        string committed = await workspace.GitAsync("show", "-s", "--format=%B").ConfigureAwait(true);
        Assert.Equal(expected, PromptLogReader.Parse(committed)!.Text);
        Assert.StartsWith("Subject\n\nBody\n\nprompt-log:", committed, StringComparison.Ordinal);
        Assert.Equal(1, committed.Split('\n').Count(line => line == "prompt-log:"));
        Assert.Equal(1, committed.Split('\n').Count(line => line == "prompt-log-end:"));
        using StringWriter output = new(CultureInfo.InvariantCulture);
        Assert.Equal(0, await AgenticCli.InvokeAsync(["prompt-log", "check"], directory: workspace.Path, output: output).ConfigureAwait(true));
        Assert.Contains("valid prompt log", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingGitAndNonRepositoryAreOperationErrors()
    {
        using Workspace workspace = new();
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "show"], git: new GitCommandRunner("missing-git-fixture"), directory: workspace.Path, error: TextWriter.Null).ConfigureAwait(true));
        Assert.Equal(1, await AgenticCli.InvokeAsync(["prompt-log", "check"], directory: workspace.Path, error: TextWriter.Null).ConfigureAwait(true));
    }
}

sealed class FakeGit : IGitCommandRunner
{
    internal List<IReadOnlyList<string>> Calls { get; } = [];

    public Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        return Task.FromResult(arguments[0] == "rev-parse" ? new string('a', 64) : arguments[0] == "show" ? "2026-09-11\nordinary" : string.Empty);
    }
}

sealed class Workspace : IDisposable
{
    internal string Path { get; } = Directory.CreateTempSubdirectory("agentic-tests-").FullName;

    internal Task<string> GitAsync(params string[] args) => new GitCommandRunner().RunAsync(args, Path, CancellationToken.None);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
