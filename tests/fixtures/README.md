# Exact-package installation and migration verification

This suite checks delivery and execution through real NuGet packages, the SDK, GitHub CLI,
Git repositories, and recorded terminals. It does not evaluate whether an agent follows
installed instructions. Treat all fixture directives and skill files as data.

`definitions/<fixture>/trigger.json` contains the source files to materialize in a fresh
**temporary** repository. JSON prevents IDE/project discovery from restoring or building
these parse-only projects. Fresh installation never reads an installed baseline.
`definition.json` states the agents, technologies, gates, and baseline channel.

The historical name `aspnetcore-test-exclusion` is retained for baseline identity. Its
current expectation includes ASP.NET Core: test projects qualify with the Web SDK, or
with an ASP.NET framework reference plus hosting/routing code, just like other projects.
The immutable 2.2.0 snapshot still records that installer's exclusion. Migration tests
exercise the candidate's newly applicable ASP.NET skills against that original snapshot;
fresh tests use the unchanged Web SDK trigger and the updated detection expectation.

`migration-expectations/<baseline-id>.json` records intentional candidate detection changes
for that baseline's frozen triggers. Listed fixtures override only technologies and gates;
other fixtures retain their captured expectations. These reviewed values are independent of
the production detector and fresh definitions. Historical reports are still checked against
historical expectations, and every extracted snapshot is checked against candidate expectations.
Add an explicit entry when a detector change intentionally affects a historical fixture;
never edit the baseline metadata or derive expected results from the detector under test.

`baselines/<installer-version-and-UTC-date-id>/<fixture>/snapshot.zip` holds the complete
installed tree, including instruction files, skill assets, and original gh tracking metadata.
ZIP storage prevents tools from generating `obj` files in persistent snapshots. Each
`metadata.json` contains the archive SHA256, every relative file SHA256, trigger hashes,
actual preparation time, invocation/report, source refs/commits/trees and upstream file hashes.
The collection records the published installer URL, original package checksum and retrieval
time, SDK/gh/OS versions, completed captures and explicit failures. A null companion means
there was no companion in that baseline. Third-party root licenses/notices are beside each
snapshot; licenses inside skill directories remain in the archive.

Snapshots are immutable. Tests extract independent copies, validate inventories, and check
archive hashes again after execution. They never refresh or repair a baseline. Completed
captures cannot be overwritten. Installer packages, source archives, reports, and credentials
are not stored in tracked snapshot content. Source archives are checksummed and extracted
outside the checkout; selected skill files are also verified against GitHub blob identities.

Historical snapshots retain their original directive markers. Candidate runs expect
prefix-free markers, including when the selected stable source still uses the old prefix.
The independent oracle normalizes only those boundary markers for candidate verification;
published baseline preparation continues to compare source blocks verbatim. Migration checks
require exactly one new marker pair and no remaining legacy pair for each selected directive.

## Preparing a baseline

Use .NET 10 and a supported real `gh skill` CLI (this capture was developed against 2.100.0).
`GH_TOKEN`/`GITHUB_TOKEN` may supply authentication. Otherwise the helper reads the existing
`gh auth token` into memory and passes it only to child processes. Credentials are resolved
once per process (`GH_TOKEN`, then `GITHUB_TOKEN`, then the existing login). Before baseline
preparation or candidate network scenarios, a once-per-process preflight checks active gh
authentication and reads the live authenticated budget in an isolated workspace. Missing or
invalid credentials fail with login/token guidance; the harness never starts an interactive
login. Redirected and terminal children receive the same explicit credentials. No token is
logged or preserved in reports. PTY startup uses a temporary user-only environment file that
Bash removes before commands run. Git/global agent directories, CLI home, NuGet caches, and configuration are isolated.
Fixture child processes disable gh telemetry (`GH_TELEMETRY=0`) to prevent delayed telemetry
writes racing disposal of the temporary home directory.

From the checkout, in the foreground:

```sh
dotnet build tests/Agentic.FixturePreparation/Agentic.FixturePreparation.csproj
dotnet run --no-build --project tests/Agentic.FixturePreparation -- prepare \
  tests/fixtures/baseline-definitions/agentic-check-2.2.0.json \
  agentic-check-2.2.0-YYYY-MM-DD-capture01
```

Use the actual UTC date and a new suffix. The utility downloads the published NuGet package,
verifies its identity/checksum, installs it through a controlled feed, executes it with
`--yes --agents <definition-agents>` (plus `--preview` for `preview-web-cli`), and independently
compares upstream contents. These runs do not execute skill scripts or build trigger projects.
All fourteen definitions are attempted; a partial installation is a failure, not a fixture.
The source world is the world at preparation time, **not** a reconstruction of release day.

An interrupted/failed, still incomplete capture can be explicitly resumed with the same
command arguments and `resume-incomplete` in place of `prepare`. This verifies and skips
completed captures, retains failure evidence in TestResults, and retries only missing/failed
captures. A completed collection cannot be resumed. To recapture any completed snapshot, use
a new collection ID. Never turn a migrated test target into the next baseline.

An incomplete collection remains visibly incomplete: selecting it adds a failing
`baseline-preparation-failed` case for each rejected capture. Fresh targets and completed
baseline migrations remain independently runnable, but the complete candidate gate cannot
pass. The snapshot-inventory regression also rejects a collection with capture failures.
Do not invent a snapshot or remove a failure merely to make the suite green.

The 2.2.0 collection was completed on 2026-09-15 with an explicitly approved one-time
selective capture of `preview-web-cli`: the unchanged published installer ran interactively
with only the upstream-removed `dotnet-test-frameworks` recommendation deselected. All other
recommendations were installed successfully and independently verified. The existing thirteen
captures remain byte-for-byte unchanged. This used a temporary harness; the preparation tool
and its normal all-recommendations checks are unchanged. See the
[capture provenance and verification](baselines/agentic-check-2.2.0-2026-09-14-capture02/preview-web-cli/README.md).
The collection ID retains its initial preparation date; the added fixture records its actual
2026-09-15 UTC capture time. Tests consume the saved snapshot without repeating preparation.

Historical preview pins retain the format emitted by that installer, including branch-name
pins in 2.2.0. Preparation independently resolves each actual pin to the recorded source
commit; it does not rewrite the pin into today's candidate SHA format.

The historical installer makes unauthenticated GitHub HTTP requests even when gh is
authenticated. Its unauthenticated API limit may block preparation. HTTP 403, missing skills,
source movement, and failed commands are failures; none become unchanged-source skips.
Within one explicit preparation operation the utility permits the product's ordinary response
cache (3600 seconds), populated only by real product invocations. It does not inject source
responses. Wait for the API reset and resume an incomplete capture when rate-limited.

Every live test host also writes `github-runs/<id>/budgets.json` and `budgets.txt` under the
report directory: fresh authenticated and anonymous REST budgets before authentication/source
priming and after final source validation, observed comparable deltas, reset/counter warnings,
and upstream request/cache-hit counts. Four uncached probes measure shared account/IP budgets;
deltas are not exclusive test usage. Missing end measurements remain explicit after a crash.
See [quota measurement details](github-api-quota-plan.md#evidence-and-operation).

The utility is an unpublishable test-support console project, not a production command/tool.

## Candidate source and package provenance

The internal unsupported interface is:

```text
agentic-check <target> --preview --preview-source-ref <branch-or-full-commit-SHA>
```

It is hidden from ordinary help, requires preview, accepts only a branch or full commit SHA
in `VincentH-Net/dotnet-agentic-engineering`, and resolves that source once. This immutable SHA
feeds this repository's skills, directive listing/content, and companion source-version lookup.
Explicit resolution failures are fatal. External repositories retain ordinary preview/stable
selection. There is no environment-variable source override. Hiding affects discoverability,
not access. Ordinary behavior is unchanged when the option is absent.

Complete implementation and local regressions **before** source readiness. Committing and
pushing require separate user authorization/action, including the active prompt-log rules.
No command here publishes packages, creates releases/tags, or pushes source.

After authorized commits and push to the current non-default development branch at origin:

```sh
git status --short
git branch --show-current
git remote get-url origin
git rev-parse HEAD
git ls-remote --exit-code origin refs/heads/<development-branch>
dotnet run --no-build --project tests/Agentic.FixturePreparation -- pack-candidates \
  /absolute/path/to/new-candidate-directory Release
```

The last command validates the actual remote branch against HEAD (not a cached tracking ref),
checks the expected HTTPS/SSH origin and non-default branch, rejects dirty production/content
inputs, and packs all three tool projects exactly once. Unrelated untracked documents are outside these
inputs and remain untouched. It passes RepositoryCommit into NuGet metadata and writes
`candidate-build.json` with configuration, branch, SHA, package IDs/versions and hashes.
It checks source identity again after packing and refuses an existing output directory.
A Debug package build is accepted with `Debug`; the test runner configuration is independent.
Release verification requires Release packages. Candidate input changes require a new commit,
push, package directory and affected verification; no candidate is rewritten or relabelled.

## Input contract and commands

| Input | Meaning |
| --- | --- |
| `AGENTIC_E2E_CHECK_PACKAGE` | Absolute exact Agentic.Check nupkg path. |
| `AGENTIC_E2E_COMPANION_PACKAGE` | Absolute exact InnoWvate.Agentic nupkg path. |
| `AGENTIC_E2E_DNA_PACKAGE` | Absolute exact InnoWvate.Dna nupkg path. |
| `AGENTIC_E2E_BUILD_MANIFEST` | Optional manifest path; defaults beside Check to candidate-build.json. |
| `AGENTIC_E2E_BASELINE` | Explicit collection ID for migration; not needed by fresh scenarios. |
| `AGENTIC_E2E_SOURCE_CHECKOUT` | Optional explicit checkout; otherwise discovered from test binaries. |
| `AGENTIC_E2E_NETWORK=1` | Opt in to real package/GitHub install/update tests. |
| `AGENTIC_E2E_FIXTURE` | Optional exact fixture name. |
| `AGENTIC_E2E_SCENARIO` | Optional exact scenario name from the table below. |
| `AGENTIC_E2E_REPORTS` | Optional artifact directory, otherwise Agentic.Check.LiveTests/TestResults/package-fixtures. |
| `AGENTIC_E2E_CACHE` | Optional cache directory, otherwise cache below reports. |

Without opt-in, network cases are genuine runner skips. Once verification is opted in,
missing/invalid package/source/build inputs fail. The harness never builds packages inside
main candidate tests or substitutes source assemblies, fake gh, SDK responses, or HTTP caches.
Each target gets an isolated feed containing the unchanged candidates. The serial candidate
rows in one test-host run share two product source-response caches (3600 seconds), one per
channel. Each starts empty; the first selected scenario in each channel provides its cold
check, with no separate cold probes. Only actual package invocations populate these caches.
Cache paths and cold/shared use are logged; installer identity and start time are recorded
under `TestResults/package-fixtures/runs/<unique-run-id>/`. Different installer packages
cannot share a run cache. New test-host runs always get new directories, including filtered
reruns. Preserve the directories for diagnostics; never seed them from the oracle or prior
runs. This supersedes the original per-scenario product HTTP isolation requirement.
With unchanged sources and no expiry/retries, the candidate matrix is estimated to need
about 17 anonymous API requests (8 preview + 9 stable). This is not a measured guarantee;
real gh and independent verification use the separate authenticated budget through one
run-scoped test-only caching gateway. Every temporary gh configuration points its
`http_unix_socket` at the same .NET gateway; your ordinary gh configuration is untouched.
Only quota probes and boundary source validation deliberately bypass read caching.
See [the quota design and diagnostics](github-api-quota-plan.md) and
[measured proxy verification results](github-api-proxy-verification.md).
Target, SDK/NuGet HTTP, CLI, Git, and gh state stay separate. Independently retrieved upstream
identities are compared at the start/end of the whole network run. A changed or unavailable
source invalidates the run with rerun guidance, including otherwise unchanged-source skips.
Cached installed nupkg bytes and running companion versions are checked, and input hashes are checked again
on completion/failure. Test fixture commits are local to disposable repositories.

First prove candidate-source installation and reinstallation with the CLI fixture. It includes
this repository's CLI skill, external test skills, directives, and the companion prerequisite:

```sh
export AGENTIC_E2E_NETWORK=1
export AGENTIC_E2E_CHECK_PACKAGE=/absolute/candidates/Agentic.Check.<version>.nupkg
export AGENTIC_E2E_COMPANION_PACKAGE=/absolute/candidates/InnoWvate.Agentic.<version>.nupkg
export AGENTIC_E2E_DNA_PACKAGE=/absolute/candidates/InnoWvate.Dna.1.0.0.nupkg
export AGENTIC_E2E_BASELINE=agentic-check-2.2.0-<UTC-date>-<capture>
AGENTIC_E2E_FIXTURE=dotnet-cli AGENTIC_E2E_SCENARIO=fresh \
  dotnet test tests/Agentic.Check.LiveTests --filter 'Category=PackageNetwork' \
  --logger 'console;verbosity=normal'
AGENTIC_E2E_FIXTURE=dotnet-cli AGENTIC_E2E_SCENARIO=migration \
  dotnet test tests/Agentic.Check.LiveTests --filter 'Category=PackageNetwork' \
  --logger 'console;verbosity=normal'
```

Then run the full collection (unset optional fixture/scenario selectors):

```sh
dotnet test tests/Agentic.Check.LiveTests \
  --filter 'Category=PackageNetwork|Category=CandidatePackage' \
  --logger 'trx;LogFileName=candidate-packages.trx' --logger 'console;verbosity=normal'
```

PowerShell users set the same names with `$env:NAME = 'value'` before running dotnet.

| Scenario | Independent starting state and behavior |
| --- | --- |
| `fresh` | Each of 14 trigger definitions; preview plus exact candidate SHA. Broad is interactive and checks dependency selection and both agent directories. |
| `migration` | Each of 13 stable snapshots; preview plus exact candidate SHA. |
| `stable` | Each stable snapshot; published-source real gh update, no override. Broad accepts available updates interactively. |
| `stable-current` | Broad snapshot updated within this case, then checked again. |
| `stable-declined` | Another broad copy; declines an available published update and preserves selected-out files. |
| `preview-preview` | A new preview-web-cli copy; forced real pinned reinstall from candidate SHA. |
| `preview-stable` | A separate preview-web-cli copy; stable switching followed by an ordinary stable check. |
| `candidate-preview-stable` | Another copy, first installed from candidate SHA, then stable. Exposes any SHA-pin switching defect. |
| `preview-declined` | Another preview-web-cli copy; interactive deselection and exact skill/metadata preservation. |

The `candidate-preview-stable` variation also runs against the pre-companion `v2.2.0` source.
Its Git-only prompt-log directive requires no companion source-project lookup. The tools
installed during candidate preview setup must remain usable after switching and on the next
stable check; normal shorthand update behavior remains enabled. Historical `preview-stable`
starts with the published 2.2.0 snapshot and has no such candidate tool setup. Missing sources,
HTTP failures and genuine prerequisite errors remain failures.

Migration and preview-reinstall cases have separate content-transition rows. Before those
rows run an update, they independently compare intended source content with the baseline.
If unchanged, `Xunit.SkippableFact` reports a real runner skip containing source identities.
Separate installation/reinstall/no-op/preservation rows still execute. A changed pin/ref alone
is not a content transition. Errors, malformed metadata, source movement, mismatches and
prerequisite failures cannot become no-change skips. Selected source refs are rechecked after
the operation; movement is reported as an infrastructure failure, with no hidden retry.

Stable tests verify published content, not unreleased candidate content. Preview-only skills
outside the stable manifest must retain their existing bytes. Stable-manifest skills must
lose preview pins through the real product path; tests never call manual `--unpin` to help.
If selected published content requires a companion incompatible with supplied candidates,
verification fails and reports that mismatch.

Skill verification requires every file from the selected source, with correct asset hashes,
authored `SKILL.md` content, and source/pin metadata. Fresh installations and baseline preparation
require exact file inventories. For migrations and repeat installations, an extra file is allowed
only when the same path existed before that particular operation and its SHA256 is unchanged.
This accommodates `gh skill install --force` retaining files from an earlier version; Agentic.Check
does not promise to remove obsolete assets. Missing source files, incorrect expected contents,
new extra files, and modified retained files still fail. Each setup, apply, and subsequent-stable
phase captures its own pre-operation inventory. Accepted retained files are logged with their
paths/hashes and saved in `<run>-<phase>-retained-assets.json`; they are not treated as files from
the selected source. Subsequent stable checks also repeat delivery verification.

Companion checks run help/version and a Unicode/escaped prompt-log stdin -> wrap -> fixture
Git commit -> show/check round trip. Git HEAD must remain unchanged by companion reading.
The package-only case also commits and clones an installed target, then restores with clean
NuGet caches and checks preservation of an unrelated real tool manifest entry. The 2.2.0
baseline exercises **first companion installation**, not a binary upgrade. Existing synthetic
SDK version-resolution tests remain separate and are not historical release evidence.

## Regressions and recordings

```sh
dotnet test tests/Agentic.Check.Tests --logger 'trx;LogFileName=check.trx'
dotnet test tests/Agentic.Tests --logger 'trx;LogFileName=companion.trx'
dotnet test tests/Agentic.LiveTests --logger 'trx;LogFileName=sdk.trx'
dotnet test tests/Agentic.Check.LiveTests \
  --filter 'Category!=PackageNetwork&Category!=CandidatePackage&Category!=BaselineNetwork&Category!=SkillMaintenance' \
  --logger 'trx;LogFileName=local-cli.trx'
AGENTIC_E2E_NETWORK=1 dotnet test tests/Agentic.Check.LiveTests --filter 'Category=BaselineNetwork'
```

The existing AgenticCheckEndToEndTests use a real local apphost and recorded terminal with
**fake gh and companion SDK commands**. They cover deterministic UI/failure branches, not
real skill installation/update. Existing maintenance tests use
`AGENTIC_CHECK_SKILL_MAINTENANCE=1` for discovery/preview only; this suite does not reuse that opt-in.

Interactive recordings use Hex1b with fixed dimensions and disabled input recording.
They survive target cleanup, including failures, and are not committed. Each case prints:

```sh
asciinema play '<absolute-report-directory>/recordings/<fixture>-<scenario>-<id>-apply.cast'
asciinema play 'tests/Agentic.LiveTests/TestResults/recordings/companion-<id>.cast'
```

New PTY scenarios explicitly skip unsupported platforms; process/package checks are portable.
This task validates macOS. Windows PTY and a CI OS matrix remain future work.

## Rolling and historical baseline policy

| Baseline/scenario | Future policy |
| --- | --- |
| Fresh targets | Keep every applicable trigger permanently and test each candidate. |
| Previous published release | After a successful release, deliberately capture a new dated full collection using the actual published package(s), then use it for normal next-candidate upgrades. |
| 2.2.0 | Keep representative broad-stack, foundation-only and preview-web-cli history while migration from pre-companion installs is supported. This future reduction does not reduce the fourteen fixtures prepared/tested in this task. |
| Significant migrations | Retain selected releases when manifests, source tracking, directive storage, or compatibility rules change; add explicit migration expectations. |

Add a successor definition with its published installer identity/URL and companion expectation,
then run the same preparation utility with a new ID. Set `companionExpected: true` and
`companion: { "id": "InnoWvate.Agentic", "version": "<published-version>", "url": "<exact-NuGet-URL>" }`
when that release includes a companion. The utility uses this actual package in the isolated
feed and records/restores its bytes; it does not hardcode companion absence in reusable helpers.
A baseline containing a companion must retain its original published package URL/checksum.
Migration helpers download or verify that exact cached artifact, then restore/execute it in
isolation before migration. Select historical IDs explicitly for direct upgrades; do not assume installation
of intervening releases. Parameterized source/package/copy helpers keep the candidate SHA and
hashes fixed regardless of the chosen baseline. Additional release captures, reduced historical
coverage, and new migration boundaries are future work.
