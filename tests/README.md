# Full-suite runner

Run the complete suite with the C# runner:

```sh
dotnet run --file tests/run-full-suite.cs
```

The runner resolves the checkout from its own file location, so an absolute script path also works
from another directory. `--help` describes usage. The C# runner uses .NET 10 file-based app
support and only the standard library.

It follows this fixed sequence:

1. Build the existing fixture-preparation helper in Release. Invoke its `pack-candidates`
   command, which validates the expected origin, non-default development branch, pushed HEAD,
   and clean production/content inputs before packing both Release candidates. It records
   package hashes and source provenance and checks source readiness again after packing.
2. Build all four test projects in Release, then run all four without rebuilding, in this order:
   `Agentic.Tests`, `Agentic.Check.Tests`, `Agentic.LiveTests`, `Agentic.Check.LiveTests`.
   Enable both network and maintenance tests, pass the exact newly packed candidates, and
   remove inherited fixture/scenario selectors. Test failures do not prevent subsequent
   test projects from running.
3. Return zero only if every build, packaging operation, and test command succeeds. Setup/build
   failures stop the run; tests retain their documented genuine skip behavior.

The default persisted baseline is `agentic-check-2.2.0-2026-09-14-capture02`.
Set `AGENTIC_E2E_BASELINE` explicitly to select a later collection. Baselines are never prepared
or modified by the runner. Normal `dotnet test` defaults are unchanged.

Tools and authentication are checked by the commands and existing test setup that need them.
The runner does not add separate prerequisite probes, commit/push source, publish packages,
retry with different settings, run preliminary smoke tests, or change SDK/workload settings.
Other ambient SDK/authentication settings remain inherited. A blocked command must be
diagnosed as a failed or incomplete standard run, not silently worked around.

Each invocation creates a new ignored `tests/TestResults/full-suite/<run-id>/` directory:

- `summary.txt`: commands, their exit codes, and the final result (absent if forcibly terminated).
  This includes the consolidated overview described below.
- `candidates/`: exact packages and `candidate-build.json` provenance.
- `<test-project>/results.trx`: one results file per test project.
- `pack/` and `network/`: existing source, fixture, gateway, and GitHub budget reports.
- `maintenance/`: upstream skill maintenance reports.

Existing terminal tests continue preserving recordings in their own `TestResults/recordings/`
directories; network fixture recordings are below this run's `network/recordings/`.
Their paths and replay commands appear in the test output/TRX. No previous reports are removed.
Budget comparisons bracket the existing network-test scope, not every build command in the runner.

## C# console overview

The C# runner ends by printing one consolidated overview and appending the same text to
`summary.txt`: per-project and overall passed/failed/skipped/other counts, failed commands and
test names with their first error line, grouped skip reasons, candidate branch/SHA and package
paths/hashes, and the existing budget summaries for packaging and network verification.
Budget reset/measurement caveats are retained; no extra measurements or network requests are made.

Missing, malformed, or incomplete reports are identified explicitly. Totals cover only the
available result rows; missing reports are never treated as successful projects or zero tests.
The command exit code remains authoritative, and an incomplete overview is labelled separately.
Detailed errors and recording replay paths remain in the TRX files.

The workflow is unchanged.
For individual developer runs and detailed test contracts, see
[package fixtures](fixtures/README.md), [Check live tests](Agentic.Check.LiveTests/README.md),
and [companion integration tests](Agentic.LiveTests/README.md).
