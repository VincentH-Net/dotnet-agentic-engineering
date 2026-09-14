# Package fixture implementation and verification report

The pre-release candidate gate has **not passed**. All fourteen baseline preparations were
attempted with the real published package: **13 stable snapshots passed; preview-web-cli
failed and has no accepted snapshot**. Source/test implementation and independent local
verification were completed before the readiness checkpoint. After explicit user approval,
implementation and prompt-log commits were pushed to `origin/v2-3-dna`; exact Release
candidates were packed and exercised against their own pushed source revisions.

No packages were published, and no releases or tags were created. Authorized root-repository
commits and pushes are listed below. Git commits inside disposable test repositories were test setup. The unrelated
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
The [post-candidate independent audit](../Agentic.Check.LiveTests/TestResults/package-fixtures/baseline-post-candidate-audit.txt)
again verified every completed snapshot and all files, then returned 1 solely for the missing
preview capture. All fourteen attempted preparations remain recorded.

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
| Final candidate own skills/directives/prerequisite | VincentH-Net/dotnet-agentic-engineering | exact candidate SHA | `154294a7bdf29fc0517b415c35d5346819ad4f3a` |
| Completed candidate preview installations | dotnet/skills | main | `24f7cfbd42ad7bf52bcd67372816b982c38c64c6` |

Per-skill tree IDs and complete upstream file hashes are in each fixture's metadata. Source
archives were independently retrieved and selected files checked against GitHub blob hashes.
No source movement was accepted in completed captures. The maintenance run and failed preview
capture saw different moving-head revisions and are deliberately reported separately. Candidate
stable selections use the same tagged identities listed above; completed candidate Uno preview
installation resolved mtmattei/UnoPlatformSkills and unoplatform/studio to the same commits
listed for accepted captures. Per-run `*-sources.json` and `*-installed-sources.json` preserve
these identities and selected upstream content inventories.

## Candidate source/package readiness

- Development branch: **`v2-3-dna`**, origin `VincentH-Net/dotnet-agentic-engineering`.
- Implementation commit: `9b8a0b9`; immediate prompt-log commit: `a073b187a379383cae67864f27bc3740ee471bd5`.
- Test setup correction: `154294a7bdf29fc0517b415c35d5346819ad4f3a`; pushed to the same origin branch.
- Final tested candidate SHA: **`154294a7bdf29fc0517b415c35d5346819ad4f3a`**.
- Both packages are **2.3.0, Release**, packed at `2026-09-14T16:37:04.541914+00:00`.
- Package directory: `tests/Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/154294a-release/`.
- [Build manifest](../Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/154294a-release/candidate-build.json) records absolute paths, configuration, commit and hashes.

| Exact artifact | SHA256 |
| --- | --- |
| [Agentic.Check.2.3.0.nupkg](../Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/154294a-release/Agentic.Check.2.3.0.nupkg) | `ba1b4ed740e7c8b4ce8f873a1b670b7eb9943e0e16d895cf94200a0d9969664a` |
| [InnoWvate.Agentic.2.3.0.nupkg](../Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/154294a-release/InnoWvate.Agentic.2.3.0.nupkg) | `48be52e0f3b2a3c1cc58347cb96f64e39e12b24043b690395f0725b31e16f1b3` |

The utility verified clean committed production/content inputs and the actual origin branch
at the package SHA before and after packing. Main tests consume these unchanged bytes and
independently retrieve own skills/directives/prerequisite source from that same immutable SHA.
No readiness substitute was used. Release verification requires Release; diagnostic Debug
packages use the same input contract.

The first candidates at `a073b187a379383cae67864f27bc3740ee471bd5` are also preserved under
`candidates/a073b18-release/`: Check SHA256
`b71ed2b1269e2ad1eaa9e687edfe08593cbdcc187e17d83f6a65037ca368e983`, companion SHA256
`5ec0e691cd3f6368457e06c1170edf734232816abcfdb92d79cc4af9a9f6aa56`.
A first real SDK test exposed a setup error: the manually authored unrelated tool entry lacked
SDK-owned `rollForward: false`. The helper now prepares that entry through real SDK installation;
strict full-entry preservation remains asserted. After fixing setup, migration, actual content
transition, and companion clean-cache restore passed against those original bytes. A fresh
candidate pair was packed after committing/pushing the helper correction.

The full run additionally uses a test-driver correction to recognize dry-run “Would install”
selector actions, including stable switching with no missing skills. This changes no candidate
production/content input or expected product behavior. The tested source SHA above remains the
identity of the supplied packages and all own-source retrievals. A follow-up tests/docs commit
records the driver/telemetry fixes and final evidence; it does not claim new candidate bytes
or candidate verification at that follow-up commit. Future runs must satisfy source readiness
and pack a new pair for their selected current revision.

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
| First candidate fresh proof | 1 passed, 1 failed (SDK manifest setup), 68 selection skips | Agentic.Check.LiveTests/TestResults/candidate-first-install.trx |
| First candidate migration and actual content transition / companion restore | 3 passed, 67 selection skips | Agentic.Check.LiveTests/TestResults/candidate-first-migration.trx |
| Final candidate fresh install / companion clean-cache restore proof | 2 passed, 68 selection skips | Agentic.Check.LiveTests/TestResults/candidate-final-proof.trx |
| Final exact candidate full suite | 2 passed, 68 failed, 0 skipped; classifications below | Agentic.Check.LiveTests/TestResults/candidate-full-154294a.trx |
| Cleanup-fix candidate rerun | 1 passed (companion), 2 failed (HTTP 403), 67 selection skips; no cleanup exception | Agentic.Check.LiveTests/TestResults/candidate-cleanup-verification.trx |
| Independent baseline preparation/audit | 13 accepted, 1 rejected; overall exit 1 | Baseline collection and independent audit |

Final test and preparation-utility builds completed with zero warnings/errors; `git diff --check` passed.
Both candidate pairs retained their exact input hashes after all tests; see
[post-test package verification](../Agentic.Check.LiveTests/TestResults/package-fixtures/package-post-test-integrity.json).
The older 71-pass local run predates the strengthened completeness assertion and is superseded
by the final run. The first candidate CLI migration passed a real content-transition assertion.
The opt-out and fixture/scenario-selection skips are **not** no-change evidence. Reinstall/no-op/preservation and transition
cases are separate; unchanged content yields a real runner skip with source identities when
source comparison confirms unchanged content. Authentication, missing skills, rate limits and source
movement remain failures. Existing synthetic SDK package versions are not historical releases.

The full candidate run executed all **70 rows** available for the selected incomplete collection
in 17 minutes 18 seconds. Its two passes were Uno Cupertino migration/reinstallation/preservation
from the immutable 2.2.0 snapshot to candidate content, and exact companion round-trip plus
clean-cache clone/restore. It failed **65 rows with HTTP 403**, **one missing-baseline row**,
**one already-current production assertion**, and **one test-workspace cleanup race** that
masked an HTTP 403. [Per-case outcomes](../Agentic.Check.LiveTests/TestResults/package-fixtures/candidate-full-outcomes.json)
preserve the original primary errors.

The cleanup race left only `home/.local/state/gh/device-id`: gh telemetry wrote after its parent
command exited. The fixture environment now sets documented `GH_TELEMETRY=0`, and the abandoned
fixture-owned directory was removed. This test-only correction does not change package/source
inputs or assertions. The focused rerun retained both genuine HTTP 403 failures without a cleanup exception;
the exact companion restore case passed again. The final helper/build changes introduce no
production change beyond the originally authorized hidden option.

**No unchanged-source skips occurred in the full run.** Source resolution failed before those
comparisons could complete; those failures were not relabelled as unchanged. Stable no-op and
reinstall/preservation rows were attempted separately. Available published stable content was
current in the completed broad-stack case, but that case failed because of unnecessary reinstalls.
No successful changed-content stable `gh skill update` or historical preview migration is
claimed. The CLI content-transition pass is from the first pushed candidate pair; the final
candidate's separate Uno content-transition row was quota-blocked.

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
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/broad-stack-stable-current-bfd78809ce2c4ffaaa09a65809a33df9-apply.cast
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/published-2.2.0-7dec71386ae1442a9fd3bd2a96f29a1a.cast
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/published-2.2.0-7dec71386ae1442a9fd3bd2a96f29a1a.failure.cast
asciinema play tests/Agentic.LiveTests/TestResults/recordings/companion-7a70326403c447beab22458a9e90972c.cast
```

The published smoke recordings include successful exit and preserved failed-command output.
Existing deterministic Agentic.Check terminal tests use fake gh/SDK commands; the companion
SDK recordings use real SDK operations. Candidate interactive recordings are preserved in the same package-fixture recording directory.

The exact final candidate reproduced an existing stable-classification defect: the broad-stack
`stable-current` case first prepared an updated stable state, then reported zero outdated skills
but performed **62 reinstalls** on the subsequent check. Its assertion deliberately fails.
The affected untagged upstreams legitimately track `refs/heads/main`; the product classifies
them as preview installations requiring a switch to stable. The JSON report and recorded
terminal are preserved under `broad-stack-stable-current-bfd78809ce2c4ffaaa09a65809a33df9-*`.
No out-of-scope production fix was made.

A second existing concern remains an inference from source and real-gh metadata: stable switching
recognizes only `refs/heads/` tracking, while SHA-pinned preview metadata uses a bare SHA. The
candidate-preview-to-stable variation retains assertions for this, but its historical preview
starting fixture is unavailable; an executed packaged reproducer is not claimed.

The historical missing-skill blocker and anonymous source API quota are observed failures.
The candidate source API quota was exhausted during the full run; isolated later cases failed
HTTP 403. Authenticated gh access continued to work, but the production HTTP source resolver
does not use gh authentication. Additional quota windows or separately authorized product work
would be needed for a wider successful network run. Genuine prior-companion
binary upgrades remain unexecuted until an actual prior package baseline exists. macOS was
executed; Windows/Linux package behavior and Windows PTY coverage are unverified. Unsupported
new PTY cases use explicit skips. The full fourteen-baseline and successful exact-candidate gate objectives
remain incomplete for the stated reasons. All available candidate rows were attempted.
