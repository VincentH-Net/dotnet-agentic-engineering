# Test runners

## Manual test shell

Test the local builds of all three tools together with this branch's directives and skills, before
anything is published:

```sh
dotnet run --file tests/manual-test.cs
```

It packs `Agentic.Check`, `InnoWvate.Agentic` and `InnoWvate.Dna` from the working tree into an
isolated feed, unpacks a baseline fixture (default: the newest collection's `broad-stack`) into a
new Git repository under the temp folder, installs the local `dna` into an isolated .NET CLI home,
caches the local `Agentic.Check`, and on macOS opens a Terminal window in that repository. Only that
terminal's environment changes: `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `AGENTIC_CHECK_CACHE_DIR`,
PATH, and `AGENTIC_CHECK_PREVIEW_SOURCE_REF`. Your normal global tools, caches, GitHub login and
shell profiles are untouched. In that shell `dna check`, `dna prompt-log`, `dnx agentic.check` and
agent-started `dotnet agentic` commands all resolve the local packages, because the repository's
`NuGet.Config` lists only the local feed.

Directives and skills have no local-file source in Agentic.Check; it reads them from GitHub. The
script therefore requires HEAD to be pushed and `directives/` and `plugins/` to be clean, and pins
every check the shell starts to that commit through the hidden `AGENTIC_CHECK_PREVIEW_SOURCE_REF`
variable, which implies `--preview`. The status table's "Source channel" row shows the pinned commit
and whether it came from the variable or from `--preview-source-ref`. Tools are packed from the
working tree, so uncommitted `src` changes are included and noted.

Options: `--list` shows the baseline collections, their fixtures and the fresh definitions;
`--baseline <id>` and `--fixture <name>` choose what to unpack; `--fresh` materializes only the
fixture's trigger content for first-install flows; `--target <path>` reuses an existing test
repository and rewrites only its `NuGet.Config`; `--debug` packs Debug; `--no-terminal` prints
the activation command instead of opening a window; `--no-gh` keeps gh off PATH to test the
missing-prerequisite message. Each run writes `tests/TestResults/manual/<run-id>/` with `feed/`,
`activate.sh`, `README.md` and `manual-build.json`.

## Full-suite runner

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
   and clean production/content inputs before packing all three Release candidates
   (`Agentic.Check`, `InnoWvate.Agentic`, and `InnoWvate.Dna`). Release builds of the tool projects
   map source paths to repository-relative paths, and packing fails if a Release PDB still contains
   the local checkout path. It records
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

Anonymous-access regressions run without changing the developer's GitHub login. Unit tests use
the existing command-runner and HTTP boundaries to simulate missing credentials and quota errors,
verify that execution stops without a quota lookup or retry, and preserve completed work.
The recorded terminal scenarios use the existing fake gh executable and
seeded source cache. Real anonymous `gh skill` compatibility was verified separately with gh
2.93.0 and 2.101.0; routine tests do not require a container, logout, or production testing switch.
The real-package and maintenance tests retain their authenticated fixture setup.

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
