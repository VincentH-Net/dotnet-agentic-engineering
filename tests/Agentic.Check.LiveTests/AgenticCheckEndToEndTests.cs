using System.Text.Json;
using Hex1b;
using Hex1b.Automation;
using Xunit.Abstractions;

namespace Agentic.Check.LiveTests;

public sealed class AgenticCheckEndToEndTests(ITestOutputHelper testOutput)
{
    readonly ITestOutputHelper output = testOutput;

    const string DotnetUpdateOutput = """
      • dotnet-livecharts2 (VincentH-Net/dotnet-agentic-engineering) 52b04c64 > c9fa2d43 [1.2.0]
      1 update(s) available:
    """;

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task HelpListsCoreOptionsAndAgentValues()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(HelpListsCoreOptionsAndAgentValues)).ConfigureAwait(true);
        var result = await RunCommandAsync(workspace, "--help").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Optimizes your repo for agentic engineering", result.Screen, StringComparison.Ordinal);
        Assert.Contains("--agents", result.Screen, StringComparison.Ordinal);
        Assert.Contains("[default: claude-code,codex]", result.Screen, StringComparison.Ordinal);
        Assert.Contains("--skills-dir", result.Screen, StringComparison.Ordinal);
        Assert.Contains("--dry-run", result.Screen, StringComparison.Ordinal);
        Assert.Contains("GitHub Copilot (github-copilot)", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Claude Code (claude-code)", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Codex (codex)", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("standard", result.Screen, StringComparison.OrdinalIgnoreCase);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InvalidCommandLineInputsReportErrorsBeforeRunningWorkflow()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(InvalidCommandLineInputsReportErrorsBeforeRunningWorkflow)).ConfigureAwait(true);
        workspace.WriteRepoFile("target-file", "not a directory");
        workspace.EnsureRepoDirectory("custom-skills");

        await using (var terminal = CreateTerminal(workspace))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var runTask = terminal.RunAsync(cancellation.Token);
            var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

            try
            {
                var unknown = await RunCommandInOpenTerminalAsync(auto, workspace, "--not-real", 1).ConfigureAwait(true);
                Assert.Contains("Unknown option: --not-real", unknown.Screen, StringComparison.Ordinal);

                _ = await RunCommandInOpenTerminalAsync(auto, workspace, $"--dry-run --agents standard {Quote(workspace.RepoPath)}", 1).ConfigureAwait(true);

                _ = await RunCommandInOpenTerminalAsync(
                    auto,
                    workspace,
                    $"--dry-run --skills-dir {Quote(Path.Combine(workspace.RepoPath, "custom-skills"))} --agents codex {Quote(workspace.RepoPath)}",
                    1).ConfigureAwait(true);

                var fileTarget = await RunCommandInOpenTerminalAsync(auto, workspace, Quote(Path.Combine(workspace.RepoPath, "target-file")), 2).ConfigureAwait(true);
                Assert.Contains("Invalid target directory", fileTarget.Screen, StringComparison.Ordinal);
                Assert.Contains("is a file", fileTarget.Screen, StringComparison.Ordinal);
            }
            finally
            {
                await StopShellAsync(auto, runTask).ConfigureAwait(true);
            }
        }

        string recordingText = await workspace.ReadRecordingTextAsync().ConfigureAwait(true);
        Assert.Contains("Unknown agent value(s): standard", recordingText, StringComparison.Ordinal);
        Assert.Contains("Specify no more than one of --skills-dir and --agents.", recordingText, StringComparison.Ordinal);
        Assert.Empty(await workspace.ReadGhLogAsync().ConfigureAwait(true));
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DryRunReportsSummaryActionsSkillUpdatesAndJsonReport()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(DryRunReportsSummaryActionsSkillUpdatesAndJsonReport)).ConfigureAwait(true);
        await workspace.SetSkillUpdateDryRunOutputAsync(DotnetUpdateOutput).ConfigureAwait(true);
        string reportPath = Path.Combine(workspace.RootPath, "report.json");

        var result = await RunCommandAsync(
            workspace,
            $"--dry-run --report {Quote(reportPath)} {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(".NET Agentic Engineering Check", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Check", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Status", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Target agents", result.Screen, StringComparison.Ordinal);
        Assert.Contains("claude-code,codex", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Skills directories", result.Screen, StringComparison.Ordinal);
        Assert.Contains(".claude/skills", result.Screen, StringComparison.Ordinal);
        Assert.Contains(".agents/skills", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Recommended directives", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Recommended skills", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Would install directives into AGENTS.md:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Would install skills into skills directories:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Would update skills in skills directories:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("VincentH-Net/dotnet-agentic-engineering repo:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("dotnet:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("dotnet-livecharts2", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("Directive dotnet-cli-run:", result.Screen, StringComparison.Ordinal);

        string ghLog = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.Contains("skill update --dir", ghLog, StringComparison.Ordinal);
        Assert.Contains(".claude/skills --all --dry-run", ghLog, StringComparison.Ordinal);
        Assert.Contains(".agents/skills --all --dry-run", ghLog, StringComparison.Ordinal);
        Assert.DoesNotContain(" skill install ", ghLog, StringComparison.Ordinal);
        Assert.DoesNotContain(".agents/skills --all\n", ghLog, StringComparison.Ordinal);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(true));
        Assert.True(report.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, report.RootElement.GetProperty("outdatedSkills").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("skillsDirectories").GetArrayLength());
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DryRunForFullStackReportsOrderedStackGatesWarningAndSkillGroups()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(
            nameof(DryRunForFullStackReportsOrderedStackGatesWarningAndSkillGroups),
            writeDotnetProject: false).ConfigureAwait(true);
        workspace.WriteRepoFile(
            "src/UnoCSharp/App.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVVM;CSharpMarkup;Material</UnoFeatures>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Orleans.Server" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteRepoFile(
            "src/UnoCSharp2/App.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVUX;SimpleTheme</UnoFeatures>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="CSharpMarkup.WinUI" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteRepoFile(
            "src/Web/Web.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = await RunCommandAsync(workspace, $"--dry-run --agents codex {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Uno Platform", result.Screen, StringComparison.Ordinal);
        Assert.Contains("UI update pattern: mvux, mvvm", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Markup type: csharp, csharp2, xaml", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Design system: material, simple", result.RecordingText, StringComparison.Ordinal);
        Assert.Contains("Microsoft Orleans", result.Screen, StringComparison.Ordinal);
        Assert.Contains("ASP.NET", result.Screen, StringComparison.Ordinal);
        Assert.Contains(".NET", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Agentic Foundation", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Warning: multiple Uno markup gate values detected (csharp, csharp2) - agents may become confused:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("src/UnoCSharp/App.csproj: csharp", result.Screen, StringComparison.Ordinal);
        Assert.Contains("src/UnoCSharp2/App.csproj: csharp2", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("src/UnoCSharp/App.csproj: xaml", result.Screen, StringComparison.Ordinal);
        Assert.Contains("VincentH-Net/dotnet-agentic-engineering repo:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("uno-platform:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("dotnet:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("mtmattei/UnoPlatformSkills repo:", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("UnoPlatformSkills:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("unoplatform/studio repo:", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("studio:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("dotnet/skills repo:", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-aspnetcore:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("dotnet-test:", result.Screen, StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task YesInstallsDirectivesIntoAgentsClaudeAndCopiesSkillsAcrossDefaultAgentDirectories()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(YesInstallsDirectivesIntoAgentsClaudeAndCopiesSkillsAcrossDefaultAgentDirectories)).ConfigureAwait(true);
        var result = await RunCommandAsync(workspace, $"--yes {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, "CLAUDE.md")));
        Assert.Contains("@AGENTS.md", await workspace.ReadRepoFileAsync("CLAUDE.md").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Contains("dotnet-agentic-engineering:dotnet-cli-run:start", await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, ".claude", "skills", "dotnet-livecharts2", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, ".agents", "skills", "dotnet-livecharts2", "SKILL.md")));

        string ghLog = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.Contains("skill install VincentH-Net/dotnet-agentic-engineering dotnet-livecharts2 --dir", ghLog, StringComparison.Ordinal);
        Assert.Contains(".claude/skills", ghLog, StringComparison.Ordinal);
        Assert.DoesNotContain("skill install VincentH-Net/dotnet-agentic-engineering dotnet-livecharts2 --dir " + Path.Combine(workspace.RepoPath, ".agents", "skills"), ghLog, StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task CodexOnlyYesCreatesAgentsButNotClaudeOrClaudeSkillsDirectory()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(CodexOnlyYesCreatesAgentsButNotClaudeOrClaudeSkillsDirectory)).ConfigureAwait(true);
        var result = await RunCommandAsync(workspace, $"--yes --agents codex {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(workspace.RepoPath, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, ".agents", "skills", "dotnet-livecharts2", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.RepoPath, ".claude")));
        Assert.DoesNotContain(".claude/skills", result.Screen, StringComparison.Ordinal);
        Assert.Contains(".agents/skills", result.Screen, StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task CustomSkillsDirectoryYesInstallsOnlyIntoSpecifiedDirectory()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(CustomSkillsDirectoryYesInstallsOnlyIntoSpecifiedDirectory)).ConfigureAwait(true);
        string customSkillsDirectory = Path.Combine(workspace.RepoPath, "custom-skills");
        workspace.EnsureRepoDirectory("custom-skills");

        var result = await RunCommandAsync(
            workspace,
            $"--yes --skills-dir custom-skills {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("custom skills directory", result.Screen, StringComparison.Ordinal);
        Assert.Contains("custom-skills", result.Screen, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(customSkillsDirectory, "dotnet-livecharts2", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.RepoPath, ".agents")));
        Assert.False(Directory.Exists(Path.Combine(workspace.RepoPath, ".claude")));
        Assert.True(File.Exists(Path.Combine(workspace.RepoPath, "AGENTS.md")));
        Assert.False(File.Exists(Path.Combine(workspace.RepoPath, "CLAUDE.md")));
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveRecommendationListCanDeselectEverything()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(InteractiveRecommendationListCanDeselectEverything)).ConfigureAwait(true);
        var result = await RunInteractiveCommandAsync(
            workspace,
            Quote(workspace.RepoPath),
            async auto =>
            {
                await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("select which to apply:", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.LeftAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("[ ] dotnet-cli-run", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("__AGENTIC_DONE_", result.Screen, StringComparison.Ordinal);
        string agentsContent = File.Exists(Path.Combine(workspace.RepoPath, "AGENTS.md"))
            ? await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true)
            : string.Empty;
        Assert.DoesNotContain("dotnet-agentic-engineering:", agentsContent, StringComparison.Ordinal);
        Assert.DoesNotContain(" skill install ", await workspace.ReadGhLogAsync().ConfigureAwait(true), StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveSpecializeTargetDirectoryDeselectsScopedDuplicates()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(InteractiveSpecializeTargetDirectoryDeselectsScopedDuplicates)).ConfigureAwait(true);
        string backendPath = Path.Combine(workspace.RepoPath, "backend");
        _ = Directory.CreateDirectory(backendPath);
        workspace.WriteRepoFile(
            "AGENTS.md",
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            # foundation-prompt-log
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        workspace.WriteRepoFile(Path.Combine(".agents", "skills", "dotnet-livecharts2", "SKILL.md"), "# parent skill");
        workspace.WriteRepoFile(
            Path.Combine("backend", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = await RunInteractiveCommandAsync(
            workspace,
            $"--agents codex {Quote(backendPath)}",
            async auto =>
            {
                await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Tab to specialize target directory", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.TabAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("actions already present upwards / downwards", timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Duplicate(s) that prevent specialization:", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("../AGENTS.md", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync(Path.Combine("..", ".agents", "skills", "dotnet-livecharts2", "SKILL.md"), timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("actions already present upwards / downwards", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Duplicate(s) that prevent specialization:", result.Screen, StringComparison.Ordinal);
        string ghLog = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.DoesNotContain("dotnet-livecharts2", ghLog, StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveRecommendationListSupportsFilterAsYouTypeAndScrolling()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(
            nameof(InteractiveRecommendationListSupportsFilterAsYouTypeAndScrolling),
            writeDotnetProject: false).ConfigureAwait(true);
        WriteFullStackProjects(workspace);

        var result = await RunInteractiveCommandAsync(
            workspace,
            $"--agents codex {Quote(workspace.RepoPath)}",
            async auto =>
            {
                await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Showing 1-24 of", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                for (int index = 0; index < 30; index++)
                {
                    await auto.DownAsync().ConfigureAwait(true);
                }

                await auto.WaitUntilTextAsync("Showing 19-42 of", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);

                await auto.TypeAsync("t").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Filter: t", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.TypeAsync("e").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Filter: te", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.TypeAsync("s").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Filter: tes", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.TypeAsync("t").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Filter: test", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("uno-test-resize-app-window (install)", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("dotnet-test", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("run-tests (install)", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);

                using (var snapshot = auto.CreateSnapshot())
                {
                    string filteredScreen = snapshot.GetScreenText();
                    Assert.DoesNotContain("orleans-result-pattern (install)", filteredScreen, StringComparison.Ordinal);
                    Assert.DoesNotContain("uno-navigation-contentcontrol (install)", filteredScreen, StringComparison.Ordinal);
                }

                await auto.LeftAsync().ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(" skill install ", await workspace.ReadGhLogAsync().ConfigureAwait(true), StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveSelectingSkillSelectsAndInstallsDependencies()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(InteractiveSelectingSkillSelectsAndInstallsDependencies)).ConfigureAwait(true);
        var result = await RunInteractiveCommandAsync(
            workspace,
            $"--agents codex {Quote(workspace.RepoPath)}",
            async auto =>
            {
                await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.LeftAsync().ConfigureAwait(true);
                await auto.TypeAsync("run-tests").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Filter: run-tests", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("[ ] run-tests (install)", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.SpaceAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("[x] run-tests (install)", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        string ghLog = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        string agentsSkillsDirectory = Path.Combine(workspace.RepoPath, ".agents", "skills");
        Assert.Contains("skill install dotnet/skills plugins/dotnet-test/skills/filter-syntax --dir", ghLog, StringComparison.Ordinal);
        Assert.Contains("skill install dotnet/skills plugins/dotnet-test/skills/platform-detection --dir", ghLog, StringComparison.Ordinal);
        Assert.Contains("skill install dotnet/skills plugins/dotnet-test/skills/run-tests --dir", ghLog, StringComparison.Ordinal);
        Assert.Contains(".agents/skills", ghLog, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(agentsSkillsDirectory, "filter-syntax", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(agentsSkillsDirectory, "platform-detection", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(agentsSkillsDirectory, "run-tests", "SKILL.md")));
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task InteractiveSkillUpdatePromptUpdatesUniqueSkillsAcrossDirectories()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(InteractiveSkillUpdatePromptUpdatesUniqueSkillsAcrossDirectories)).ConfigureAwait(true);
        await workspace.SetSkillUpdateDryRunOutputAsync(DotnetUpdateOutput).ConfigureAwait(true);
        await workspace.SetSkillUpdateOutputAsync("Updated dotnet-livecharts2\n1 update(s) available:\n").ConfigureAwait(true);

        var result = await RunInteractiveCommandAsync(
            workspace,
            Quote(workspace.RepoPath),
            async auto =>
            {
                await auto.WaitUntilTextAsync("Recommend ", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.LeftAsync().ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Found 1 skill update(s) available:", timeout: TimeSpan.FromSeconds(45)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Update these skill(s)?", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.TypeAsync("y").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Found 1 skill update(s) available:", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Updated 1 skill(s) successfully.", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("Updated 2 skill(s) successfully.", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("1 update(s) available:", result.Screen, StringComparison.Ordinal);
        string ghLog = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.Contains(".claude/skills --all", ghLog, StringComparison.Ordinal);
        Assert.Contains(".agents/skills --all", ghLog, StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task GhSkillHelpFailureIsWarningInDryRunButFatalInNormalMode()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var dryRunWorkspace = await TestWorkspace.CreateAsync($"{nameof(GhSkillHelpFailureIsWarningInDryRunButFatalInNormalMode)}DryRun").ConfigureAwait(true);
        await dryRunWorkspace.SetGhSkillHelpFailureAsync().ConfigureAwait(true);
        var dryRun = await RunCommandAsync(dryRunWorkspace, $"--dry-run {Quote(dryRunWorkspace.RepoPath)}").ConfigureAwait(true);

        Assert.Equal(0, dryRun.ExitCode);
        Assert.Contains("Could not check target-local skills for updates", dryRun.Screen, StringComparison.Ordinal);
        Assert.Contains("Would install skills into skills directories:", dryRun.Screen, StringComparison.Ordinal);
        AssertRecordingWasWritten(dryRunWorkspace);

        using var normalWorkspace = await TestWorkspace.CreateAsync($"{nameof(GhSkillHelpFailureIsWarningInDryRunButFatalInNormalMode)}Normal").ConfigureAwait(true);
        await normalWorkspace.SetGhSkillHelpFailureAsync().ConfigureAwait(true);
        var normal = await RunCommandAsync(normalWorkspace, Quote(normalWorkspace.RepoPath), expectedExitCode: 2).ConfigureAwait(true);

        Assert.Equal(2, normal.ExitCode);
        Assert.Contains("Required tools are missing or too old", normal.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommend ", normal.Screen, StringComparison.Ordinal);
        AssertRecordingWasWritten(normalWorkspace);
    }

    static string ToolAssemblyPath => typeof(AgenticCheckCli).Assembly.Location;

    static void WriteFullStackProjects(TestWorkspace workspace)
    {
        workspace.WriteRepoFile(
            "src/UnoCSharp/App.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVVM;CSharpMarkup;Material</UnoFeatures>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Orleans.Server" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteRepoFile(
            "src/UnoCSharp2/App.csproj",
            """
            <Project Sdk="Uno.Sdk">
              <PropertyGroup>
                <UnoFeatures>MVUX;SimpleTheme</UnoFeatures>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="CSharpMarkup.WinUI" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteRepoFile(
            "src/Web/Web.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }

    async Task<TerminalRunResult> RunCommandAsync(
        TestWorkspace workspace,
        string arguments,
        int expectedExitCode = 0)
    {
        output.WriteLine($"Hex1b recording: {workspace.RecordingPath}");
        TerminalRunResult result;
        var terminal = CreateTerminal(workspace);
        await using (terminal.ConfigureAwait(true))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var runTask = terminal.RunAsync(cancellation.Token);
            var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

            try
            {
                result = await RunCommandInOpenTerminalAsync(auto, workspace, arguments, expectedExitCode).ConfigureAwait(true);
            }
            finally
            {
                await StopShellAsync(auto, runTask).ConfigureAwait(true);
            }
        }

        return result with { RecordingText = await workspace.ReadRecordingTextAsync().ConfigureAwait(true) };
    }

    async Task<TerminalRunResult> RunInteractiveCommandAsync(
        TestWorkspace workspace,
        string arguments,
        Func<Hex1bTerminalAutomator, Task> interact,
        int expectedExitCode = 0)
    {
        output.WriteLine($"Hex1b recording: {workspace.RecordingPath}");
        TerminalRunResult result;
        var terminal = CreateTerminal(workspace);
        await using (terminal.ConfigureAwait(true))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var runTask = terminal.RunAsync(cancellation.Token);
            var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

            try
            {
                string sentinel = CreateSentinel();
                await auto.TypeAsync($"{AgenticCheckCommand(workspace, arguments)}; printf '\\n{sentinel}:%s__\\n' \"$?\"").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
                await interact(auto).ConfigureAwait(true);
                await auto.WaitUntilTextAsync($"{sentinel}:{expectedExitCode}__", timeout: TimeSpan.FromSeconds(90)).ConfigureAwait(true);
                result = CreateResult(auto, $"{sentinel}:{expectedExitCode}__");
            }
            finally
            {
                await StopShellAsync(auto, runTask).ConfigureAwait(true);
            }
        }

        return result with { RecordingText = await workspace.ReadRecordingTextAsync().ConfigureAwait(true) };
    }

    static async Task<TerminalRunResult> RunCommandInOpenTerminalAsync(
        Hex1bTerminalAutomator auto,
        TestWorkspace workspace,
        string arguments,
        int expectedExitCode)
    {
        string sentinel = CreateSentinel();
        await auto.TypeAsync($"{AgenticCheckCommand(workspace, arguments)}; printf '\\n{sentinel}:%s__\\n' \"$?\"").ConfigureAwait(true);
        await auto.EnterAsync().ConfigureAwait(true);
        await auto.WaitUntilTextAsync($"{sentinel}:{expectedExitCode}__", timeout: TimeSpan.FromSeconds(90)).ConfigureAwait(true);
        return CreateResult(auto, $"{sentinel}:{expectedExitCode}__");
    }

    static string CreateSentinel()
        => $"__AGENTIC_DONE_{Guid.NewGuid():N}";

    static TerminalRunResult CreateResult(Hex1bTerminalAutomator auto, string sentinel)
    {
        using var snapshot = auto.CreateSnapshot();
        string screen = snapshot.GetScreenText();
        Assert.Contains(sentinel, screen, StringComparison.Ordinal);
        int exitCodeStart = sentinel.LastIndexOf(':') + 1;
        int exitCode = int.Parse(sentinel[exitCodeStart..^2], System.Globalization.CultureInfo.InvariantCulture);
        return new TerminalRunResult(exitCode, screen, string.Empty);
    }

    static string AgenticCheckCommand(TestWorkspace workspace, string arguments)
    {
        string path = workspace.BinPath + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        return $"AGENTIC_CHECK_GH_LOG={Quote(workspace.GhLogPath)} PATH={Quote(path)} dotnet {Quote(ToolAssemblyPath)} {arguments}";
    }

    static Hex1bTerminal CreateTerminal(TestWorkspace workspace)
    {
        string path = workspace.BinPath + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        return Hex1bTerminal.CreateBuilder()
            .WithHeadless()
            .WithDimensions(300, 260)
            .WithPtyProcess(options =>
            {
                options.FileName = "/bin/bash";
                options.Arguments = ["--noprofile", "--norc", "-i"];
                options.WorkingDirectory = workspace.RootPath;
                options.Environment = new Dictionary<string, string>
                {
                    ["AGENTIC_CHECK_GH_LOG"] = workspace.GhLogPath,
                    ["GH_PAGER"] = "cat",
                    ["PATH"] = path,
                    ["TERM"] = "xterm-256color"
                };
            })
            .WithAsciinemaRecording(
                workspace.RecordingPath,
                new AsciinemaRecorderOptions
                {
                    Title = "agentic-check end-to-end test",
                    Command = "agentic-check",
                    IdleTimeLimit = 1
                })
            .Build();
    }

    static void AssertRecordingWasWritten(TestWorkspace workspace)
    {
        FileInfo recording = new(workspace.RecordingPath);
        Assert.True(recording.Exists, $"Expected Hex1b recording to exist: {workspace.RecordingPath}");
        Assert.True(recording.Length > 0, $"Expected Hex1b recording to contain events: {workspace.RecordingPath}");
    }

    static async Task StopShellAsync(Hex1bTerminalAutomator auto, Task runTask)
    {
        try
        {
            await auto.TypeAsync("exit").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
            await runTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }
    }

    static string Quote(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    static bool IsUnsupportedPlatform()
        => OperatingSystem.IsWindows() || !File.Exists("/bin/bash");

    sealed record TerminalRunResult(int ExitCode, string Screen, string RecordingText);

    sealed class TestWorkspace : IDisposable
    {
        TestWorkspace(string rootPath, string testName)
        {
            RootPath = rootPath;
            RepoPath = Path.Combine(rootPath, "repo");
            BinPath = Path.Combine(rootPath, "bin");
            GhLogPath = Path.Combine(rootPath, "gh.log");
            RecordingPath = CreateRecordingPath(testName);
        }

        public string RootPath { get; }

        public string RepoPath { get; }

        public string BinPath { get; }

        public string GhLogPath { get; }

        public string RecordingPath { get; }

        public static async Task<TestWorkspace> CreateAsync(string testName, bool writeDotnetProject = true)
        {
            var workspace = new TestWorkspace(Path.Combine("/private/tmp", $"agentic-check-e2e-{Guid.NewGuid():N}"), testName);
            _ = Directory.CreateDirectory(workspace.RootPath);
            _ = Directory.CreateDirectory(workspace.RepoPath);
            _ = Directory.CreateDirectory(workspace.BinPath);
            await File.WriteAllTextAsync(workspace.GhLogPath, string.Empty).ConfigureAwait(true);
            if (writeDotnetProject)
            {
                workspace.WriteRepoFile(
                    "App.csproj",
                    """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                      </PropertyGroup>
                    </Project>
                    """);
            }

            await WriteFakeGhAsync(workspace).ConfigureAwait(true);
            await WriteFakeAgentProbeCommandsAsync(workspace).ConfigureAwait(true);
            return workspace;
        }

        public void WriteRepoFile(string relativePath, string content)
        {
            string fullPath = Path.Combine(RepoPath, relativePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        public void EnsureRepoDirectory(string relativePath)
            => Directory.CreateDirectory(Path.Combine(RepoPath, relativePath));

        public async Task<string> ReadRepoFileAsync(string relativePath)
            => await File.ReadAllTextAsync(Path.Combine(RepoPath, relativePath)).ConfigureAwait(true);

        public async Task<string> ReadGhLogAsync()
            => await File.ReadAllTextAsync(GhLogPath).ConfigureAwait(true);

        public async Task<string> ReadRecordingTextAsync()
            => File.Exists(RecordingPath)
                ? await File.ReadAllTextAsync(RecordingPath).ConfigureAwait(true)
                : string.Empty;

        public async Task SetSkillUpdateDryRunOutputAsync(string output)
            => await File.WriteAllTextAsync(Path.Combine(RootPath, "skill-update-dry-run.txt"), output).ConfigureAwait(true);

        public async Task SetSkillUpdateOutputAsync(string output)
            => await File.WriteAllTextAsync(Path.Combine(RootPath, "skill-update.txt"), output).ConfigureAwait(true);

        public async Task SetGhSkillHelpFailureAsync()
            => await File.WriteAllTextAsync(Path.Combine(RootPath, "gh-skill-help-fails"), string.Empty).ConfigureAwait(true);

        static string CreateRecordingPath(string testName)
        {
            string recordingDirectory = Path.Combine(GetProjectDirectory(), "TestResults", "recordings");
            _ = Directory.CreateDirectory(recordingDirectory);
            string safeTestName = string.Join(
                '_',
                testName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return Path.Combine(recordingDirectory, $"{safeTestName}-{Guid.NewGuid():N}.cast");
        }

        static string GetProjectDirectory()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Agentic.Check.LiveTests.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        static async Task WriteFakeGhAsync(TestWorkspace workspace)
        {
            string ghPath = Path.Combine(workspace.BinPath, "gh");
            await File.WriteAllTextAsync(
                ghPath,
                """
                #!/usr/bin/env bash
                set -euo pipefail

                root="$(cd "$(dirname "$0")/.." && pwd)"

                log_path="${AGENTIC_CHECK_GH_LOG:-$root/gh.log}"
                printf '%s\n' "$*" >> "$log_path"

                if [[ "${1:-}" == "--version" ]]; then
                  echo "gh version 2.93.0 (test)"
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "--help" ]]; then
                  if [[ -f "$root/gh-skill-help-fails" ]]; then
                    echo "unknown command \"skill\"" >&2
                    exit 1
                  fi
                  echo "gh skill help"
                  exit 0
                fi

                if [[ "${1:-}" == "skills" && "${2:-}" == "--help" ]]; then
                  if [[ -f "$root/gh-skill-help-fails" ]]; then
                    echo "unknown command \"skills\"" >&2
                    exit 1
                  fi
                  echo "gh skills help"
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "update" ]]; then
                  dry_run="false"
                  for arg in "$@"; do
                    if [[ "$arg" == "--dry-run" ]]; then
                      dry_run="true"
                    fi
                  done

                  if [[ "$dry_run" == "true" ]]; then
                    if [[ -f "$root/gh-skill-help-fails" ]]; then
                      echo "unknown command \"skill\"" >&2
                      exit 1
                    fi
                    if [[ -f "$root/skill-update-dry-run.txt" ]]; then
                      cat "$root/skill-update-dry-run.txt"
                    else
                      echo "No updates available."
                    fi
                    exit 0
                  fi

                  if [[ -f "$root/skill-update.txt" ]]; then
                    cat "$root/skill-update.txt"
                  fi
                  exit 0
                fi

                if [[ "${1:-}" == "skill" && "${2:-}" == "install" ]]; then
                  target_dir=""
                  for ((index = 1; index <= $#; index++)); do
                    if [[ "${!index}" == "--dir" ]]; then
                      next=$((index + 1))
                      target_dir="${!next}"
                    fi
                  done

                  if [[ -z "$target_dir" ]]; then
                    echo "missing --dir" >&2
                    exit 2
                  fi

                  skill_path="${4:-unknown}"
                  skill_without_ref="${skill_path%%@*}"
                  skill_name="$(basename "$skill_without_ref")"
                  mkdir -p "$target_dir/$skill_name"
                  printf '# Installed by fake gh\n' > "$target_dir/$skill_name/SKILL.md"
                  echo "Installed ${3:-unknown} ${4:-unknown}"
                  exit 0
                fi

                echo "Unexpected gh invocation: $*" >&2
                exit 2
                """).ConfigureAwait(true);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    ghPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
        }

        static async Task WriteFakeAgentProbeCommandsAsync(TestWorkspace workspace)
        {
            await WriteExecutableAsync(
                workspace,
                "claude",
                """
                #!/usr/bin/env bash
                if [[ "${1:-}" == "--help" ]]; then
                  echo "Claude Code test cli"
                  exit 0
                fi
                exit 2
                """).ConfigureAwait(true);
            await WriteExecutableAsync(
                workspace,
                "codex",
                """
                #!/usr/bin/env bash
                if [[ "${1:-}" == "--version" ]]; then
                  echo "codex 0.0.0-test"
                  exit 0
                fi
                exit 2
                """).ConfigureAwait(true);

            foreach (string command in new[] { "copilot", "gemini", "crush", "goose", "opencode", "qwen" })
            {
                await WriteExecutableAsync(
                    workspace,
                    command,
                    """
                    #!/usr/bin/env bash
                    exit 127
                    """).ConfigureAwait(true);
            }
        }

        static async Task WriteExecutableAsync(TestWorkspace workspace, string name, string content)
        {
            string path = Path.Combine(workspace.BinPath, name);
            await File.WriteAllTextAsync(path, content).ConfigureAwait(true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
        }
    }
}
