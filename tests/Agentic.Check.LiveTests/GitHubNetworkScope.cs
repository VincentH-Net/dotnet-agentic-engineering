using System.Diagnostics.CodeAnalysis;
using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

[SuppressMessage("Maintainability", "CA1515", Justification = "xUnit requires public collection definitions; this small fixture API has no maintainability cost. See xUnit1027 and the CA1515 suppression guidance.")]
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitHubNetworkScope : ICollectionFixture<GitHubNetworkLifetime>
{
    public const string Name = "Run-scoped GitHub network";
}

[SuppressMessage("Maintainability", "CA1515", Justification = "The public xUnit collection definition exposes this fixture type; making it internal is not compatible with that required API.")]
public sealed class GitHubNetworkLifetime : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    // xUnit reports cleanup failures as run failures, even if every individual scenario passed.
    public Task DisposeAsync() => GitHubFixtureRun.Shared.CompleteAsync();
}
