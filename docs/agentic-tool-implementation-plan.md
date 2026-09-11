# Agentic companion tool implementation plan

## Purpose and scope

Implement the agreed first release of a companion .NET local tool and its installation/update through Agentic.Check. This document is the handoff for a new implementation session; no prior conversation is required.

The companion executes deterministic logic extracted from this repository's directives and skills. The first release implements ONLY prompt logging. Do not implement the other previously explored commands for code style, builds, Orleans, Uno, charts, or terminal testing.

This is an implementation specification, not a verification report. It has been amended after implementation review: prompt logging now accepts one complete raw body rather than accumulating JSON-encoded entries. The historical formats remain read-only compatibility paths. The package version remains 2.3.0; the earlier contract was not published.

## Settled decisions

- Next repo release: **2.3.0**.
- New package ID: **InnoWvate.Agentic**.
- New project: **src/Agentic/Agentic.csproj**.
- Invocation: **dotnet agentic**. Set `PackAsTool=true` and `ToolCommandName=agentic`.
- Initial companion package version: **2.3.0**. Bump Agentic.Check to **2.3.0** too.
- Target .NET 10, with use on later supported runtimes considered during tool installation.
- Prefer Agentic.Check's dependencies, patterns, and helpers when appropriate. Do not introduce dependencies merely for symmetry.
- Prefer existing CLI commands for Git and .NET tool operations rather than Git/NuGet service API implementations. Use .NET JSON/XML APIs for structured local data.
- Use a target-local .NET tool manifest. Do not install a global launcher, modify PATH, add aliases, or require activation scripts.
- Reuse the existing skill dependency-selection algorithm for tool and directive dependencies. Do not add a parallel dependency engine, a synthetic prerequisite skill, or a repository-wide version bundle.
- Only directives and skills authored in this repository will declare the companion prerequisite.
- The companion does not discover or read installed skills/directives at runtime. Compatibility is explicit in each invocation.
- The companion never stages files, commits, amends, pushes, or modifies Git configuration. It may perform read-only Git operations for history and validation.
- The agent decides what to commit and executes Git. The companion creates a prompt-log block on stdout or in a file that the agent includes in its commit message.

## Current implementation map

Read these files before editing; preserve unrelated user changes:

| Path | Relevant existing behavior |
| --- | --- |
| `src/Agentic.Check/Agentic.Check.csproj` | Packaging, version, dependencies. Currently System.CommandLine 2.0.9 and Spectre.Console 0.57.1. |
| `src/Agentic.Check/AgenticCheckCli.cs` | Argument validation, help, startup wiring. |
| `src/Agentic.Check/CheckWorkflow.cs` | Scan, recommendations, directive application, skill installs/copies, update flow, dry-run, reports. |
| `src/Agentic.Check/SkillManifest.cs` | Skill identities, stable/preview manifests, dependencies. |
| `src/Agentic.Check/SkillSelectionPrompt.cs` | Shared interactive list; selection/dependency traversal, filtering, scrolling, specialization. |
| `src/Agentic.Check/ScopeDuplicateScanner.cs` | Target specialization duplicate discovery. Not a runtime companion version detector. |
| `src/Agentic.Check/DirectiveInstaller.cs` | Directive planning/application, source resolution, persistent HTTP cache. |
| `src/Agentic.Check/SourceVersionResolver.cs` | Stable release/default-branch source versions. |
| `src/Agentic.Check/CommandRunner.cs` | Injectable CLI execution boundary. |
| `src/Agentic.Check/Interaction.cs`, `ActionOutputFormatter.cs`, `ReportModels.cs` | Tables, progress, action output, JSON report. |
| `src/Agentic.Check/AgentSkillRegistry.cs` | Target skill paths; retain current behavior. |
| `directives/foundation-prompt-log.md` | Authoritative source directive; embedded JSON-line logs, currently implemented with Perl helpers. |
| `AGENTS.md` | Active repo instructions; currently an older standalone prompt-log commit format. |
| `tests/Agentic.Check.Tests` | Existing xUnit tests and manual-test script. |
| `tests/Agentic.Check.LiveTests` | Existing terminal end-to-end tests, Hex1b recordings, and network-dependent maintenance tests. |
| `Directory.Build.props`, `.editorconfig` | Build/analyzer requirements. |

At preparation time, `docs/agentic-check-announcement-x-article.md` is an unrelated untracked user artifact. Do not remove or rewrite it.

## Companion command contract

```bash
dotnet agentic prompt-log wrap --input <file|-> [--prompt-log <file|->] [-m <major.minor>]
dotnet agentic prompt-log show [--since <date>] [--until <date>] [-m <major.minor>]
dotnet agentic prompt-log check [--commit <ref>] [-m <major.minor>]
```

Support ordinary help and version output. Keep them available even when an operation would fail compatibility. Use the CLI framework for validation and help. Register `-m` as the single-character alias of `--minver`, with the same value and behavior, and make the option inherited by all subcommands. Accept both leading placement (before `prompt-log`) and trailing placement. Use trailing `-m 2.3` as the documented convention in directives and examples; do not introduce `-mv` or repurpose `-v`.

### Compatibility

`-m` / `--minver` accepts exactly two nonnegative decimal components, such as `2.3`. Reject patch versions, wildcard suffixes, ranges, missing components, and other invalid forms. Do not retain the earlier proposed `--major-version` or `--minver 2.3-*` syntax.

Compatibility is:

```text
running.Major == required.Major && running.Minor >= required.Minor
```

Ignore patch and prerelease suffix for this check. Thus all of `2.3.0-preview.1`, `2.3.0`, and `2.4.0-preview.1` satisfy `2.3`; `2.2.9` and `3.0.0` do not. A prerelease with a given major/minor is expected to implement that minor's command contract.

When omitted, default the effective requirement to the running tool's major/minor. This permits manual and third-party use without compatibility configuration. Provide the effective requirement, whether it was explicit, and the running version/prerelease state to command logic for future backward-compatible behavior.

On incompatibility, fail before performing the command, including file mutations. Report the running version, requested minimum, same-major constraint, and an instruction such as:

```text
InnoWvate.Agentic 3.0.0 is incompatible with --minver 2.3.
Required: major 2, minor 3 or later.
Stop this operation and ask the user to run agentic-check interactively
in the intended target directory, then retry.
```

The directive must instruct agents to follow this failure guidance. The tool does not launch Agentic.Check or self-update.

### prompt-log wrap

- `--input` contains the complete already-sanitized, human-readable raw log for one commit. Support `--input -` for stdin.
- The agent chooses entries, pairs questions and answers, sanitizes sensitive content, and formats the body for readability. No internal entry schema, JSON serialization, marker insertion, or escaping is required of the agent.
- `--prompt-log` is the output block file, or `-` for stdout (the default). File input and output work independently of stdin/stdout. Named output is replaced, never accumulated or appended to.
- The tool adds exactly one full-line `prompt-log:` / `prompt-log-end:` pair and the immediately following format identifier `prompt-log-format: raw-v1`. It handles reversible escaping entirely internally.
- In the body, prefix a delimiter line (including one with trailing whitespace) with one backslash so Git cleanup cannot turn content into a delimiter. Also prefix every line already beginning with a backslash with one additional backslash. Leave other lines unchanged. The reader reverses this transformation and rejects invalid escapes.
- Preserve all text and meaningful whitespace, including repeated blank lines and final empty lines. Normalize CRLF, LF, and lone CR to logical LF. Use UTF-8 input/output.
- Insert one framing LF before the closing delimiter independently of the body's existing final LF. This permits exact round trips after newline normalization before Git cleanup. Normal Git whitespace cleanup is acceptable; `show` preserves the resulting stored text. Accept a whitespace-only body that Git has reduced to one empty line.
- Zero-byte input produces empty output, with no block. For a named output, clear it atomically so a previous log cannot be reused accidentally. Preserve whitespace-only input.
- Buffer the complete input before output. Input/compatibility failures emit no block; callers must discard output on any nonzero exit, including partial stdout write failures.
- Replace named output atomically; failure must preserve the previous file. Reject input/output referring to the same file, including supported alias checks. Concurrent file writers must fail clearly rather than race.
- This command needs no Git repository, Git access, extra executable, or package dependency.

Example output:

```text
prompt-log:
prompt-log-format: raw-v1
First prompt, with "quotes" and raw multiline text.

Q: Keep the setting?
A: Yes.
prompt-log-end:
```

The agent includes this block unchanged in its initial commit message, before Git trailers. Git's normal whitespace cleanup is acceptable. The companion does not assemble the rest of the message or write Git history. Do not add a commit command, structured request envelope, per-entry protocol, or temporary-workspace manager.

### Historical format support

- The raw format identifier is explicit; do not infer raw versus historical JSON from whether the body happens to look like JSON.
- Retain reading and checking of the original JSON-string-line blocks, including the original Perl helper's final blank separator. Retain numbered standalone prompt-log commits as raw historical text.
- Isolate both historical readers in `LegacyPromptLogReader.cs`, with documented dispatch points in `PromptLogReader.cs` and dedicated tests so support can be removed later.
- Unknown format identifiers are errors, not a reason to guess or fall back silently.
- Escaping protects block framing and round-trip fidelity. It is not a prompt-injection security boundary. Retrieved logs are historical content, like repository files.

### prompt-log show

- Use the Git CLI in the current working directory; normal Git behavior supports invocation from a repository subfolder or worktree.
- Retrieve logs from current reachable history, in chronological order, optionally filtered by `--since` and `--until`.
- Display full commit hash, ISO 8601 committer datetime including timezone, and status/legacy label, then the unescaped complete body in full. Preserve the agent-authored multiline content and spacing; do not parse internal entry boundaries. Omit ordinary commit subjects, bodies, and trailers.
- Do not truncate, summarize, re-encode entries for human display, or interpret entry text as Spectre markup.
- Ignore commits with no prompt logs. Report malformed logs with the commit identity and fail; do not silently discard malformed content. Prefer continuing to show other valid logs while returning a failure status.
- Support older standalone prompt-log commits as legacy read-only history. Preserve their textual entries; do not pretend the older raw format has the current JSON-format guarantees.
- Use structured/framed Git output so arbitrary message text cannot confuse commit boundaries. Support both ordinary Git hash formats rather than assuming every object ID has 40 characters.

### prompt-log check

- Use read-only Git to retrieve the specified commit; default to `HEAD`.
- Validate embedded markers, the format identifier, and reversible escaping. Do not impose an internal entry or Q&A schema on raw logs. Apply the historical format rules when reading historical logs.
- Missing prompt log is reported but succeeds: not every commit requires a log.
- Recognized legacy standalone logs are reported as legacy, not rejected solely for using the old format.
- Invalid revisions, malformed current-format logs, missing Git, or unavailable Git repositories are errors.
- This validates structure, not sanitization, completeness, Q&A pairing, or whether the correct files were committed.

### Process/output defaults

- Exit codes: 0 success; 1 operation, compatibility, or log-validation failure; 2 invalid CLI arguments.
- Diagnostics go to stderr. Generated/decoded content must not be polluted by banners, progress, or decorative output.
- Preserve useful Git errors in diagnostics; do not expose temporary files or leak input through command-line interpolation.
- Invoke Git using a process argument list, not a shell. Validate revision input and pass options safely so refs cannot inject Git flags.
- Drain stdout and stderr concurrently and clean up child processes on cancellation. Follow the command-runner interface pattern, but do not copy existing process-handling shortcomings blindly.

## Agentic.Check integration

### Architectural limits

Implement one companion installer, one source-project version reader, one major/minor compatibility check, and a small extension to the existing dependency graph.

- The only new package handled in this release is `InnoWvate.Agentic`, installed locally. Use one known package identity; do not introduce a configurable package registry or installer-provider framework.
- The version reader handles this repository's known `src/Agentic/Agentic.csproj` path through the existing source resolution and cache. Do not introduce pluggable version sources.
- Compatibility is the comparison specified above. Do not implement version-range solving, negotiation among consumers, or automatic reconciliation of conflicting requirements.
- Keep dependency node identities independent of how their actions are installed. Dependency traversal belongs to the existing graph; invoking `gh skill` or `dotnet tool` belongs to action execution. A small explicit distinction between existing skill actions and the companion action is sufficient.
- Preserve injectable command/source boundaries needed for tests, but do not extract a general framework merely because more tools might be added later.
- A possible future `InnoWvate.Dna` package could be an optional global-tool node depending on the local `InnoWvate.Agentic` node. This is NOT part of this release: do not implement its node, packaging, global installation, configuration, or runtime resolution now. The existing graph should be reusable when that feature is specified; installation scope can be added then. A future global tool would still need to resolve/check its local prerequisite in the working target at runtime.

### Dependency identity and selection

The dependency declaration records that a consumer needs **InnoWvate.Agentic**, not its version. Do not add tool-version YAML metadata to skills or comments to directives.

Initially only `foundation-prompt-log` is a real consumer. Do not mark unrelated skills as consumers merely to populate tests. Use test fixtures for dependent-skill scenarios until actual skills use the tool.

Extend the existing selection graph's input only enough for skills and directives to reference the single companion prerequisite node. Reuse `SelectWithDependencies`, dependent deselection, and the same list interaction. Adapt the existing workflow dependency closure for these actions, using stable node identities independent of installation. Do not implement a second selection system for tools or a generic package-management model.

- Selecting a dependent action selects the necessary companion action.
- Deselecting the companion action deselects dependent actions through the same existing behavior.
- Install/update the companion once per target, even with multiple consumers or multiple agent skill paths.
- Preserve selection, filtering, scrolling, dependency propagation, and specialization for existing items.
- A tool prerequisite is not a duplicate skill file and must not be deselected just because a consumer exists in a parent/child scope.
- `--yes` and dry-run must use the same dependency closure even though they bypass the interactive prompt.
- No dependent actions selected means no automatic tool update, except an explicitly selected prerequisite repair.

### Required version: one source-project read

When dependent content requires a tool action, fetch `src/Agentic/Agentic.csproj` from **the same selected source revision of this repo as that content**:

- Stable: the resolved release/tag, respecting the existing stable source policy.
- Preview: the resolved default branch, never hardcode `main`. Reuse a resolved commit identity where available to avoid inconsistent reads within one run.
- Use the existing persistent HTTP cache, including its duration setting and applicable stale-response behavior. Cache keys must distinguish refs; do not use stable cached data as preview or vice versa.
- Read once per source revision/run; do not fetch every dependent skill's content or clone repositories to discover the version.
- Keep the well-known repo-relative project path in one production constant used to construct the GitHub request. The local source-location contract test must use that same constant and the production XML version parser.
- Parse a single explicit `<Version>` with the .NET XML API. Require it to be literal and valid; do not evaluate arbitrary MSBuild to get it.
- Add this concise XML comment immediately above `<Version>` in `src/Agentic/Agentic.csproj`:

  ```xml
  <!-- Agentic.Check reads this literal version from this GitHub path to install a compatible tool.
       Keep it explicit here (no imports/expressions); update consumer -m values when major/minor changes.
       If this file moves, update Agentic.Check's source path; contract tests verify both. -->
  <Version>2.3.0</Version>
  ```

- Derive required major/minor from that version, ignoring patch/prerelease for compatibility.
- A missing/ambiguous/invalid project version fails dependent actions rather than guessing.
- Use existing source-resolution information where possible; avoid authenticated GitHub requirements or new redundant metadata calls.

The source consistency test described below guarantees agreement between this project version and authored command invocations. Agentic.Check must not infer a selected remote source's requirement from its own executable version.

### SDK installation/update

Use the actual `dotnet tool` CLI and normal NuGet configuration/source behavior.

| Agentic.Check mode | Required source major/minor | SDK `--version` |
| --- | --- | --- |
| Stable | 2.3 | `2.*` |
| Preview | 2.3 | `2.*-*` |

The pattern includes newer minors within the same major. Preview allows both stable and prerelease packages. Let the SDK resolve the highest matching package; do not reproduce NuGet feed resolution with HTTP APIs.

- Always use the explicit target manifest path.
- Create a target manifest explicitly when necessary; .NET's automatic manifest discovery/creation can choose an ancestor, which is inappropriate here.
- Preserve unrelated manifest entries and existing `isRoot` settings. For newly created nested manifests, permit discovery of unrelated parent tools.
- Whenever a dependent skill/directive is selected for installation or update, run the appropriate companion install/update even if its current version already satisfies the minimum: the desired behavior is the latest matching package.
- Allow downgrades, including repairing a manual major-version upgrade and switching preview back to stable.
- Respect the target's `nuget.config` and configured sources; do not hardcode nuget.org as the only source.
- After success, read the **resolved exact version from the manifest**, not localized CLI output. Verify major/minor compatibility and the requested stable/preview channel.
- The manifest records the installed version for reproducible restores; it does not prevent compatible updates. Restore uses that exact recorded version. Installing/updating dependent content still invokes the SDK with `major.*` or `major.*-*`, resolves the latest matching package, and updates the manifest to record the result.
- Do not change dependent skill/directive content until the prerequisite has succeeded and passed validation.
- If no suitable package is available, a CLI operation fails, or the installed version is below the required minor, report an error and skip dependent actions. Unrelated actions may continue.
- If the companion changed before validation failed, report that partial outcome accurately. No distributed rollback mechanism is required.

### Repair and existing update flows

Handle a companion whose installed version is recorded in the manifest but is not restored, and existing dependent content whose prerequisite is absent/incompatible. Do not rely solely on manifest presence for availability. Prefer SDK restore/execution checks over assumptions about NuGet cache directories.

Keep repair handling narrow:

- If the target manifest already records a compatible installed companion version and only restore is needed, restore that exact version. Do not query a newer source release merely to restore it.
- For selected source-content updates, use the remote source project version as above and install/update the latest matching package.
- Only when repairing existing consumers without a compatible recorded installed version, inspect an already-installed known consumer's literal `-m` / `--minver` locally, accepting either spelling and leading or trailing placement. Do not restore a version known to be incompatible. Do not infer an older installed consumer's requirement from a newer release's major version.
- This local fallback is not a duplicate remote skill-content fetch and does not add runtime scanning to the companion. If requirements conflict, report the conflict; do not add a version solver, automatic reconciliation, or repository-wide synchronization.

Stable skill updates currently bypass recommendation selection: after handling that list, `RunSkillUpdateAsync` separately asks "Update these skill(s)?" and invokes `gh skill update --dir ... --all`. An installed skill can therefore be updated even when the recommendations list has no selected actions.

Route those confirmed update actions through the **same dependency resolution and prerequisite execution used for selected recommendations**, before invoking `gh skill update`. Use the already-discovered update candidates and existing dependency declarations to identify required prerequisites; do not add a second dependency mechanism or additional remote skill-content reads. Preserve the existing display-only update list and confirmation prompt; this integration does not require merging it into the interactive recommendations list.

If the user declines updates, do not perform prerequisite actions solely for those updates. If the user accepts, ensure the required companion installation/update and version validation succeed before updating dependent skills. Reuse a successful prerequisite operation already performed for the same requirement in this run rather than repeating it. If `--all` would update dependent skills after a prerequisite failure, skip that directory update and explain why.

Initially this update concern is tested with fixtures because prompt-log is a directive, not a skill. Include a regression test where no recommendation actions are selected but an installed tool-dependent skill has an update: accepting the separate update prompt must resolve and satisfy its prerequisite first. Also test declining the prompt and prerequisite failure, asserting that dependent update commands do not run. Keep changes scoped to enforcing the prerequisite.

### Reporting and dry-run

- Surface companion installed/required version and planned install/update/restore alongside existing status/recommendations.
- Include the prerequisite in the existing action count/output/progress and JSON report, once per target. Use existing styling and spacing conventions.
- Dry-run may resolve cached source version information, but must not create a tool manifest, restore/install/update tools, or write dependent content. Report the pattern and minimum that would be used; do not promise an exact newest version unless actually resolved without mutation.
- Preserve normal agent detection, target-relative paths, skill installation/copy behavior, and existing UI behavior unrelated to this feature.
- Do not add global PATH setup, repo-root resolution, or Git initialization prompts.

## Directive and documentation changes

Replace the Perl helpers in `directives/foundation-prompt-log.md` with concise invocations of the three commands, always using literal trailing `-m 2.3` in executable examples.

Retain the agent's responsibilities:

- Choose relevant prompts since the previous logged work; omit continuation prompts and a final commit/push-only request as the current source directive specifies.
- Pair answers with their questions, sanitize sensitive content, preserve everything else verbatim, and check completeness.
- Collect the complete free-form log and send it as raw text to one `wrap` invocation, preferably through stdin/stdout when supported by the harness. Otherwise use one input file and one output file, outside staged work.
- Check the command exit code before using the result. Each invocation replaces its named output, and empty input clears it.
- Include the block in the initial commit message, then use normal agent-controlled staging/commit operations.
- Keep any Git trailers at the end of the complete message. Git's normal whitespace cleanup is acceptable. This is a short directive note, not companion trailer-manipulation logic.
- Stop on compatibility failure and ask the user to run Agentic.Check interactively; do not bypass it by removing `-m` / `--minver`.

Correct the source introduction that still describes an accompanying separate commit. Do not introduce a replacement prompt-log skill solely for dependency management.

Add `src/Agentic/README.md`, update Agentic.Check's README/help for the new prerequisite and version behavior, and update the root README as needed for the companion. Explain local-tool restore and development package feeds.

The active repo `AGENTS.md` is an older installed directive and governs the implementation session. Default: leave that installed file unchanged unless the user explicitly requests migration; changing authored source is in scope. Do not silently change active agent instructions to bypass their commit requirements. There is no requirement to commit implementation changes automatically.

Do not bulk-bump unrelated plugin/skill versions. Apply existing repository release conventions to any actually changed publication metadata; both tool package versions are explicitly 2.3.0.

## Required verification

### Companion unit and Git integration tests

Create `tests/Agentic.Tests` using existing test conventions.

- CLI parsing/help: missing/unknown parameters, invalid `-m` / `--minver`, valid default, operation exit codes. Verify both aliases in leading and trailing placement for all three subcommands, equivalent compatibility behavior, and help documenting both spellings.
- Compatibility matrix: lower/equal/higher minor, different major, patch ignored, prerelease ignored, explicit/default requirement exposed to handlers. No writes or Git calls on incompatibility.
- Raw framing: quotes, backslashes, Unicode, repeated blank lines, trailing newlines, CRLF/LF/lone CR, exact and near-match delimiters, escape-prefix collisions, JSON-looking raw text, explicit format routing, and unstructured Q&A text. Verify that real Git commits with normal cleanup preserve valid framing, including delimiters with trailing whitespace and whitespace-only bodies. Verify exact round trips and independently specified wrapped output.
- I/O: all file/stdin/stdout combinations, default stdout, no per-entry files or Git dependency, complete replacement instead of append, and empty-output replacement. Failures: missing/invalid input, denied access, identical input/output, interrupted write, competing access, and incompatible version. Failed operations preserve existing output and do not emit partial stdout before input validation.
- Reading/validation: multiple commits and entries, exact decoded text, ordinary commits without logs, legacy standalone logs, malformed current format, invalid refs, date filtering, non-repository folders, missing Git, repository subfolders/worktrees, and Git messages with trailers after the block.
- Use real temporary Git repositories for history behavior. Tests can create commits as fixture setup; production code must perform only read-only Git operations.
- Verify literal text containing markup characters is not interpreted or truncated.

### Local source-location contract test

Add an offline test to the normal Agentic.Check test suite that validates GitHub's well-known source location against the current working checkout, including uncommitted edits. It must fail locally when the project file is renamed/moved or its version declaration becomes unreadable, without requiring a push or a network request.

- Resolve the checkout root independently of the companion project location, then append the exact repo-relative path constant used by production GitHub requests (initially `src/Agentic/Agentic.csproj`).
- Require the file to exist at that exact path, including path-component casing. Detect case-only renames even on a case-insensitive development filesystem, since remote paths may differ in case sensitivity.
- Read the actual working-tree file and pass it to the same XML/version parser used for fetched GitHub content. Require one explicit, valid literal `<Version>`.
- Do not search for a moved `.csproj`, use build-output copies or assembly versions, fall back to cached/GitHub content, or duplicate the path/parser in the test. Those fallbacks would hide the regression this test is intended to catch.
- On failure, identify the expected path and explain that Agentic.Check retrieves the package version from this location on GitHub. A deliberate move requires updating the production path and related references together.
- Also test the GitHub request construction with a fake transport to ensure it actually uses the shared path for stable and preview refs. No real GitHub access is needed.
- Run this test with the regular local suite and CI. It catches the problem before pushing when that suite is run; do not add a Git hook or claim the test executes automatically on every file change.

### Source version/consumer consistency test

Implement the explicitly requested in-repo test:

1. Read the source package `<Version>` from the working-tree path validated by the local source-location contract test, using the same production path constant and XML/version parser.
2. Inspect every authored skill under `plugins/**/skills/**/SKILL.md`, relevant authored skill scripts/assets with invocations, and every source directive under `directives/`.
3. Find actual companion command invocations, including multiline examples, require a literal `-m` or `--minver`, and compare its major/minor to the package version. Recognize both spellings independent of leading or trailing placement; use trailing `-m` in newly authored examples. Include detection fixtures for both spellings and placements.
4. Check that each invoking skill/directive is declared as tool-dependent in Agentic.Check and that required consumers are not missing declarations.
5. Make the test non-vacuous: assert the prompt-log directive contains the expected wrap/show/check invocations. Use synthetic skill fixtures to test discovery until real skills invoke the tool.
6. Do not apply this test to installed historical copies in `.agents`, unrelated documentation quoting old versions, or intentional negative test fixtures. Do not hide invalid authored invocations behind broad exclusions.

Use a narrow, documented invocation convention and fixtures covering detection rather than pretending to parse every possible shell program. No version metadata is introduced by this test.

### Agentic.Check tests

- Source project version parsing: stable/preview source ref, default branch not named main, cache reuse, disabled-cache setting, missing/malformed/ambiguous versions, fetch errors.
- Derived install patterns and acceptance/rejection of actual manifest versions, including same-major downgrade, wrong-major downgrade, preview-to-stable, and highest available version below the minimum.
- Multiple consumers select one prerequisite; deselecting prerequisite deselects consumers; transitive dependencies, select all/none, filtering/scrolling, specialization, `--yes`, and dry-run retain existing behavior.
- Directive-only dependency works; no fictitious production dependent skill is required.
- Precondition ordering: failed tool install or version verification cannot mutate dependent content or run dependent skill updates.
- Explicit target manifest, existing unrelated entries, nested manifests/parent discovery, missing restore, repair-only older requirements, custom target paths and `--skills-dir`.
- All/none selected, no dependencies, partial failures, useful action/error output, and JSON report fields.

### Real local-package and terminal end-to-end tests

Read and use `.agents/skills/cli-e2e-testing/SKILL.md` for terminal tests. Use Hex1b and preserve replayable `.cast` recordings as standard artifacts. Keep Hex1b out of production dependencies.

- Create `tests/Agentic.LiveTests` if a separate companion test project makes ownership clearer; follow existing test patterns.
- Run the real packaged companion via `dotnet agentic` from a local manifest, including from child folders.
- Record help, complete raw-log preparation from file and stdin, stdout output, raw and historical history show/check, invalid arguments, and incompatibility/error output. Test generated files and exit codes as well as terminal text. Add a shell-independent real-process stdin/stdout round trip using a large Unicode body, literal delimiters and escape prefixes, a fixture Git commit from captured output, exact displayed body and commit metadata, and no per-entry files.
- Extend Agentic.Check terminal tests to cover prerequisite selection, cancellation/deselection, dry-run, installation failure, and version-validation failure without dependent mutations.
- Use a temporary `nuget.config` and local package directory with uniquely versioned package fixtures. Exercise actual SDK floating resolution, install, update, downgrade, restore, and exact manifest verification.
- Test stable `2.*` and preview `2.*-*` resolution with stable/prerelease fixture versions; verify the runtime trailing `-m 2.3` behavior independently of installation channel and retain coverage of the `--minver` alias.
- Isolate NuGet caches/configuration and Git identity in tests. Routine tests must not depend on publishing the real package or waiting for GitHub rate limits.
- Fake external GitHub/gh operations using existing boundaries where appropriate; real package tests must not fake the `dotnet tool` resolution being tested.
- Development may include a local feed alongside nuget.org. Resolution chooses the highest version across feeds, so a published stable version can outrank an earlier prerelease. Use unique versions to avoid stale package cache contents.
- Follow the existing distinction between deterministic e2e tests and optional network-dependent maintenance tests. Report any unavailable platform/network validation honestly.

## Implementation sequence

1. Read active repo instructions, relevant files, and testing skill. Inspect the worktree. Do not disturb unrelated changes.
2. Add companion packaging, CLI, compatibility context, wrapper/parser, and read-only Git commands with focused tests.
3. Add the single companion prerequisite node to the existing dependency selection model, including directive dependencies. Implement its specific local-tool installer and known source-project version reader with testable command/source boundaries; follow the architectural limits above.
4. Integrate planning, prerequisite-first execution, reporting, dry-run, and existing update/repair paths in Agentic.Check.
5. Replace source directive helpers, add dependency declaration and source consistency tests, and update docs/help and package versions.
6. Build and run unit, Git integration, real local-package, and terminal e2e tests; run existing Agentic.Check regression suites. Fix regressions without weakening meaningful assertions.
7. Pack both Release NuGets after verification. Report artifact paths, test results, recordings/replay commands, and any remaining limitations.

Do not publish NuGets, create releases/tags, commit, or push unless explicitly requested. A future publishing sequence should make the matching package available before exposing consumers that require it.

## Build and validation commands

Follow the active `AGENTS.md` foreground-dotnet and analyzer instructions. Check the prescribed `.editorconfig` baseline immediately before the first build. Resolve all build errors/warnings according to those instructions. Use sandbox escalation when necessary; do not hide blocked commands in background sessions.

Expected commands, adjusting test project names only if the final organization differs:

```bash
dotnet build src/Agentic/Agentic.csproj --configuration Release
dotnet build src/Agentic.Check/Agentic.Check.csproj --configuration Release
dotnet test tests/Agentic.Tests/Agentic.Tests.csproj
dotnet test tests/Agentic.LiveTests/Agentic.LiveTests.csproj
dotnet test tests/Agentic.Check.Tests/Agentic.Check.Tests.csproj
dotnet test tests/Agentic.Check.LiveTests/Agentic.Check.LiveTests.csproj
dotnet pack src/Agentic/Agentic.csproj --configuration Release
dotnet pack src/Agentic.Check/Agentic.Check.csproj --configuration Release
```

Use isolated fixture packs during integration tests without altering the committed 2.3.0 version. Consult the SDK documentation if runtime-roll-forward or local-tool flags vary with the installed SDK.

## References

- .NET local tools: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools
- Tool install: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install
- Tool update: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-update
- NuGet floating versions: https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution#floating-versions
- Git history: https://git-scm.com/docs/git-log
- Existing authoring source: `directives/foundation-prompt-log.md`
- Terminal-testing instructions: `.agents/skills/cli-e2e-testing/SKILL.md`

## Completion criteria

- The three documented companion commands work, and production Git usage is read-only.
- Wrap frames one complete raw log with reversible escaping and explicit format identification, default stdout, and atomic file replacement. Historical readers are isolated for later removal. No per-entry protocol, commit-message formatter, or Git writer was added.
- Every authored consumer invocation has the package's major/minor through `-m` or `--minver`, enforced by tests. Newly authored examples use trailing `-m`; both spellings and leading/trailing placement work.
- An offline working-tree contract test fails if the source project is missing, renamed/moved without updating the production GitHub path, or no longer contains a version readable by the production parser.
- Agentic.Check reuses dependency selection, fetches one source project version, delegates package resolution to the SDK, and verifies the exact manifest version before dependent mutations.
- Stable/preview installs, downgrade, missing restore, failures, dry-run, and nested target behavior are covered by tests.
- No runtime skill scanning, version metadata, global launcher, or repository-wide bundle/synchronization was introduced.
- No configurable package registry, installer-provider framework, pluggable version-source system, or version solver was introduced. Node identities remain independent of installation, but the future `InnoWvate.Dna` tool is not implemented.
- Both 2.3.0 Release packages build without errors/warnings; tests and terminal recordings are reported.
