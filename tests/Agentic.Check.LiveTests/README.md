Exact supplied-package installation/migration verification is documented in [the fixture suite](../fixtures/README.md).

# Skill maintenance

These opt-in tests inspect source repositories without installing skills or modifying manifests or review baselines. They require a recent GitHub CLI with `gh skill preview`, plus GitHub authentication (`gh auth login`, or `GH_TOKEN` in CI). This requirement applies to maintenance tests only, not to agentic-check users.

From the repository root, run both live maintenance tests:

```sh
AGENTIC_CHECK_SKILL_MAINTENANCE=1 dotnet test tests/Agentic.Check.LiveTests --filter 'Category=SkillMaintenance' --logger 'console;verbosity=normal'
```

In PowerShell, first set `$env:AGENTIC_CHECK_SKILL_MAINTENANCE = '1'`, then run the same `dotnet test` command without the environment assignment prefix.

Run only discovery or manifest validation by replacing the filter with:

```text
FullyQualifiedName~SourceRepositoriesHaveNoUnreviewedSkills
FullyQualifiedName~AllManifestSkillsCanBeFoundByGhSkillPreview
```

Run the offline classification and report tests without GitHub access:

```sh
dotnet test tests/Agentic.Check.LiveTests --filter 'FullyQualifiedName~SkillDiscoveryTests|FullyQualifiedName~SkillDiscoveryReportTests'
```

## Reports and caching

Reports are written to `tests/Agentic.Check.LiveTests/bin/Debug/net10.0/TestResults/skill-maintenance/` by default (the configuration/target framework follows the test build). Test output prints the absolute paths. Set `AGENTIC_CHECK_MAINTENANCE_REPORT_DIR` to override the directory.

- `skill-discovery.md`: navigation links, scan errors when present, one cross-repo table of new candidates, one of missing/moved skills, one of unique alternative parent folders, then an exclusions table per repo. Skill names link directly to their source files. Included skills, identical copies, long descriptions, and historical stable-only paths are omitted.
- `skill-discovery-sources.json`: baseline, stable and default-branch SHAs/timestamps and cache policy, for reproducing a scan or updating reviewed baselines without cluttering the review document.
- `manifest-validation.md`: each stable and preview `gh skill preview` result, including source-resolution output and failures.

Discovery uses `gh api --cache 1800s`, separate from agentic-check's cache. `AGENTIC_CHECK_MAINTENANCE_CACHE_SECONDS=0` requests fresh API responses; other non-negative values change the duration. Within a run, identical endpoints are fetched once. The source JSON records the cache policy, not individual cache hits. `gh skill preview` controls its own fetching; the API cache setting does not govern that command.

## Discovery rules

For every repo in `StaticSkillManifest.SourceReviews`, resolve the current default branch dynamically and the latest non-prerelease GitHub release, falling back to the default branch when no release exists. Read recursive Git trees at those exact SHAs and at the reviewed SHA. This stable policy matches agentic-check's source-version resolver; the separate unversioned `gh skill preview` check exercises gh's actual stable selection.

Scan every blob named `SKILL.md`, including hidden folders and folders not yet represented in the manifest. Compare case-sensitive repo-relative paths, scoped by repo. Exact-path manifest entries stay exact; name-only entries support root folders and use the manifest plugin label to disambiguate where possible, otherwise preferring the plugins convention over legacy layouts. Remaining ambiguities require review.

- Already in either manifest: omitted from the report, with availability checked separately for each manifest's source.
- Present at the baseline but absent from both manifests: presumed excluded.
- An otherwise excluded path with the same verified frontmatter name as an included skill in the same repo: alternative location. Identical directory trees are omitted. Differing or unverified trees appear in a single **Alternative locations** table, deduplicated by repo and parent folder of the skill folder, alongside the included parent folders. Individual skill names are not repeated. These rows do not fail discovery. Names in other repos remain separate. New candidates, moves, and missing-path checks retain their existing path-based behavior.
- Added since the baseline and absent from both manifests: new candidate, whether available in stable, preview, or both.
- Removed baseline path plus a new path with the same frontmatter name: possible move, requiring manual review.
- Manifest entry missing from its corresponding source: missing upstream.
- Ignore a path during candidate discovery when it is absent from both the reviewed inventory and the current default-branch inventory, exists in the current stable inventory, and that stable snapshot's commit timestamp is at or before the recorded review timestamp. No historical finding or category is retained. Missing manifest-path validation still runs independently. This compares snapshot timestamps, not individual skill commit history or release publication dates.

Current skill frontmatter is read with a YAML parser to compare actual names, even when folder names differ. This adds blob reads, reused by SHA within each repo and cached by gh. Directory tree SHAs come from the existing recursive tree responses, so comparing scripts and references needs no extra tree requests. Descriptions are taken from preview when a path exists in both current inventories. Invalid unchanged historical frontmatter (for example, validator fixtures) is marked unverified and is never deduplicated; changed-path parse errors and fetch errors still fail the scan. Hidden folders are included in discovery, but excluded from name-only manifest argument matching, consistent with gh's default discovery without `--allow-hidden-dirs`. There is no execution of upstream content.

Discovery fails on candidates, possible moves, missing entries, or fetch/parse errors. Other repositories are still scanned after a repo error. Truncated trees fail explicitly rather than silently producing incomplete inventories. Errors are not interpreted as exclusions or absence. Manifest validation continues through failed skills and reports all failures. Commands have two-minute timeouts; discovery and validation have overall 20- and 30-minute budgets.

## Reviewing a report

1. Review every candidate and possible move for a source repo. Adjust stable/preview entries and gates as needed. Resolve missing entries and scan errors.
2. Only after all decisions for that repo are complete, update its `SourceReviews` entry in `src/Agentic.Check/SkillManifest.cs`: `CommitSha` and `CommittedAt` come from the exact default-branch snapshot in `skill-discovery-sources.json`; `ReviewedAt` is the actual review time.
3. Rejected candidates become presumed exclusions once present in that new baseline. If any decisions remain pending, do not advance that repo's baseline.
4. Run maintenance again. Nothing advances baselines automatically. These SHAs never pin installation or update versions.

Initial review dates were inferred from manifest history and do not prove that every upstream skill was consciously reviewed. Moves with changed frontmatter names cannot be identified reliably and appear as separate additions/missing entries. Name-only install arguments whose frontmatter name differs from their folder may need explicit paths in the manifest; `gh skill preview` remains the authoritative argument validation.

## Deterministic terminal regression tests

Run `dotnet test tests/Agentic.Check.LiveTests/Agentic.Check.LiveTests.csproj` for the offline tests
and Hex1b terminal suite. GitHub responses are seeded in a per-test cache from working-tree source;
`gh` and companion SDK operations use isolated executable fixtures. Agentic.Check runs through its
real apphost so the process boundary honors these fixtures. Actual SDK/NuGet resolution is tested
separately by `tests/Agentic.LiveTests`, with local packages and isolated caches.

The Agentic.Check command explicitly sets `TERM=xterm-256color` inside the test PTY.
Hex1b 0.165.0's Unix launcher ignores its configured environment overrides; setting TERM
at the command boundary preserves cursor redraws even when the test host has `TERM=dumb`.
No terminal override is needed in the invoking shell or full-suite runner.

Recordings include prerequisite selection/deselection, dry-run, install failure, and version
validation failure. Replay with:

```bash
asciinema play tests/Agentic.Check.LiveTests/TestResults/recordings/<test-name>-<id>.cast
```

The CI workflow runs both normal unit suites (including the offline source path/version contract)
and both live-test projects, and uploads terminal recordings. The two network maintenance tests
remain opt-in. Running the normal suite checks working-tree edits; no Git hook is installed.
