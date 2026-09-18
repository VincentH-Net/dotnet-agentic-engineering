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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "EndToEnd")]
    public async Task MissingAuthenticationContinuesWithoutLoginPrompt(bool dryRun)
    {
        if (IsUnsupportedPlatform())
            return;
        using var workspace = await TestWorkspace.CreateAsync(nameof(MissingAuthenticationContinuesWithoutLoginPrompt)).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, "gh-auth-missing"), string.Empty).ConfigureAwait(true);
        workspace.WriteRepoFile("AGENTS.md", "Preserve these user instructions.\n");
        string before = await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true);
        string reportPath = Path.Combine(workspace.RootPath, "auth-report.json");
        var result = await RunCommandAsync(workspace, $"{(dryRun ? "--dry-run" : "--yes")} --report {Quote(reportPath)} {Quote(workspace.RepoPath)}").ConfigureAwait(true);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("gh auth login", result.Screen, StringComparison.Ordinal);
        string after = await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true);
        Assert.StartsWith(before, after, StringComparison.Ordinal);
        Assert.Equal(!dryRun, after.Contains("foundation-prompt-log:start", StringComparison.Ordinal));
        string log = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.Equal(!dryRun, log.Contains("skill install", StringComparison.Ordinal));
        Assert.Contains("skill update", log, StringComparison.Ordinal);
        Assert.DoesNotContain("api --hostname", log, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-authentication-secret", await File.ReadAllTextAsync(reportPath).ConfigureAwait(true), StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "EndToEnd")]
    public async Task CompanionFailureSkipsDependentDirectiveAndReportsPartialOutcome(bool invalidVersion)
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(CompanionFailureSkipsDependentDirectiveAndReportsPartialOutcome), writeDotnetProject: false).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, invalidVersion ? "tool-invalid-version" : "tool-failure"), string.Empty).ConfigureAwait(true);
        string reportPath = Path.Combine(workspace.RootPath, "report.json");
        var result = await RunCommandAsync(workspace, $"--yes --agents codex --report {Quote(reportPath)} {Quote(workspace.RepoPath)}", 1).ConfigureAwait(true);
        Assert.Contains("Dependent actions skipped", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("foundation-prompt-log:start", await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true), StringComparison.Ordinal);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(true));
        Assert.False(report.RootElement.GetProperty("companion").GetProperty("success").GetBoolean());
        Assert.Equal(invalidVersion, report.RootElement.GetProperty("companion").GetProperty("changed").GetBoolean());
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DeselectingCompanionDeselectsPromptDirectiveWithoutInstallation()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(DeselectingCompanionDeselectsPromptDirectiveWithoutInstallation), writeDotnetProject: false).ConfigureAwait(true);
        _ = await RunInteractiveCommandAsync(workspace, $"--agents codex {Quote(workspace.RepoPath)}", async auto =>
        {
            await auto.WaitUntilTextAsync("InnoWvate.Agentic (install/update)").ConfigureAwait(true);
            await auto.TypeAsync("InnoWvate.Agentic").ConfigureAwait(true);
            await auto.WaitUntilTextAsync("Filter: InnoWvate.Agentic").ConfigureAwait(true);
            await auto.SpaceAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[ ] InnoWvate.Agentic").ConfigureAwait(true);
            await auto.EscapeAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[ ] foundation-prompt-log").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        Assert.DoesNotContain(await File.ReadAllLinesAsync(Path.Combine(workspace.RootPath, "tool.log")).ConfigureAwait(true), line => !line.StartsWith("tool list ", StringComparison.Ordinal));
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(workspace.RepoPath)));
        Assert.DoesNotContain("foundation-prompt-log:start", await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true), StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DirectiveSelectionIncludesCompanionAndDryRunDoesNotInstall()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(DirectiveSelectionIncludesCompanionAndDryRunDoesNotInstall), writeDotnetProject: false).ConfigureAwait(true);
        var dry = await RunCommandAsync(workspace, $"--dry-run --agents codex {Quote(workspace.RepoPath)}").ConfigureAwait(true);
        Assert.Contains("required 2.3, pattern 2.*", dry.Screen, StringComparison.Ordinal);
        Assert.False(File.Exists(CompanionInstaller.ManifestPath(workspace.RepoPath)));
        Assert.DoesNotContain(await File.ReadAllLinesAsync(Path.Combine(workspace.RootPath, "tool.log")).ConfigureAwait(true), line => !line.StartsWith("tool list ", StringComparison.Ordinal));
        _ = await RunInteractiveCommandAsync(workspace, $"--agents codex {Quote(workspace.RepoPath)}", async auto =>
        {
            await auto.WaitUntilTextAsync("InnoWvate.Agentic (install/update)").ConfigureAwait(true);
            await auto.LeftAsync().ConfigureAwait(true);
            await auto.TypeAsync("foundation-prompt-log").ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[ ] foundation-prompt-log").ConfigureAwait(true);
            await auto.SpaceAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[x] foundation-prompt-log").ConfigureAwait(true);
            await auto.EscapeAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[x] InnoWvate.Agentic").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        Assert.Contains("foundation-prompt-log:start", await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(workspace.RepoPath));
        _ = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(workspace.RootPath, "tool.log")).ConfigureAwait(true), line => line.Contains("InnoWvate.Agentic", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "dna-installed")));
        AssertRecordingWasWritten(workspace);
    }

    [SkippableFact]
    [Trait("Category", "EndToEnd")]
    public async Task ShorthandCanBeDeselectedWithoutRemovingCompanion()
    {
        Skip.If(IsUnsupportedPlatform(), "Terminal interaction requires Bash on macOS/Linux.");
        using var workspace = await TestWorkspace.CreateAsync(nameof(ShorthandCanBeDeselectedWithoutRemovingCompanion), writeDotnetProject: false).ConfigureAwait(true);
        _ = await RunInteractiveCommandAsync(workspace, $"--agents codex {Quote(workspace.RepoPath)}", async auto =>
        {
            await auto.WaitUntilTextAsync("shorthand for").ConfigureAwait(true);
            await auto.TypeAsync("shorthand").ConfigureAwait(true);
            await auto.WaitUntilTextAsync("Filter: shorthand").ConfigureAwait(true);
            await auto.SpaceAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[ ] `dna`").ConfigureAwait(true);
            await auto.EscapeAsync().ConfigureAwait(true);
            await auto.WaitUntilTextAsync("[x] InnoWvate.Agentic").ConfigureAwait(true);
            await auto.EnterAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(workspace.RepoPath));
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, "dna-installed")));
        Assert.Contains("foundation-prompt-log:start", await workspace.ReadRepoFileAsync("AGENTS.md").ConfigureAwait(true), StringComparison.Ordinal);
        AssertRecordingWasWritten(workspace);
    }

    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Category", "EndToEnd")]
    public async Task ShorthandCollisionRequiresExplicitConsent(bool consent, bool unattended)
    {
        Skip.If(IsUnsupportedPlatform(), "Terminal interaction requires Bash on macOS/Linux.");
        using var workspace = await TestWorkspace.CreateAsync(nameof(ShorthandCollisionRequiresExplicitConsent), writeDotnetProject: false).ConfigureAwait(true);
        string unknown = Path.Combine(workspace.BinPath, "dna");
        await File.WriteAllTextAsync(unknown, "#!/bin/sh\ntouch " + Quote(Path.Combine(workspace.RootPath, "unknown-executed")) + "\n").ConfigureAwait(true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(unknown, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string reportPath = Path.Combine(workspace.RootPath, "dna-report.json");
        string arguments = $"--agents codex --report {Quote(reportPath)} {Quote(workspace.RepoPath)}";
        if (unattended)
        {
            _ = await RunCommandAsync(workspace, "--yes " + arguments).ConfigureAwait(true);
        }
        else
        {
            _ = await RunInteractiveCommandAsync(workspace, arguments, async auto =>
            {
                await auto.WaitUntilTextAsync("shorthand for").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Install anyway?").ConfigureAwait(true);
                await auto.TypeAsync(consent ? "y" : "n").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(true));
        var dna = report.RootElement.GetProperty("dna");
        Assert.Equal(!consent, dna.GetProperty("skipped").GetBoolean());
        Assert.Equal(unknown, dna.GetProperty("conflicts")[0].GetString());
        Assert.Equal(consent, File.Exists(Path.Combine(workspace.RootPath, "dna-installed")));
        Assert.Equal("2.3.0", CompanionInstaller.InstalledVersion(workspace.RepoPath));
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, "unknown-executed")));
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task HelpListsCoreOptionsAndAgentValues()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(HelpListsCoreOptionsAndAgentValues)).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, "gh-auth-missing"), string.Empty).ConfigureAwait(true);
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
        Assert.Empty(await workspace.ReadGhLogAsync().ConfigureAwait(true));
        AssertRecordingWasWritten(workspace);
    }

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task VersionRequiresNoGitHubAuthentication()
    {
        if (IsUnsupportedPlatform())
            return;
        using var workspace = await TestWorkspace.CreateAsync(nameof(VersionRequiresNoGitHubAuthentication)).ConfigureAwait(true);
        await File.WriteAllTextAsync(Path.Combine(workspace.RootPath, "gh-auth-missing"), string.Empty).ConfigureAwait(true);
        var result = await RunCommandAsync(workspace, "--version").ConfigureAwait(true);
        Assert.Equal(0, result.ExitCode);
        string version = typeof(AgenticCheckReport).Assembly.GetName().Version!.ToString(3);
        Assert.Contains(version, result.Screen, StringComparison.Ordinal);
        Assert.Empty(await workspace.ReadGhLogAsync().ConfigureAwait(true));
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

                var invalidAgent = await RunCommandInOpenTerminalAsync(auto, workspace, $"--dry-run --agents standard {Quote(workspace.RepoPath)}", 1).ConfigureAwait(true);
                Assert.Contains("Unknown agent value(s): standard", invalidAgent.Screen, StringComparison.Ordinal);

                var conflictingOptions = await RunCommandInOpenTerminalAsync(
                    auto,
                    workspace,
                    $"--dry-run --skills-dir {Quote(Path.Combine(workspace.RepoPath, "custom-skills"))} --agents codex {Quote(workspace.RepoPath)}",
                    1).ConfigureAwait(true);
                Assert.Contains("Specify no more than one of --skills-dir and --agents.", conflictingOptions.Screen, StringComparison.Ordinal);

                var fileTarget = await RunCommandInOpenTerminalAsync(auto, workspace, Quote(Path.Combine(workspace.RepoPath, "target-file")), 2).ConfigureAwait(true);
                Assert.Contains("Invalid target directory", fileTarget.Screen, StringComparison.Ordinal);
                Assert.Contains("is a file", fileTarget.Screen, StringComparison.Ordinal);
            }
            finally
            {
                await StopShellAsync(auto, runTask).ConfigureAwait(true);
            }
        }

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
        Assert.Contains("Source channel", result.Screen, StringComparison.Ordinal);
        Assert.Contains("Stable", result.Screen, StringComparison.Ordinal);
        Assert.DoesNotContain("no skills update check", result.Screen, StringComparison.Ordinal);
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
            Path.Combine("backend", "api", "AGENTS.md"),
            """
            <!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
            # foundation-prompt-log
            <!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
            """);
        workspace.WriteRepoFile(Path.Combine("backend", "api", ".agents", "skills", "dotnet-livecharts2", "SKILL.md"), "# descendant skill");
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
                await auto.WaitUntilTextAsync("Target directory specialization: ON", timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Tab to toggle", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("actions already present above / below", timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Duplicate(s) that prevent specialization:", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("../AGENTS.md", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync(Path.Combine("api", "AGENTS.md"), timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync(Path.Combine("..", ".agents", "skills", "dotnet-livecharts2", "SKILL.md"), timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync(Path.Combine("api", ".agents", "skills", "dotnet-livecharts2", "SKILL.md"), timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Target directory specialization: ON", result.Screen, StringComparison.Ordinal);
        Assert.Contains("actions already present above / below", result.Screen, StringComparison.Ordinal);
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
        var normal = await RunInteractiveCommandAsync(
            normalWorkspace,
            Quote(normalWorkspace.RepoPath),
            async auto =>
            {
                await auto.WaitUntilTextAsync("F1 to learn more at https://github.com/VincentH-Net/dotnet-agentic-engineering", timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("F2 to open https://cli.github.com/ for how to install GitHub CLI", timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                await auto.WaitUntilTextAsync("Press any other key to exit", timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            },
            expectedExitCode: 2).ConfigureAwait(true);

        Assert.Equal(2, normal.ExitCode);
        Assert.Contains("GitHub CLI is missing or too old", normal.Screen, StringComparison.Ordinal);
        Assert.Contains("F2 to open https://cli.github.com/ for how to install GitHub CLI", normal.Screen, StringComparison.Ordinal);
        Assert.Contains("Press any other key to exit", normal.Screen, StringComparison.Ordinal);
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

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task PreviewDisplaysSourceChannelPresenceAndReinstallActions()
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync(nameof(PreviewDisplaysSourceChannelPresenceAndReinstallActions)).ConfigureAwait(true);
        const string installed = "---\nname: dotnet-livecharts2\n---\nExisting skill\n";
        workspace.WriteRepoFile(".agents/skills/dotnet-livecharts2/SKILL.md", installed);

        var result = await RunInteractiveCommandAsync(workspace,
            $"--preview --agents codex {Quote(workspace.RepoPath)}",
            async auto =>
            {
                await auto.WaitUntilTextAsync("dotnet-livecharts2 (re-install)").ConfigureAwait(true);
                await auto.WaitUntilTextAsync("dotnet-modern-csharp-editorconfig (install)").ConfigureAwait(true);
                using (var snapshot = auto.CreateSnapshot())
                {
                    string screen = snapshot.GetScreenText();
                    int channel = screen.IndexOf("Source channel", StringComparison.Ordinal);
                    int directives = screen.IndexOf("Recommended directives", StringComparison.Ordinal);
                    Assert.True(channel >= 0 && directives > channel);
                    Assert.Contains("Preview", screen[channel..directives], StringComparison.Ordinal);
                    Assert.Contains("* no skills update check - always (re)installs", screen[channel..directives], StringComparison.Ordinal);
                    Assert.Contains("12 missing, 1 installed", screen, StringComparison.Ordinal);
                    Assert.DoesNotContain("12 missing, 1 up to date", screen, StringComparison.Ordinal);
                }

                await auto.LeftAsync().ConfigureAwait(true);
                await auto.WaitUntilTextAsync("[ ] dotnet-livecharts2 (re-install)").ConfigureAwait(true);
                await auto.EnterAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        Assert.Contains("No actions selected", result.Screen, StringComparison.Ordinal);
        string log = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.DoesNotContain("skill update", log, StringComparison.Ordinal);
        Assert.DoesNotContain("skill install", log, StringComparison.Ordinal);
        Assert.Equal(installed, await File.ReadAllTextAsync(Path.Combine(workspace.RepoPath, ".agents/skills/dotnet-livecharts2/SKILL.md")).ConfigureAwait(true));
        Assert.False(File.Exists(Path.Combine(workspace.RepoPath, ".agents/skills/dotnet-modern-csharp-editorconfig/SKILL.md")));
        AssertRecordingWasWritten(workspace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "EndToEnd")]
    public async Task StableAlreadyCurrentSkillsDoNotReinstall(bool defaultBranch)
    {
        if (IsUnsupportedPlatform())
        {
            return;
        }

        using var workspace = await TestWorkspace.CreateAsync($"{nameof(StableAlreadyCurrentSkillsDoNotReinstall)}-{defaultBranch}").ConfigureAwait(true);
        if (defaultBranch)
        {
            DirectiveHttpCache cache = new(new(3600, Path.Combine(workspace.RootPath, "cache"), []), null);
            foreach (string repo in StaticSkillManifest.All.Select(skill => skill.SourceRepo).Distinct(StringComparer.Ordinal))
                cache.TryWrite(new($"https://api.github.com/repos/{repo}/releases/latest"), "__agentic_check_no_latest_release__");
        }

        string reference = defaultBranch ? "refs/heads/main" : "refs/tags/v2.3.0";
        string installed = $"---\nmetadata:\n  github-ref: {reference}\n  github-tree-sha: unchanged-tree\n---\nExisting skill\n";
        List<string> paths = [];
        foreach (string agent in new[] { ".agents", ".claude" })
        {
            foreach (var skill in StaticSkillManifest.All.Where(skill => skill.Technology == TechnologyNames.Dotnet && skill.GateRequirements.Count == 0))
            {
                string path = $"{agent}/skills/{skill.LocalFolder}/SKILL.md";
                workspace.WriteRepoFile(path, installed);
                paths.Add(path);
            }
        }

        string reportPath = Path.Combine(workspace.RootPath, "report.json");
        var result = await RunCommandAsync(workspace,
            $"--yes --agents codex,claude-code --report {Quote(reportPath)} {Quote(workspace.RepoPath)}").ConfigureAwait(true);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(true));
        Assert.Empty(report.RootElement.GetProperty("missingSkills").EnumerateArray());
        Assert.Equal(0, report.RootElement.GetProperty("outdatedSkills").GetInt32());
        Assert.Empty(report.RootElement.GetProperty("installResults").EnumerateArray());
        Assert.Empty(report.RootElement.GetProperty("skillCopyResults").EnumerateArray());
        Assert.DoesNotContain("Switch to stable skill", result.Screen, StringComparison.Ordinal);
        string log = await workspace.ReadGhLogAsync().ConfigureAwait(true);
        Assert.DoesNotContain("skill install", log, StringComparison.Ordinal);
        string[] updateCalls = [.. log.Split('\n').Where(line => line.StartsWith("skill update", StringComparison.Ordinal))];
        Assert.Equal(2, updateCalls.Length);
        Assert.All(updateCalls, line => Assert.Contains("--dry-run", line, StringComparison.Ordinal));
        foreach (string path in paths)
            Assert.Equal(installed, await workspace.ReadRepoFileAsync(path).ConfigureAwait(true));
        AssertRecordingWasWritten(workspace);
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
        // Hex1b 0.165.0 ignores the PTY environment overrides on Unix. Set TERM at the
        // actual command boundary so a headless parent's TERM=dumb cannot disable redraws.
        return $"TERM=xterm-256color GH_TOKEN= GITHUB_TOKEN= GH_CONFIG_DIR={Quote(Path.Combine(workspace.RootPath, "gh"))} AGENTIC_CHECK_CACHE_SECONDS=3600 AGENTIC_CHECK_CACHE_DIR={Quote(Path.Combine(workspace.RootPath, "cache"))} AGENTIC_CHECK_GH_LOG={Quote(workspace.GhLogPath)} PATH={Quote(path)} {Quote(Path.ChangeExtension(ToolAssemblyPath, null))} {arguments}";
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
            var workspace = new TestWorkspace(Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), $"agentic-check-e2e-{Guid.NewGuid():N}"), testName);
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

            SeedSourceCache(workspace);
            await WriteFakeDotnetAsync(workspace).ConfigureAwait(true);
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

        static void SeedSourceCache(TestWorkspace workspace)
        {
            DirectiveHttpCache cache = new(new(3600, Path.Combine(workspace.RootPath, "cache"), []), null);
            foreach (string repo in StaticSkillManifest.All.Concat(StaticSkillManifest.Preview).Select(skill => skill.SourceRepo).Distinct(StringComparer.Ordinal))
            {
                string api = "https://api.github.com/repos/" + repo;
                cache.TryWrite(new(api + "/releases/latest"), "{\"tag_name\":\"v2.3.0\",\"published_at\":\"2026-09-01T00:00:00Z\"}");
                cache.TryWrite(new(api), "{\"default_branch\":\"main\"}");
                cache.TryWrite(new(api + "/branches/main"), "{\"commit\":{\"commit\":{\"committer\":{\"date\":\"2026-09-01T00:00:00Z\"}}}}");
            }

            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "directives")))
            {
                directory = directory.Parent;
            }

            string checkout = directory?.FullName ?? throw new InvalidOperationException("Missing source checkout.");
            foreach (string sourceRef in new[] { "v2.3.0", "main" })
            {
                List<object> listing = [];
                foreach (string file in Directory.EnumerateFiles(Path.Combine(checkout, "directives"), "*.md"))
                {
                    string name = Path.GetFileName(file);
                    string url = $"https://raw.githubusercontent.com/{CompanionDependency.SourceRepo}/{sourceRef}/directives/{name}";
                    listing.Add(new { name, download_url = url, type = "file" });
                    cache.TryWrite(new(url), File.ReadAllText(file));
                }

                cache.TryWrite(new(DirectiveInstallerUrl.Listing(sourceRef)), JsonSerializer.Serialize(listing));
                cache.TryWrite(new($"https://raw.githubusercontent.com/{CompanionDependency.SourceRepo}/{sourceRef}/{CompanionSourceVersionReader.ProjectPath}"), File.ReadAllText(Path.Combine(checkout, CompanionSourceVersionReader.ProjectPath)));
            }
        }

        static async Task WriteFakeDotnetAsync(TestWorkspace workspace)
        {
            string realDotnet = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../../dotnet");
            string script = """
                #!/usr/bin/env python3
                import json, os, pathlib, sys
                root = pathlib.Path(__file__).resolve().parent.parent
                args = sys.argv[1:]
                if not args or args[0] != 'tool':
                    os.execv(REAL_DOTNET, [REAL_DOTNET] + args)
                with (root / 'tool.log').open('a') as log:
                    log.write(' '.join(args) + '\n')
                if args[1] == 'list' and '--global' in args:
                    installed = (root / 'dna-installed').exists()
                    print(json.dumps({'version': 1, 'data': [{'packageId': 'InnoWvate.Dna', 'version': '1.0.0', 'commands': ['dna']}] if installed else []}))
                    sys.exit(0)
                if (root / 'tool-failure').exists():
                    print('fixture SDK installation failure', file=sys.stderr)
                    sys.exit(1)
                if args[1] == 'run':
                    print('2.3.0')
                    sys.exit(0)
                if '--global' in args:
                    assert 'InnoWvate.Dna' in args
                    (root / 'dna-installed').write_text('1.0.0')
                    sys.exit(0)
                manifest = pathlib.Path(args[args.index('--tool-manifest') + 1])
                data = json.loads(manifest.read_text())
                if args[1] in ('install', 'update'):
                    version = '2.2.0' if (root / 'tool-invalid-version').exists() else '2.3.0'
                    data['tools']['innowvate.agentic'] = {'version': version, 'commands': ['agentic']}
                    manifest.write_text(json.dumps(data))
                sys.exit(0)
                """;
            script = script.Replace("REAL_DOTNET", JsonSerializer.Serialize(Path.GetFullPath(realDotnet)), StringComparison.Ordinal);
            await WriteExecutableAsync(workspace, "dotnet", script).ConfigureAwait(true);
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

                if [[ -f "$root/gh-auth-missing" && ( -n "${GH_TOKEN:-}" || -n "${GITHUB_TOKEN:-}" ) ]]; then
                  echo "Unexpected credential in anonymous test" >&2
                  exit 88
                fi

                if [[ "${1:-}" == "--version" ]]; then
                  echo "gh version 2.93.0 (test)"
                  exit 0
                fi

                if [[ "${1:-}" == "auth" && "${2:-}" == "token" ]]; then
                  if [[ -f "$root/gh-auth-missing" ]]; then
                    echo "no token" >&2
                    exit 1
                  fi
                  echo "fixture-authentication-secret"
                  exit 0
                fi

                if [[ "${1:-}" == "api" ]]; then
                  if [[ "${GH_TOKEN:-}" != "fixture-authentication-secret" ]]; then
                    echo "HTTP/2.0 401 Unauthorized"
                    exit 1
                  fi
                  printf 'HTTP/2.0 200 OK\r\nX-RateLimit-Limit: 5000\r\n\r\n{}\n'
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
