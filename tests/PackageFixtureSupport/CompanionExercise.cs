namespace Agentic.PackageFixtures;

static class CompanionExercise
{
    internal static async Task RunAsync(FixtureWorkspace workspace, PackageArtifact companion)
    {
        string cached = Directory.GetFiles(workspace.Environment["NUGET_PACKAGES"], "*.nupkg", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(path).Equals($"{companion.Id}.{companion.Version}.nupkg", StringComparison.OrdinalIgnoreCase));
        FixtureFiles.Require(FixtureFiles.Hash(cached) == companion.Sha256, "SDK installed different companion package bytes.");
        string version = await workspace.Process.SuccessAsync("dotnet", ["tool", "run", "agentic", "--", "--version"], workspace.Target).ConfigureAwait(false);
        FixtureFiles.Require(version.Split('+')[0] == companion.Version.Split('+')[0], $"Wrong companion executable: {version}");
        string help = await workspace.Process.SuccessAsync("dotnet", ["agentic", "--help"], workspace.Target).ConfigureAwait(false);
        FixtureFiles.Require(help.Contains("prompt-log", StringComparison.Ordinal), "Companion help lacks prompt-log.");
        const string body = "Literal [red] text, quotes \" 漢字 😀\n\nprompt-log-end:\n\\prompt-log-end:\n";
        string minimum = string.Join('.', companion.Version.Split('.')[..2]);
        var filesBefore = FixtureFiles.Inventory(workspace.Target);
        string refsBefore = await workspace.Process.SuccessAsync("git", ["for-each-ref", "--format=%(refname) %(objectname)"], workspace.Target).ConfigureAwait(false);
        var wrapped = await workspace.Process.RunAsync("dotnet", ["agentic", "prompt-log", "wrap", "--input", "-", "-m", minimum], workspace.Target, body).ConfigureAwait(false);
        wrapped.RequireSuccess("packaged prompt-log wrap");
        const string expected = "prompt-log:\nprompt-log-format: raw-v1\nLiteral [red] text, quotes \" 漢字 😀\n\n\\prompt-log-end:\n\\\\prompt-log-end:\n\nprompt-log-end:\n";
        FixtureFiles.Require(wrapped.Output == expected, "Companion raw framing/escaping differs from the exact stdin contract.");
        FixtureFiles.Require(FixtureFiles.EqualInventory(filesBefore, FixtureFiles.Inventory(workspace.Target)), "Wrap wrote target files.");
        FixtureFiles.Require(refsBefore == await workspace.Process.SuccessAsync("git", ["for-each-ref", "--format=%(refname) %(objectname)"], workspace.Target).ConfigureAwait(false), "Wrap mutated Git history.");
        _ = await workspace.Process.SuccessAsync("git", ["commit", "--allow-empty", "--cleanup=verbatim", "--file", "-"], workspace.Target, "Fixture round trip\n\n" + wrapped.Output).ConfigureAwait(false);
        string head = await workspace.Process.SuccessAsync("git", ["rev-parse", "HEAD"], workspace.Target).ConfigureAwait(false);
        var shown = await workspace.Process.RunAsync("dotnet", ["agentic", "prompt-log", "show", "-m", minimum], workspace.Target).ConfigureAwait(false);
        shown.RequireSuccess("packaged prompt-log show");
        string identity = await workspace.Process.SuccessAsync("git", ["show", "-s", "--format=%H %cI"], workspace.Target).ConfigureAwait(false);
        FixtureFiles.Require(shown.Output.EndsWith(identity + ": valid prompt log" + Environment.NewLine + body + Environment.NewLine + Environment.NewLine, StringComparison.Ordinal), "Companion failed to round-trip the exact latest commit and stdin body.");
        _ = await workspace.Process.SuccessAsync("dotnet", ["agentic", "prompt-log", "check", "-m", minimum], workspace.Target).ConfigureAwait(false);
        FixtureFiles.Require(head == await workspace.Process.SuccessAsync("git", ["rev-parse", "HEAD"], workspace.Target).ConfigureAwait(false), "Companion mutated Git history.");
    }

}
