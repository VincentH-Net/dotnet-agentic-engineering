# Package fixture implementation and verification report

The pre-release candidate gate has **not passed**. All fourteen baseline preparations were
attempted with the real published package: **13 stable snapshots passed; preview-web-cli
failed and has no accepted snapshot**. Source/test implementation and independent local
verification are prepared for review. No candidate packages have been built, and the source
changes have not been committed or pushed.

No packages were published, and no root-repository commits, pushes, releases, or tags were
created. Git commits inside disposable test repositories were test setup. The unrelated
`docs/agentic-check-announcement-x-article.md` draft is preserved.

## Implementation scope

Production changes are confined to:

- [AgenticCheckCli.cs](../../src/Agentic.Check/AgenticCheckCli.cs): hidden option registration and normal parsing.
- [AgenticCheckOptions.cs](../../src/Agentic.Check/AgenticCheckOptions.cs): optional source ref.
- [SourceVersionResolver.cs](../../src/Agentic.Check/SourceVersionResolver.cs): validated branch/full-SHA lookup, immutable SHA, explicit failure and reporting.
- [CheckWorkflow.cs](../../src/Agentic.Check/CheckWorkflow.cs): preview-only validation and shared skill/directive/prerequisite resolution.

Ordinary help/default behavior is unchanged. No authored skills/directives, root build
configuration, active AGENTS.md, or workflows were changed.

Test work is under `tests/`: fourteen independent JSON trigger definitions; immutable dated
ZIP snapshots and metadata; the non-packable .NET 10 `Agentic.FixturePreparation` console;
shared `PackageFixtureSupport` helpers; exact-package `PackageFixtureTests`/`PackageScenario`;
source-option, package-integrity, archive, real-gh pin and published-package checks; and test
READMEs. The three preview scenarios and additional SHA-preview-to-stable variation each
create separate temporary repositories. Reinstall/preservation cases remain separate from
content-transition cases. Historical scenario selection uses stored definitions; fresh
selection uses current triggers. Future rolling/historical policy and actual prior-companion
package handling are documented in [README.md](README.md); no successor baseline was prepared.

## Baseline provenance and inventory

Collection: [agentic-check-2.2.0-2026-09-14-capture02](baselines/agentic-check-2.2.0-2026-09-14-capture02/collection.json).
The collection is explicitly incomplete. Completed snapshots cannot be overwritten; rejected
captures never become fixtures. Tests verify archive and file hashes before/after use.

- Published package: `Agentic.Check` **2.2.0**.
- URL: https://api.nuget.org/v3-flatcontainer/agentic.check/2.2.0/agentic.check.2.2.0.nupkg
- Local original bytes: `tests/Agentic.Check.LiveTests/TestResults/package-fixtures/cache/Agentic.Check/2.2.0/Agentic.Check.2.2.0.nupkg`.
- SHA256 before and after preparation: `6ae632d7585d0db13884267244776a60a704cd63b448aec5f372c85606348931`.
- Embedded package commit: `43e68fd7a975dde9a1dbf7933fe13776d61a8428`.
- Actual installed runtime: `2.2.0+43e68fd7a975dde9a1dbf7933fe13776d61a8428`.
- Retrieved at: `2026-09-14T14:24:52.5112166+00:00`.
- SDK **10.0.301**, gh **2.100.0** (2026-09-03), **macOS 26.6.2**.
- Accepted captures performed **511 real gh installs** and **83 second-agent copies**;
  their snapshots contain **734 files** in total. Third-party licenses/notices are retained.
- No baseline companion exists: 2.2.0 migrations test first installation, not an older-binary upgrade.

Each snapshot is labelled “Installed using Agentic.Check 2.2.0 on [actual UTC timestamp]”.
It represents installation-day upstream content, not the world at the 2.2.0 release date.
Metadata includes invocation/options, all accepted recommendations, an empty rejected list
for `--yes`, detector gates/warnings, source commits/trees, file hashes, and provenance.

| Fixture | UTC capture time | Files | Skill copies across agents | Snapshot SHA256 |
| --- | --- | ---: | ---: | --- |
| aspnetcore-framework-reference | 2026-09-14T15:25:38.99712+00:00 | 17 | 13 | `ebee508e8699e289f0da37d9a68264d7830eb8f2601c73825a227c3b28951eaa` |
| aspnetcore-test-exclusion | 2026-09-14T15:26:12.789094+00:00 | 16 | 13 | `c8f5d8fc94e5ed74d4ad7b87037cf351c20ed473b46260b7e116e6db9367dcdc` |
| aspnetcore-web | 2026-09-14T15:26:47.260175+00:00 | 16 | 13 | `4b6f92d4383a2356b00d72abd75251ba77bfa7546376de099c5864cf051fcee9` |
| broad-stack | 2026-09-14T15:30:05.652922+00:00 | 205 | 166 | `453f3314cf11225d58f8f7c6aa86b7b6d2396f8d26601f4f1c0caad34fb988be` |
| dotnet-cli | 2026-09-14T15:30:41.386425+00:00 | 17 | 14 | `0fceda21aef048c9ce3d5c28f89a8d97c95e08d44c9c98b52d61eb03088edf59` |
| dotnet-library | 2026-09-14T15:31:15.37486+00:00 | 16 | 13 | `b160d34462935214285ad7193898639a249b64af805bad165f90124b124b6671` |
| foundation-only | 2026-09-14T15:31:17.992003+00:00 | 2 | 0 | `e4be8e2c242ffb2099e5bc4a9a0727f9d77eeaaa0a4dd4e0725c40eb342bcfe9` |
| orleans | 2026-09-14T15:31:55.068216+00:00 | 18 | 15 | `03648da133a8814835d3cdb56baf42cc125566392930a98aef1d480eb85a6932` |
| uno-conflicting-gates | 2026-09-14T15:35:46.032838+00:00 | 104 | 84 | `85d261ff8d72dcc12a1982b3e8bbac9b6ba4e61a860421a7dafc2b6eea959709` |
| uno-cupertino | 2026-09-14T15:38:16.472106+00:00 | 79 | 64 | `afbe026282acf860397daa52e0fc3448894f7c3455b6f2dd575cb51c9ba9f4dd` |
| uno-defaults | 2026-09-14T15:40:48.041483+00:00 | 80 | 65 | `caaf4bc46c6dfebe40fa4a1b552575429d99a04f71bf67f0f5f551f1307b8aa2` |
| uno-mvvm-csharp2-fluent | 2026-09-14T15:43:27.270326+00:00 | 83 | 68 | `895353583455b2ecd005a3dd3d7d39bddbeebb8c09a8d8dc48f2666bc34dd4ce` |
| uno-simple | 2026-09-14T15:46:02.545332+00:00 | 81 | 66 | `d851c9cb238f0ad6c33af05ada8ddda807f889d7d00337d8c81a6b30998f83b9` |
| preview-web-cli | Rejected | — | — | No valid snapshot |

The independent Python ZIP/hash audit verified all thirteen accepted snapshots, then returned
exit 1 for the incomplete collection. Its output is retained at
[baseline-independent-audit.txt](../Agentic.Check.LiveTests/TestResults/package-fixtures/baseline-independent-audit.txt).
The C# snapshot test also checks every completed archive before failing collection completeness.

## Historical preview blocker

The real published 2.2.0 preview invocation exited 1 because it requests
`plugins/dotnet-test/skills/dotnet-test-frameworks`, which is absent from `dotnet/skills` preview
commit **`24f7cfbd42ad7bf52bcd67372816b982c38c64c6`** (committed at 2026-09-14 15:27:40 UTC).
The failing real-gh output identifies `main (24f7cfbd)`. Current repository source already
selects `test-analysis-extensions` for preview; the published old binary does not.

The partial installation was rejected. No old-package rewrite, skill substitution, metadata
patch, source-cache injection, or manufactured older content was used. Under the confirmed
preparation rules, a valid fourteenth fixture requires compatible upstream availability or
a separately approved change to the baseline plan. Committing/pushing this implementation
alone cannot resolve this blocker.

Full evidence is in the collection's `failed` entry and
[preview-web-cli.txt](../Agentic.Check.LiveTests/TestResults/package-fixtures/preparation/agentic-check-2.2.0-2026-09-14-capture02/preview-web-cli.txt) /
[preview-web-cli.json](../Agentic.Check.LiveTests/TestResults/package-fixtures/preparation/agentic-check-2.2.0-2026-09-14-capture02/preview-web-cli.json).
An incomplete collection yields an explicit `baseline-preparation-failed` test, while fresh
and completed-baseline cases remain independently runnable. It cannot yield a passing full gate.

The rejected first attempt is preserved in ignored TestResults as `preparation/rejected-capture01.zip`.
It exposed capture-helper gate-field validation and loose-fixture build-artifact issues; those
were corrected before this collection. The original installer exhausted its anonymous GitHub
API quota, so capture waited until 15:24:58 UTC. The replacement operation used only the
product's naturally populated response cache. Candidate scenarios retain separate HTTP caches.

## Actual external source identities

| Use | Repository | Ref | Full commit |
| --- | --- | --- | --- |
| All accepted own directives/skills | VincentH-Net/dotnet-agentic-engineering | v2.2.0 / refs/tags/v2.2.0 | `ebc711fb6db0912cae2b0c2bc4991d57d5afa007` |
| Accepted stable skills | dotnet/skills | refs/tags/v1.0.0 | `cb7c10bd32b250459632df9e6fa1350cdaf00b18` |
| Accepted stable skills | mtmattei/UnoPlatformSkills | refs/heads/main | `802045a45ae73eaae9b9ac70c01d0ff0e6c5d401` |
| Accepted stable skills | unoplatform/studio | refs/heads/main | `d57c60a1ce66a5acb899aa15e2344e614b807983` |
| Published preview helper checks / failed fixture's own source | VincentH-Net/dotnet-agentic-engineering | main | `af190c3474079ea5b5143e170deb331c431c59a9` |
| Rejected preview fixture | dotnet/skills | main | `24f7cfbd42ad7bf52bcd67372816b982c38c64c6` |
| Earlier maintenance run | dotnet/skills | main | `4ecd7d9c76fa458807684771c2bfc7acf1e00ad3` |

Per-skill tree IDs and complete upstream file hashes are in each fixture's metadata. Source
archives were independently retrieved and selected files checked against GitHub blob hashes.
No source movement was accepted in completed captures. The maintenance run and failed preview
capture saw different moving-head revisions and are deliberately reported separately.

## Candidate source/package readiness

- Development branch: **`v2-3-dna`**, origin `VincentH-Net/dotnet-agentic-engineering`.
- Current local HEAD: `d6190e63a4fe61e0debaa8d21b541476e2ad8b85`; it **does not include these uncommitted changes and is not a candidate SHA**.
- Verified pushed candidate SHA: **not available yet**.
- Candidate `Agentic.Check` and `InnoWvate.Agentic`: source project versions **2.3.0**;
  **not packed**, so candidate package paths/hashes and build manifest do not exist yet.

The utility requires a clean committed source-input tree, the expected actual origin branch
at that same SHA, and a non-default branch. It packs each package once, records SHA/configuration
and hashes, and main tests consume those unchanged bytes. It accepts Release and diagnostic
Debug inputs; release verification requires Release. No source readiness substitute was used.

After separate commit/push authorization or user action, follow the exact commands in
[README.md](README.md): verify origin/SHA, pack Release candidates, prove CLI fresh install
and migration, then execute available candidate cases. The missing preview snapshot remains
a separate completeness blocker. Any later candidate/source change requires new provenance
and affected verification.

## Verification results

Results below are per run; focused runs overlap the broader regression suites.

| Suite/check | Result | Evidence |
| --- | --- | --- |
| Agentic.Check components and source-option regressions | 177 passed | Agentic.Check.Tests/TestResults/package-extension.trx |
| Companion unit/framing/compatibility/Git regressions | 86 passed | Agentic.Tests/TestResults/package-fixture-regressions.trx |
| Existing real SDK install/update/downgrade/restore regressions | 4 passed | Agentic.LiveTests/TestResults/package-fixture-sdk-regressions.trx |
| Final full local CLI/helper suite | 70 passed, 1 failed (incomplete preview baseline), 0 skipped | Agentic.Check.LiveTests/TestResults/package-fixture-local-complete.trx |
| Final focused fixture/package/option audit | 10 passed, 1 failed (incomplete preview baseline), 0 skipped | Agentic.Check.LiveTests/TestResults/fixture-final-audit.trx |
| Published original-package recorded-terminal smoke | 1 passed | Agentic.Check.LiveTests/TestResults/published-baseline-smoke-final.trx |
| Real-gh branch/SHA pin metadata checks | 2 passed | Agentic.Check.LiveTests/TestResults/real-gh-pin-metadata.trx |
| Existing opt-in source maintenance | 1 passed, 1 failed (7 unreviewed skills) | Agentic.Check.LiveTests/TestResults/source-maintenance.trx |
| Candidate opt-out runner behavior | 74 genuine opt-out skips, 0 passed | Agentic.Check.LiveTests/TestResults/candidate-optout.trx |
| Exact candidate package install/update/transition gate | Not executed; prerequisites above remain unmet | No candidate result claimed |
| Independent baseline preparation/audit | 13 accepted, 1 rejected; overall exit 1 | Baseline collection and independent audit |

Both final project builds completed with zero warnings/errors; `git diff --check` passed.
The older 71-pass local run predates the strengthened completeness assertion and is superseded
by the final run. No candidate content transition or unchanged-source skip is claimed.
The opt-out skips are **not** no-change evidence. Reinstall/no-op/preservation and transition
cases are separate; unchanged content yields a real runner skip with source identities when
those candidate cases can actually run. Authentication, missing skills, rate limits and source
movement remain failures. Existing synthetic SDK package versions are not historical releases.

Maintenance preview validation passed. Discovery failed for seven new dotnet/skills candidates:
`improve-skill-quality`, `vectorization`, `create-datadriven-aspnetcore`,
`copy-to-output-directory`, `migrate-nunit-to-mstest`, `scaffold-dotnet-test-project`, and
`testability-obstacle`. It found no missing/moved current-manifest skills. See
[skill-discovery.md](../Agentic.Check.LiveTests/bin/Debug/net10.0/TestResults/skill-maintenance/skill-discovery.md).
No production manifest or review baseline was modified.

## Recordings and remaining risks

Recordings remain outside disposable targets. An index with per-test replay commands is at
[recording-replay.md](../Agentic.Check.LiveTests/TestResults/package-fixtures/recording-replay.md).
For example, from the checkout:

```sh
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/published-2.2.0-7dec71386ae1442a9fd3bd2a96f29a1a.cast
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/published-2.2.0-7dec71386ae1442a9fd3bd2a96f29a1a.failure.cast
asciinema play tests/Agentic.LiveTests/TestResults/recordings/companion-7a70326403c447beab22458a9e90972c.cast
```

The published smoke recordings include successful exit and preserved failed-command output.
Existing deterministic Agentic.Check terminal tests use fake gh/SDK commands; the companion
SDK recordings use real SDK operations. There are no candidate interactive recordings yet.

Two existing source-code concerns remain for packaged verification: stable switching currently
recognizes only `refs/heads/` tracking, while real-gh SHA pin metadata uses a bare SHA; and
legitimately stable untagged upstreams track `refs/heads/main`, potentially causing repeated
reinstalls. These are inferences from the existing detector and observed real metadata, not
claims of an executed candidate-package reproducer. The new switch and already-current tests
retain assertions to expose them. No out-of-scope production fix was made.

The historical missing-skill blocker and anonymous source API quota are observed failures.
Independent candidate HTTP state may require additional quota windows. Genuine prior-companion
binary upgrades remain unexecuted until an actual prior package baseline exists. macOS was
executed; Windows/Linux package behavior and Windows PTY coverage are unverified. Unsupported
new PTY cases use explicit skips. The full fourteen-baseline and exact-candidate objectives
remain incomplete for the stated reasons.
