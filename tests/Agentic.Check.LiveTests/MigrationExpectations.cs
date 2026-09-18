using Agentic.PackageFixtures;

namespace Agentic.Check.LiveTests;

sealed record MigrationExpectations(string[] Technologies, Dictionary<string, string[]> Gates)
{
    // Historical reports describe the old installer. Candidate expectations are reviewed
    // separately and never inferred from the candidate's detector or fresh trigger files.
    internal static FixtureDefinition ForBaseline(string baselineId, FixtureDefinition historical)
    {
        FixtureFiles.Require(Path.GetFileName(baselineId) == baselineId, "Invalid baseline ID.");
        string path = Path.Combine(FixtureFiles.Checkout, "tests/fixtures/migration-expectations", baselineId + ".json");
        if (!File.Exists(path))
            return historical;
        var expectations = FixtureFiles.ReadJson<Dictionary<string, MigrationExpectations>>(path);
        return expectations.TryGetValue(historical.Name, out var expected)
            ? historical with { Technologies = expected.Technologies, Gates = expected.Gates }
            : historical;
    }
}
