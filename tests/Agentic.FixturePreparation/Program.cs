using Agentic.PackageFixtures;

try
{
    if (args is ["prepare", var definition, var baselineId])
        return await BaselinePreparation.PrepareAsync(Path.GetFullPath(definition), baselineId).ConfigureAwait(false);
    if (args is ["resume-incomplete", var resumeDefinition, var resumeId])
        return await BaselinePreparation.PrepareAsync(Path.GetFullPath(resumeDefinition), resumeId, true).ConfigureAwait(false);
    if (args is ["pack-candidates", var output, var configuration])
    {
        await CandidateInputs.PackAsync(output, configuration).ConfigureAwait(false);
        return 0;
    }
    await Console.Error.WriteLineAsync("Usage: Agentic.FixturePreparation <prepare|resume-incomplete> <baseline-definition.json> <agentic-check-VERSION-YYYY-MM-DD-SUFFIX>\n       Agentic.FixturePreparation pack-candidates <new-output-directory> <Release|Debug>").ConfigureAwait(false);
    return 2;
}
finally
{
    await GitHubFixtureRun.Shared.CompleteAsync().ConfigureAwait(false);
}
