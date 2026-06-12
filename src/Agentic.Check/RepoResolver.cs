namespace Agentic.Check;

sealed class RepoResolver(ICommandRunner commandRunner, IUserPrompts prompts, IReporter reporter)
{
    public async Task<RepoResolution> ResolveAsync(string targetDirectory, bool dryRun, CancellationToken cancellationToken)
    {
        string fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var rootResult = await commandRunner.RunAsync(
            "git",
            ["rev-parse", "--show-toplevel"],
            fullTargetDirectory,
            cancellationToken).ConfigureAwait(false);

        if (rootResult.Success && !string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            string repoRoot = rootResult.StandardOutput.Trim();
            reporter.Info($"Repository root: {repoRoot}");
            return new RepoResolution(repoRoot, true, []);
        }

        if (dryRun)
        {
            return new RepoResolution(fullTargetDirectory, true, [$"Would run git init in {fullTargetDirectory}."]);
        }

        bool initialize = await prompts.ConfirmAsync($"No git repo found. Run git init in {fullTargetDirectory}?", false, cancellationToken)
            .ConfigureAwait(false);
        if (!initialize)
        {
            return new RepoResolution(fullTargetDirectory, false, ["Skipped git init."]);
        }

        var initResult = await commandRunner.RunAsync("git", ["init"], fullTargetDirectory, cancellationToken).ConfigureAwait(false);
        return !initResult.Success
            ? new RepoResolution(fullTargetDirectory, false, [$"git init failed: {initResult.StandardError.Trim()}"])
            : new RepoResolution(fullTargetDirectory, true, [$"Ran git init in {fullTargetDirectory}."]);
    }
}

sealed record RepoResolution(string RepoRoot, bool CanProceed, IReadOnlyList<string> Actions);
