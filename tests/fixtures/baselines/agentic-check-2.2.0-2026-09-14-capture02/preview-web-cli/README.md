# Published 2.2.0 selective preview capture

This completes the previously missing `preview-web-cli` fixture. It is an authentic selective
installation by the unchanged published Agentic.Check 2.2.0 package, captured on
`2026-09-15T14:14:49.043631+00:00`. It does not reconstruct release-day upstream content.
The collection retains its initial `2026-09-14-capture02` ID; this fixture's metadata records
its actual later capture date. The 57 files belonging to all thirteen prior captures are
byte-for-byte unchanged. Only the collection's completed/failed entries changed.

## Explicit one-time exception

The published manifest recommended sixteen skills. In its normal interactive selector, only
`dotnet/skills:plugins/dotnet-test/skills/dotnet-test-frameworks` was deselected because that path
had been removed from upstream preview. It has no dependents in the published 2.2.0 manifest.
The installer exited 0 and installed all fifteen selected skills and all three applicable
directives. No missing skill was fabricated, substituted, or ignored after a failed installation.
The original report still lists all sixteen recommendations; `rejectedItems` records the exception.

An isolated, temporary .NET 10 console harness reused the existing package, terminal, source
oracle, snapshot and run-scoped authenticated gh gateway helpers. It restricted preparation
to this missing fixture, selected through the real terminal UI, and required the installed
inventory to equal exactly all recommendations except the one explicitly rejected identity.
The normal preparation tool still uses `--yes` and requires every recommendation. No production,
preparation-tool, or test assertion changes were made for this recovery.

Every installed skill body, authored metadata, asset inventory and source blob identity was
verified against its actual upstream commit. Directive contents, detector gates, original
preview pins, trigger files, and absence of a companion were verified. All eighteen end-of-run
source checks passed before the snapshot was accepted. Separate ZIP/hash verification and the
existing snapshot-inventory regression subsequently checked the persisted result.

## Provenance

| Item | Identity |
| --- | --- |
| Published installer | Agentic.Check 2.2.0 from the collection's NuGet URL |
| Installer SHA256 | `6ae632d7585d0db13884267244776a60a704cd63b448aec5f372c85606348931` |
| Installer embedded source commit | `43e68fd7a975dde9a1dbf7933fe13776d61a8428` |
| Snapshot SHA256 | `ff71a7887ba798e03549d2da76e4170495646c146c91181c0ee3f83d123f4319` |
| Own skills/directives source | `main` at `af190c3474079ea5b5143e170deb331c431c59a9` |
| dotnet/skills source | `main` at `43dec99816ae6bfafae082698a01437d237a3f90` |
| Environment | .NET SDK 10.0.301; gh 2.100.0; macOS 26.6.2 |
| Companion | Absent, as expected for published 2.2.0 |

The snapshot contains 19 files. Exact selections, individual source trees/blob hashes,
invocation and relative file hashes are in `metadata.json`; upstream licenses are in `licenses/`.
Preparation used normal default-branch preview resolution, including the original installer’s
branch-name pins. No candidate source override was used to manufacture this historical capture.

## Local evidence and replay

Artifacts are under `tests/Agentic.Check.LiveTests/TestResults/package-fixtures/preview-baseline-recovery-2026-09-15/`:

- `preparation.log`, `preparation-verification.json`, and the temporary `oneoff/` harness.
- `original-collection.json` preserves the original failure; that failure is also in Git at
  commit `deffb51d4cff4b0368946470ea8c2a6b554c4816` in this collection's `collection.json`.
- `baseline-before.json` records the pre-recovery hashes.
- Gateway evidence: `../github-runs/5265c7923a054731af9c220e3578e761/` (validated source boundary;
  107 upstream requests, 50 cache hits, zero transport failures).

From the repository root:

```sh
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/preview-baseline-recovery-2026-09-15/preview-web-cli-selective-cb5642a9622f46f0bbc8254a77b20352.cast
```

Recording SHA256: `f87e6a42377eb968a42efa1ec1e60c3e9f61ed747041b84be19368956ba2e5db`. Recordings and temporary harness code remain
local test artifacts, outside tracked fixture data. Tests load the immutable snapshot directly;
there is no local reconstruction requirement.

## Candidate verification

See `verification.json` in the local artifact directory for exact package hashes, scenario
results, source identities and recordings. Candidate tests retain their complete current skill
and dependency expectations, including `test-analysis-extensions`; stable expectations still
include `dotnet-test-frameworks`. The preparation exception does not apply to candidate assertions.

Verified candidate: Release packages from the already-pushed `origin/v2-3-dna` commit
`deffb51d4cff4b0368946470ea8c2a6b554c4816`. Package directory:
`tests/Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/deffb51-preview-baseline-recovery/`.

| Package | SHA256 |
| --- | --- |
| Agentic.Check.2.3.0.nupkg | `9a8ed8e348acf208c812e861ece1929d332ec9020c3cb7912ea89b34d6e2c517` |
| InnoWvate.Agentic.2.3.0.nupkg | `43b4dcdcc6b365d10a311c56efa399871b43020cef123bdffa6c98b261cb5b87` |

Local fixture/package support: **6 passed**, including the full persisted snapshot inventory.
All six preview fixture network cases were executed using separate temporary targets:

| Scenario | Result |
| --- | --- |
| Fresh candidate preview | Passed |
| Historical preview to candidate preview: reinstall/preservation | Passed |
| Historical preview to candidate preview: real content transition | Passed |
| Declined preview update: preservation | Passed |
| Historical preview to published stable | Failed: exact asset inventory mismatch in `platform-detection` after the real command exited 0 |
| Candidate preview to published stable | Failed: stable dry-run exits 1 trying to resolve `src/Agentic/Agentic.csproj` at own `v2.2.0` (HTTP 404) |

The run reports **4 passed, 2 failed, 67 fixture-selection skips**. There were no unchanged-source
skips: the content transition passed. Both stable-switch failures are preserved for separate
investigation; neither is hidden by changing candidate assertions or the baseline. The second
failure is outside the authorized hidden preview-source extension: it occurs in normal stable
source resolution. The first mismatch is consistent with a preview-only asset remaining after
reinstall, but the unchanged test disposes its failed target, so the exact residual file set
was not captured. Stable `platform-detection` has only `SKILL.md`; the preview snapshot also
contains `references/command-mode.md`.

During candidate testing, dotnet/skills preview was at
`36222bf32dbd6c857a1e5e60a9e43d8da8571390`, newer than the saved baseline. Stable sources were
own `v2.2.0` at `ebc711fb6db0912cae2b0c2bc4991d57d5afa007` and dotnet/skills `v1.0.0` at
`cb7c10bd32b250459632df9e6fa1350cdaf00b18`. All eighteen end-of-run source checks passed;
there were no quota/authentication or transport errors. Gateway run
`ba281f0cbe444db9a92bcbf027134dc9` recorded 186 upstream requests and 680 cache hits.
Every upstream request carried authentication. Exact packages and all snapshots retained their hashes.

TRX files: `tests/Agentic.Check.LiveTests/TestResults/preview-baseline-inventory.trx` and
`tests/Agentic.Check.LiveTests/TestResults/preview-baseline-network.trx`. The artifact directory's
`recording-replay.md` lists both the preparation and declined-update terminal recordings.
The other preview cases use real noninteractive processes and retain JSON/text reports.
The broader suite was not rerun for this data-only change; previously reported unrelated
`broad-stack/stable-current` and upstream skill-review failures are outside this recovery.
