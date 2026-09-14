# Package candidates and installed-fixture end-to-end test plan

## Purpose

Implement a collection of repositories installed using the published Agentic.Check 2.2.0 tool, and tests that migrate independent temporary copies using locally built package candidates. Exercise real `gh skill install` and `gh skill update`, real .NET tool installation, and observable terminal behavior. Before a release reaches users, test the exact candidate packages together with this repo's candidate skills and directives from the same committed source revision, pushed to a development branch at origin.

This is a self-contained handoff for a new implementation session. Read the active `AGENTS.md` and `.agents/skills/cli-e2e-testing/SKILL.md` first. The earlier `docs/agentic-tool-implementation-plan.md` explains production behavior. This plan authorizes only the narrow production extension described below; it does not reopen the earlier production implementation scope.

## Scope and decisions

- Implement fixtures, test utilities, test projects, and test documentation under `tests/`. The agreed exception to the original tests-only constraint is a minimal production `--preview-source-ref` option in Agentic.Check and the source-resolution plumbing needed for it. Include this option in the real Release package, not in a special test build.
- Do not modify authored directives/skills, root build configuration, active `AGENTS.md`, or `.github/workflows/`. Do not add other production switches, dependency providers, arbitrary repository/endpoint overrides, or a GitHub emulator/proxy. Document the internal option only in test/developer documentation under `tests/`.
- Preserve unrelated changes. At planning time `docs/agentic-check-announcement-x-article.md` is an unrelated untracked user file.
- Do not publish NuGet packages or create releases/tags/hosted fixture repositories. The candidate-source tests require source to be committed and pushed to a development branch in the existing origin repository. This is a readiness requirement, not automatic authorization for the implementing agent to commit or push: prepare the concrete changes and local verification first, then have the user perform that step or obtain explicit authorization if it has not already been given. Git commits inside disposable fixture repositories remain authorized test operations.
- Use the actual published **Agentic.Check 2.2.0 package** for fixture preparation, not a rebuild of the source tag.
- Label snapshots **"Installed using Agentic.Check 2.2.0 on [actual UTC date/time]"**. This is not a reconstruction of the upstream world at the 2.2.0 release date. Record the sources actually installed.
- Main package tests must consume the exact supplied candidate `.nupkg` files. Pack once before verification; do not rebuild, rewrite, relabel, or replace candidates inside those tests.
- Release candidates are required for release verification. The same package-input contract should also accept locally packed Debug packages for developer diagnosis. The test runner's configuration is independent of the package configuration.
- Keep prompt/skill content evaluation out of this e2e task. E2e verifies delivery of the intended files/assets, source metadata, prerequisite handling, installation/update behavior, and substantive tool execution. Deterministic content/contract tests check embedded commands, minimum versions, references, and dependency declarations; agent evals assess how well an agent follows instructions. Those latter two may be grouped in a separate eval suite, with distinct results. Retain existing content/contract tests, but do not implement a new agent/content eval framework here or claim that file delivery proves instructional correctness.
- A source repository's skills do not automatically depend on the companion. Honor actual dependency declarations. Currently `foundation-prompt-log` is the only declared real consumer. External skills do not use the companion; directives originate only in this repo.
- NuGet artifact upload and CI changes are a later task. The existing workflow packs both tools after running tests and uploads recordings only.

### Confirmed fixture scope and no-change policy

The user explicitly confirmed both decisions:

1. Prepare **all fourteen fixtures**: the nine main fixtures, `preview-web-cli`, and all four detector-edge fixtures listed below.
2. If an authentic freshly installed fixture already matches its intended source, explicitly skip the **content-change-specific** test with the source identities and reason. Still run reinstall, preservation, and package checks. For external upstream updates, this can mean waiting for newer content. For this repo, compare against the pushed candidate commit immediately: changes already present there must be tested before release, without waiting for default-branch or NuGet publication. Do not silently manufacture older content or modify tracking metadata to force a difference.

These decisions are settled and do not require reconfirmation in the implementation session.

## Current implementation and existing coverage

Relevant projects and files:

| Location | Current behavior |
| --- | --- |
| `tests/Agentic.Check.LiveTests/AgenticCheckEndToEndTests.cs` | Real Agentic.Check apphost and recorded Hex1b terminal, but fake `gh` and fake companion SDK operations. The fake installer writes a placeholder `SKILL.md`; fake update prints configured output without updating files. |
| `tests/Agentic.Check.LiveTests/ManifestGhSkillTests.cs` | Opt-in real `gh skill preview`, not installation/update. |
| `tests/Agentic.Check.LiveTests/SkillDiscoveryLiveTests.cs` | Opt-in GitHub source discovery/maintenance. Separate from release verification. |
| `tests/Agentic.LiveTests/LocalPackageTests.cs` | Packs the real companion in temporary directories. Some fixtures rewrite NuGet metadata for SDK version-resolution cases. Several tests call `CompanionInstaller` directly. |
| `tests/Agentic.Check.Tests` and `tests/Agentic.Tests` | Existing component, unit, compatibility, framing, and Git regression coverage. |
| `src/Agentic.Check/StackDetector.cs`, `SkillManifest.cs` | Detection, gate rules, and stable/preview manifests. Read the `v2.2.0` versions when validating fixture triggers. |
| `src/Agentic.Check/SkillInstaller.cs`, `CheckWorkflow.cs` | Real command arguments, preview reinstall, stable update, selection and prerequisite ordering. |
| `src/Agentic.Check/SourceVersionResolver.cs`, `DirectiveInstaller.cs`, `CompanionDependency.cs` | GitHub source resolution, existing HTTP cache, authored directive retrieval, and companion source-version lookup. |
| `src/Agentic.Check/AgenticCheckCli.cs`, `AgenticCheckOptions.cs` | CLI registration, explicit known/value option handling, and options passed to the workflow; add the hidden option here. |

Keep the existing deterministic tests. They remain useful for failures and UI branches that should not depend on GitHub. Label their boundaries accurately; do not claim their fake `gh` calls provide real skill installation/update coverage.

The production tool packages currently are `Agentic.Check` and `InnoWvate.Agentic`, version 2.3.0. Inspect the working source rather than assuming that version remains unchanged in a later session.

## Internal preview source option

Implement the agreed hidden option:

```bash
agentic-check <fixture-target> --preview --preview-source-ref <source-ref>
```

Contract:

- Register `--preview-source-ref` with System.CommandLine as hidden from ordinary help. Update the existing explicit known-option/value-option handling as necessary. It still needs normal parsing and validation; do not introduce a separate parser.
- Require `--preview`; reject use in stable mode with an actionable error before installation or content writes. A supplied empty/invalid/unavailable reference is an error.
- Scope the override exclusively to `VincentH-Net/dotnet-agentic-engineering`. Accept a branch reference or commit SHA within that repository, resolve it to a full commit SHA, and use that immutable SHA consistently throughout the invocation. The test harness supplies the verified candidate SHA.
- Apply the same source to this repo's skill installations/reinstallations, directive listing/content, and `src/Agentic/Agentic.csproj` prerequisite-version lookup. Reuse the shared source-resolution objects and existing dependency mechanism. Do not implement three independent resolution paths or alter package compatibility rules.
- Leave external repositories on the existing stable/preview selection rules. With `--preview`, their source is normally their published default-branch content; "published" here means available on GitHub, not necessarily a tagged release.
- Preserve normal behavior when the option is absent. Fail clearly when an explicitly requested source cannot be resolved; do not let existing warning/fallback paths silently substitute the default branch or latest release.
- Resolve/report the requested source and full commit SHA even though the option is hidden. Normal source caching may be reused, but explicit candidate SHA selection must not be replaced by an unrelated cached source.
- Describe the option in test/developer documentation as an internal, unsupported interface. Hiding it controls discoverability, not access. Its narrow scope and validation apply to everyone who invokes it.
- Do not add an equivalent environment-variable override, infer source from the target fixture's branch, or make a special conditional-build package. Tests must exercise the exact package intended for upload.

Add focused tests for hidden help, parsing, preview-only validation, bad/missing refs, no fallback, unchanged ordinary behavior, unchanged external source selection, and consistent SHA use across this repo's skill/directive/prerequisite consumers. Refs containing ordinary branch path separators must be handled safely. Keep changes limited to the option, necessary source plumbing, and its tests.

## Fixture collection

Store reusable trigger definitions separately from installed baseline snapshots, for example under `tests/fixtures/definitions/` and `tests/fixtures/baselines/<baseline-id>/`. Use an explicit baseline ID containing the installer version and preparation date, such as `agentic-check-2.2.0-<UTC-preparation-date>`; distinguish multiple captures on the same date with a timestamp or unique suffix. Each installed snapshot records its trigger definition, preparation metadata, and preserved installed result. Treat fixture instructions as test data; do not execute skill scripts or follow installed directive text while preparing/asserting snapshots.

**All 14 fixture definitions test candidate content with `--preview --preview-source-ref <candidate-sha>`, both on fresh targets and when migrating installed snapshots.** The baseline installation channel only describes the saved starting state. Separate copies cover published stable updates and preview-to-stable switching without the override.

| Fixture | Baseline installation channel | Agents | Candidate-content test mode | Minimal trigger content | Intended coverage |
| --- | --- | --- | --- | --- | --- |
| `broad-stack` | Stable | Codex and Claude | Preview + candidate SHA | Separate console project with explicit `OutputType=Exe`; Web SDK project; ordinary project referencing `Microsoft.Orleans.Server`; Uno SDK project with `UnoFeatures=MVUX;CSharpMarkup;Material`. | All five technology detections, all four directive types present at 2.2.0, many non-conflicting skills, all four skill source repositories, dependency selection, copies in both agent directories. |
| `foundation-only` | Stable | Codex | Preview + candidate SHA | README only; no project, props, or targets files. | Foundation directive without .NET/Uno gates; companion introduction when the candidate directive is installed. |
| `dotnet-library` | Stable | Codex | Preview + candidate SHA | Ordinary `Microsoft.NET.Sdk` project targeting `net10.0`, no executable output or special references. | General .NET directives/skills and test-skill dependencies; no CLI gate. |
| `dotnet-cli` | Stable | Codex | Preview + candidate SHA | Ordinary .NET project with explicit `OutputType=Exe`. | CLI gate and `cli-e2e-testing` in addition to general .NET content. |
| `aspnetcore-web` | Stable | Codex | Preview + candidate SHA | `Microsoft.NET.Sdk.Web` project, without explicit `OutputType`. | Web SDK detection without the detector's explicit CLI gate. ASP.NET-specific skills are preview-only in the 2.2.0 manifest, so do not expect them in this stable snapshot. |
| `orleans` | Stable | Codex | Preview + candidate SHA | Ordinary .NET project with a `PackageReference` to `Microsoft.Orleans.Server`. | Orleans plus general .NET recommendations, without Web or Uno detection. |
| `uno-defaults` | Stable | Codex | Preview + candidate SHA | Uno SDK project without presentation, markup, or theme features. | Implicit XAML and Fluent; no MVVM/MVUX or C# markup gate. |
| `uno-mvvm-csharp2-fluent` | Stable | Codex | Preview + candidate SHA | Uno SDK project with `UnoFeatures=MVVM`, reference to `CSharpMarkup.WinUI`, no explicit theme. | MVVM, second C# markup variant, XAML, and default Fluent. Complements broad-stack's MVUX/CSharpMarkup/Material. |
| `uno-simple` | Stable | Codex | Preview + candidate SHA | Uno SDK project with `UnoFeatures=SimpleTheme`. | Simple theme, shared semantic-color skill, no default Fluent gate. |
| `preview-web-cli` | Preview | Codex | Preview + candidate SHA | Web SDK project with explicit `OutputType=Exe`. | Preview-only ASP.NET skills, general .NET skills, CLI skill, directives, and sources from this repo plus `dotnet/skills`. |
| `uno-cupertino` | Stable | Codex | Preview + candidate SHA | Uno SDK project with `UnoFeatures=Cupertino`. | Cupertino detection and absence of the default Fluent gate. The 2.2.0 Cupertino toolkit skill is ungated, so its presence alone does not establish Cupertino gate detection. |
| `aspnetcore-framework-reference` | Stable | Codex | Preview + candidate SHA | Ordinary .NET project with `FrameworkReference Include="Microsoft.AspNetCore.App"` and a `.cs` file containing `WebApplication.CreateBuilder`. | Alternative ASP.NET detection without the Web SDK. As with `aspnetcore-web`, ASP.NET-specific skills are preview-only in the 2.2.0 manifest. |
| `aspnetcore-test-exclusion` | Stable | Codex | Preview + candidate SHA | Web SDK project named `Api.Tests.csproj`, without explicit `OutputType`. | Test-project exclusion: general .NET content remains applicable, but ASP.NET detection and recommendations must be absent. |
| `uno-conflicting-gates` | Stable | Codex | Preview + candidate SHA | Two Uno SDK projects: one with `UnoFeatures=MVUX;CSharpMarkup;Material`; another with `UnoFeatures=MVVM;SimpleTheme` and a reference to `CSharpMarkup.WinUI`. | Intentional multi-value warnings for presentation, C# markup, and theme, plus combined recommendations. Keep independent of the non-conflicting broad fixture. |

Use minimal parseable project/source files. These trigger projects do not need to restore or build. Supply explicit SDK/package versions where sensible, but do not install Uno workloads merely to scan them. Do not inject extra gates into generated fixture files.

Foundation is always detected. A `.csproj` enables .NET. At 2.2.0 the general .NET testing skills do not require a test project. Uno always includes XAML and defaults to Fluent when no theme is specified. XAML alongside one C# markup variant is not treated as conflicting. The broad fixture should not select both presentation styles, both C# markup variants, or multiple themes.

Keep the fixture preparation/installation inventory outside the scanned target root when possible. The detector recursively examines project files, and any `tests` path component makes ASP.NET projects look like test projects. Run copied fixtures in temporary paths that do not contain a directory named `tests`.

All fourteen rows are required fixture preparation and test scope, including the four detector-edge cases. The baseline installation channel column describes **baseline preparation**; candidate-content scenarios use preview mode and the candidate SHA even when starting from a stable baseline. Consequently, assert candidate recommendations using the candidate preview manifest rather than assuming the old stable inventory is also the intended final inventory. Retain existing component coverage for those edges as well.

## Baseline design and future maintenance

Implement only the 2.2.0 baseline collection in this task, while keeping the reusable harness capable of selecting a different baseline later. Do not build a baseline-management framework, an exhaustive release matrix, or automated release maintenance.

Required design now:

- Identify baselines explicitly by installer version and preparation date. Keep package identity/version/URL/checksum, actual installed source revisions, preparation options, fixture file hashes, and any installed companion package identity/version/checksum in baseline metadata. Record the absence of a companion explicitly for 2.2.0.
- Keep snapshots immutable. A later preparation creates a new baseline ID and directory, even if it uses the same installer version. Ordinary tests operate on temporary copies and never update the stored snapshot in place.
- Let migration scenarios select a baseline ID, fixture name, candidate package paths, and candidate source commit through the test-side input contract. Avoid hardcoding `2.2.0`, its package URL, or its companion absence in reusable download, copy, install, and migration helpers. Keep those values in this baseline's definition; expectations specific to its historical detector/manifest remain legitimate baseline-specific data.
- Keep fresh-install scenarios independent of installed baselines: build fresh targets from reusable gate-trigger definitions with no skill directories, directive/agent instruction files, or tool manifests. Record which trigger definition was used when a baseline is captured so future trigger changes do not silently alter historical fixtures.
- Distinguish **first companion installation** from **upgrade of an existing companion** using explicit baseline metadata/scenario expectations. When a future baseline contains a real companion, retain its exact published package bytes/checksum in the package cache so it can be restored in an isolated environment before migration. A changed manifest version alone does not prove an executable upgrade.
- Keep the candidate target fixed to the verified pushed commit and exact package hashes, regardless of which baseline is selected. Do not use a moving branch head as an implicit target.

Document this future maintenance policy in the test README:

| Baseline/scenario | Future coverage policy |
| --- | --- |
| Fresh targets | Keep permanently and exercise all applicable gate-trigger definitions against each candidate. |
| Previous published release | After a successful release, intentionally prepare a new dated baseline from its actual published package(s). Use the full fixture collection for the next candidate's normal upgrade coverage. |
| 2.2.0 | Retain representative historical fixtures such as `broad-stack`, `foundation-only`, and `preview-web-cli` while migration from pre-companion installations is supported. This future reduction does not remove the requirement to prepare and test all fourteen fixtures now. |
| Significant migration releases | Retain selected baselines when manifests, source tracking, directive storage, or compatibility rules change meaningfully. Add focused migration expectations when those releases exist. |

Prepare a successor baseline separately; do not overwrite the previous one or turn a mutated test target into the new baseline without an explicit, documented preparation operation. Installed external sources must still be labelled by their actual preparation-time revisions, not assumed to match the release date. Select historical baselines explicitly for direct upgrades so coverage does not rely solely on users having installed every intervening release.

Adding the next baseline should be a documented maintenance operation using these helpers. Preparing additional release baselines, reducing old baseline coverage, and deciding new migration boundaries are future work. A follow-up implementation plan is only needed if those operations require substantial new automation or uncover additional migration requirements.

## Obtaining and preparing the baseline

1. Download `Agentic.Check` **2.2.0** from NuGet.org:
   `https://api.nuget.org/v3-flatcontainer/agentic.check/2.2.0/agentic.check.2.2.0.nupkg`.
   This exact URL returned HTTP 200 during planning on 2026-09-14.
2. Verify embedded package identity/version and compute a cryptographic checksum. Record the URL, checksum, and retrieval date. Cache the original bytes outside tracked fixture content; do not commit NuGet packages or credentials. Reuse only a cache entry matching the recorded checksum.
3. Install the package with the real SDK into an isolated location, using its exact version and a controlled package source. Execute that installed tool to prepare fixtures. Do not call the current source assembly and describe it as 2.2.0.
4. Create each minimal source tree in a fresh temporary Git repository, run the baseline installer in the chosen channel, and accept the intended recommendations. Use recorded terminal automation where interaction is required.
5. Real `gh skill` must perform skill installations, including normal metadata injection and dependency handling. Do not replace missing upstream skills with placeholder files or silently discard failures.
6. Save each successfully prepared installed result. Include `AGENTS.md`, `CLAUDE.md` where applicable, full installed skill directories including assets, and original tracking metadata. Preserve applicable third-party license/notice files.
7. Record exact Agentic.Check, SDK and `gh` versions, OS, UTC timestamps, invocation/options, selected/rejected items, source repository/ref/commit/tree identities where available, and a relative file inventory with hashes. Record warnings and preparation failures separately. A partial installation must not be labelled a completed fixture.
8. Implement the preparation utility in **C# targeting .NET 10**, as a small test-support console project under `tests/`. Reuse the test suite's package, process, fixture, and metadata helpers where appropriate. Invoke the real `dotnet`, `git`, and `gh` executables through process APIs; keep preparation logic portable across Windows, macOS, and Linux rather than implementing it in Bash or PowerShell. Any existing platform limits of interactive terminal automation must still be reported explicitly.
9. Keep the utility test infrastructure: do not publish it as another tool or add a production command. Document its invocation and how to intentionally capture a new dated baseline in the test README. Refuse to overwrite a completed baseline; regeneration uses a new ID. Ordinary tests must not silently refresh or repair baseline fixtures.

Do not pin ordinary stable skills merely to make preparation deterministic: pinned skills are skipped by normal `gh skill update`. The frozen installed snapshot provides the repeatable starting state. Preview installations legitimately contain pins generated by the baseline product; preserve those.

A preparation failure caused by moved/removed upstream skills is a reported baseline/source issue. It does not authorize editing the old package, production manifests, or skill identities. Complete independent fixtures and report affected ones with actionable details.

Do not copy `.git`, tool caches, absolute temporary-path configuration, binaries, authentication files, or logs containing credentials into snapshots. Initialize a new repository when consuming a snapshot. Ensure fixture files are not accidentally compiled by test projects.

## Candidate package inputs and isolation

Implement one documented test-side input contract, for example:

- `AGENTIC_E2E_CHECK_PACKAGE`: absolute path to candidate `Agentic.Check` `.nupkg`.
- `AGENTIC_E2E_COMPANION_PACKAGE`: absolute path to candidate `InnoWvate.Agentic` `.nupkg`.
- `AGENTIC_E2E_BASELINE`: explicit baseline ID for migration scenarios. Resolve its installer/package details from baseline metadata; fresh-install scenarios do not require an installed baseline.
- `AGENTIC_E2E_SOURCE_CHECKOUT`: optional explicit source-checkout location when it cannot be unambiguously discovered. The harness obtains and verifies the candidate commit there, not in the fixture target.
- `AGENTIC_E2E_NETWORK=1`: explicit opt-in for real GitHub skill installation/update scenarios.
- Optional baseline cache, fixture, and report directory overrides under a consistent naming scheme.

Exact names may be adjusted consistently in test code/docs. Missing inputs may skip opt-in suites during an ordinary developer run. Once a package verification run is explicitly requested, missing/invalid candidates are errors, not silent fallback to source builds or another package.

- Verify package IDs, versions, and bytes before use; record hashes before and after testing.
- Install candidate Agentic.Check as a real tool in an isolated tool directory. Invoke that executable, including real interactive flows. Do not substitute its referenced test assembly or internal workflow methods in the main package e2e flows.
- Provide the candidate companion through a temporary local NuGet feed and target-local manifest. Ensure public feeds/caches cannot supply a different package with the same identity/version or outrank the candidate through floating resolution.
- Keep candidate files read-only in practice. Existing synthetic SDK resolution tests can continue using separate copies with modified metadata, but those copies are never upload candidates and are reported separately.
- Use independent temporary target repos, NuGet/HTTP/CLI state, Git identity, and agent home/config locations. Ensure real `gh` cannot update the developer's user-scoped skills. Use only appropriately scoped authentication supplied by the environment; never store it in fixtures or recordings.
- For `gh skill` behavior, use the supported real CLI and record its version. Planning inspected 2.100.0; do not assume identical preview-command behavior in every later version. Verify help/capabilities before preparation.
- Test Debug packages through the same process/package boundary when requested. Do not run every expensive network scenario twice by default.

## Candidate source readiness and provenance

The release gate tests this repo's candidate content in preview mode from an existing non-default development branch at origin. Pushing that source makes it retrievable by GitHub CLI without publishing a NuGet package or changing the source selected by regular stable/default-preview users.

1. In the source checkout, identify the current branch and expected `origin` (`VincentH-Net/dotnet-agentic-engineering`). Validate the actual remote branch/commit, rather than trusting a stale local remote-tracking ref. Accept normal HTTPS/SSH spellings of that same repository; do not broaden the override to forks or arbitrary hosts.
2. Require the candidate source, including the hidden-option implementation, to be committed and available at that origin branch. Reject detached/ambiguous source discovery unless the harness has explicit verifiable branch/commit provenance. Never inspect the fixture target's origin to select candidate sources.
3. Build the exact candidate packages from that committed source revision. Record a test-side build manifest containing the commit SHA, package IDs/versions, configuration, and package hashes. Verify package metadata and relevant source state against that manifest. A package version alone does not prove the package came from the selected commit. Require a clean candidate input tree at pack time, while leaving unrelated untracked user artifacts untouched and outside the build inputs.
4. Resolve the candidate branch to its remote SHA and pass that SHA to `--preview-source-ref`. Verify it matches the package provenance. This repo's candidate directive and skill assertions use that same immutable snapshot; a later branch movement cannot change the selected bytes.
5. Install the unchanged candidate companion through the isolated local NuGet feed. This repo's GitHub source project supplies the normal prerequisite requirement; no fake source version or cache injection is needed in the main e2e flow.

If required source has not been pushed, complete all independent implementation, fixture preparation, local tests, and reviewable changes first. Report the exact remaining readiness step, then resume the candidate gate once it is satisfied. Do not claim the release gate passed by falling back to published stable/default-branch content.

Run the real packaged Agentic.Check, real `gh`, and real SDK in these flows. Main candidate scenarios must not substitute seeded directive/project responses, fake `gh` results, patched skill metadata, or an injected in-process workflow. Existing deterministic component/UI tests may retain their fixtures with honest labelling.

## External source changes and test expectations

External skills use actual published GitHub content under the product's normal channel policy. Their future text and availability cannot be predicted when writing the tests. Installation-date baseline snapshots provide the fixed starting state; live target identities and content provide the expected ending state.

- Obtain expected content and metadata independently from the selected upstream revision, using real GitHub source reads. Record repository, ref, commit/tree identities, and relevant file hashes. Do not use a fixed snippet from the day the test was written, or the installed output itself, as the sole correctness oracle.
- For preview sources, the existing product resolution/pinning selects a commit per repository. Compare against the commit actually selected, independently retrieved. Do not give external repos this repo's candidate override or invent a shared version bundle.
- Ordinary stable `gh skill update` resolves its own latest source and has no target-SHA option. Keep that real path; do not install pins to force determinism and thereby bypass update behavior. Observe source identities around the operation and compare against the actual selected source.
- If a relevant ref changes during the test and prevents a coherent comparison, report source movement separately. At most one retry from a fresh copy is reasonable. Continued movement is an unstable-source/infrastructure outcome, not a passed update or an unchanged-source skip. Do not silently accept mixed or unexpected content.
- A missing/moved skill, invalid metadata, incompatible CLI contract, wrong installed content, or failed command is a real failure with provenance. Distinguish upstream compatibility issues from package defects and network/authentication/rate-limit failures; none count as successful updates.
- If there is no upstream content transition, follow the confirmed explicit-skip policy and still run reinstall/no-op/preservation checks. Never silently refresh the baseline to make a comparison pass.

No GitHub API emulator, endpoint interception, or separate hosted fixture repository is needed. These are live compatibility tests, so results must report their network/source dependence rather than promise permanent reproducibility against moving upstream heads.

## Required scenario collection

### Fresh installation

For each fixture definition, create a fresh target using only its trigger files, without the saved installed content. Run the candidate package with `--preview --preview-source-ref <candidate-sha>` and verify appropriate detected technologies, gates, selected actions, directive placement, skill files/assets/metadata, and exit status against the candidate source. Use the broad fixture for an interactive dependency-selection and two-agent-directory flow; simple variants may use the real non-interactive CLI mode. External repositories follow normal preview source selection.

The suite must include successful real `gh skill install` and real companion local-tool installation. Assert meaningful skill contents/assets and tracking metadata, not only that `SKILL.md` exists. Expected inventories come from the applicable manifest/channel and declared dependencies, not a duplicate hardcoded universal skill list.

### Migration to candidate content from stable baselines

For each of the thirteen stable baseline fixtures, copy the installed result into a fresh temporary repository and run the candidate package with `--preview --preview-source-ref <candidate-sha>`. This is the pre-release migration check for this repo's own content: verify its candidate directive/skill files and assets actually arrive, prerequisite installation precedes dependent content, and the installed companion operates. Preserve unrelated source, text, manifest entries, and deselected content. Do not claim this preview migration tests the stable `gh skill update` path.

### Stable updates against published sources

For each stable fixture, use another independent copy and run candidate Agentic.Check in stable mode **without the hidden override**. Assert preservation of unrelated source/text, correct selected published directive updates, real `gh skill update` discovery/application where changes exist, and companion operation when the selected content requires it. This is published-source stable compatibility coverage; it does not prove this repo's unreleased candidate content is installed by the stable path. If the selected published content requires a companion incompatible with the supplied candidate, report that source/package mismatch rather than substituting another package or changing the requirement.

Give the broad fixture an accepted interactive update path and checks for both agent directories. Add separate copies for declining an available update and for a subsequent already-current check. Every test begins from its own baseline or explicitly prepared post-update state; no order dependency or shared mutation.

### Preview scenarios: all three are required

Use **three separate temporary copies** of the same `preview-web-cli` installed snapshot:

1. **Preview to preview:** run candidate `--preview --preview-source-ref <candidate-sha>`, select the intended reinstalls, verify source resolution and the real `gh skill install --pin <sha> --force` path. Assert candidate file/metadata changes when source content differs. Verify that current-source reinstall and preservation still work when content is unchanged.
2. **Preview to stable:** run candidate stable mode without the hidden option, accept switching applicable manifest skills, verify the published stable source content and appropriate tracking/pin state. Verify an ordinary subsequent stable update check works. Cover both the baseline's preview tracking and a SHA-pinned candidate-preview installation: the latter can be a second variation prepared on another independent copy, not a fifteenth stored fixture. Do not silently invent removal semantics for preview-only skills absent from the stable manifest; preserve/report according to the actual product contract and record any unresolved gap.
3. **Declined/deselected reinstalls:** run candidate preview mode with the candidate SHA and deselect the relevant reinstallation actions. Verify affected skills' full content and metadata remain unchanged. Do not assert that unrelated allowed scaffolding cannot be created unless the product contract requires it.

Agentic.Check handles preview refresh through forced installs after resolving source versions; ordinary `gh skill update` skips pinned skills. Stable and preview tests must drive the actual product path rather than manually invoking `--unpin` to bypass it.

### Companion coverage

- Agentic.Check 2.2.0 predates the companion. Migration from that baseline tests **first installation** of InnoWvate.Agentic, not its upgrade.
- After installation, invoke the packaged companion for `--help`/`--version` and a substantive `prompt-log wrap` / fixture Git commit / `show` / `check` round trip, including stdin/stdout. The Git commit is test setup; the tool itself must remain read-only with respect to Git history.
- Retain existing real SDK install/update/downgrade/restore and nested-manifest regression tests. Their synthetic versions remain explicitly classified as SDK resolution fixtures, not historical product releases.
- Include a clean-cache clone/restore scenario and preservation of unrelated manifest entries. Ensure the exact candidate companion is used.
- A genuine older-companion-binary-to-candidate upgrade cannot be sourced from release 2.2.0. Report that limitation until an actual prior companion package is available; do not mislabel a version-rewritten candidate as one.
- Structure the shared scenario so a later baseline can supply the actual previously published companion package and verify installation identity, version, and operation before and after upgrading to the candidate. Do not prepare such an additional baseline in this task or count this future capability as executed coverage.

## Meaningful assertions and no-change handling

Verify exit codes, terminal output where relevant, installed inventories, file hashes, source tracking metadata, directive framing/placement, and preservation of unrelated content. Compare expected skill body/assets with source content while accounting explicitly for `gh`-injected frontmatter; do not broadly normalize away meaningful differences.

Split assertions so a successful reinstall cannot masquerade as an actual update. Before running a content-transition-specific test, determine whether the fixture and intended source differ. If unchanged, report a genuine test-runner skip with the fixture and source IDs. For external content this may remain skipped until newer upstream content exists. For this repo the comparison is against the pushed candidate SHA: verify existing candidate changes now and never skip them merely because the stable/default branch still has the old content. Separately execute applicable reinstall/no-op/preservation checks. Avoid `return` paths that the runner reports as passed.

Distinguish unchanged source from authentication errors, missing skills, rate limits, malformed output, failed commands, or wrong content. Once opted in, those are failures or clearly reported infrastructure blockers, not "no update" skips. Detect/report source movement during a run rather than comparing against an unrelated later download.

Do not weaken assertions, silently refresh the baseline, hand-edit tracking fields, or suppress failing meaningful tests to make the suite green. Fix implementation defects in the newly authorized hidden-option/source-resolution extension. Other production defects require a reproducer and expanded user authorization before product changes; the narrow exception is not blanket permission to repair unrelated behavior. Continue independent work.

A complete report must distinguish passed installation/reinstall checks, passed real content transitions, no-change skips, platform skips, and failures. It must not say all update behavior is verified if actual update transitions were skipped.

## Terminal recordings and platform handling

Use the CLI e2e skill's Hex1b pattern for interactive flows. Use ordinary child processes with redirected stdin/stdout for non-interactive package/round-trip checks. Preserve `.cast` recordings outside disposable targets; include the test name and unique ID. Report `asciinema play <path>` commands.

Do not modify global Git configuration or agent installations. Clean up test processes and temporary files reliably on cancellation/failure. Keep useful failure reports outside disposable workspaces without leaking authentication data.

The current PTY tests use Bash on macOS/Linux and return early on Windows. For the new suite, unsupported platforms must be explicit skips. Keep non-interactive package/process tests portable. Full Windows PTY support and a new CI OS matrix are not required for this task; report exactly which platforms were actually executed.

## Implementation sequence

1. Read instructions and the e2e skill; inspect status and preserve unrelated changes. Follow the confirmed decisions above and any subsequent explicit user amendments.
2. Implement the hidden preview-source option and minimal shared resolution changes, with focused local tests. Verify ordinary help and ordinary source selection remain unchanged.
3. Add test-side package/provenance validation, explicit baseline selection, metadata-driven baseline download/cache verification, isolation, immutable fixture capture, and preparation helpers. Separate fresh trigger definitions from installed snapshots. Reuse existing patterns without extracting a new general-purpose framework.
4. Prepare and validate all fourteen installed snapshots using real 2.2.0 and real `gh`. Preserve complete results and explicit preparation metadata. Report any upstream failures. This work does not depend on pushing candidate source.
5. Implement fresh candidate-preview installation, stable-baseline-to-candidate-preview migration, published-source stable updates, and all three independent preview scenarios. Include external source-movement detection and honest no-change outcomes.
6. Adapt relevant companion behavior tests to supplied candidates without repacking. Preserve separate synthetic version-resolution tests and deterministic regressions. Add test documentation for the internal option, source readiness, inputs, immutable baseline capture/selection, the future rolling/historical baseline policy, categories, recording paths, and skip meanings.
7. Finish local verification and prepare concrete changes for the commit/push readiness step. If that step is not already satisfied/authorized, identify it for the user; do not commit or push automatically and do not leave independently actionable implementation undone.
8. Once the source is pushed, validate origin branch/SHA, pack both candidates from that revision and record provenance, then prove one small real candidate-source install/reinstall flow before running the full collection. Do not build a GitHub simulator or seed fake candidate source responses to force this proof.
9. Run appropriate regressions and all runnable package/network scenarios. Fix harness and authorized extension defects; report other production defects without weakening tests. Any candidate input change requires new source provenance/packages and rerunning affected verification.
10. Verify candidates' post-test hashes equal their input hashes. Review the final diff to ensure only tests, the narrow Agentic.Check extension, and this plan were changed. Report results and remaining limitations.

## Validation and handoff requirements

Follow active `AGENTS.md` rules for foreground .NET commands and build diagnostics. Apply the editorconfig skill only if the prescribed pre-build check requires it. If required configuration or a diagnostic fix exceeds the tests plus narrow Agentic.Check extension scope, explain the concrete conflict and seek expanded scope instead of silently changing those files.

Document exact commands for:

- Checking the source checkout, pushed origin branch/commit, and clean candidate inputs, then packing both tools in Release to a separate artifacts directory and recording source/package provenance.
- Preparing baseline fixtures using the downloaded 2.2.0 package.
- Selecting a baseline by ID, and intentionally capturing a successor baseline under a new ID without overwriting historical snapshots.
- Running offline regression tests.
- Running candidate package tests with explicit package inputs and the hidden preview-source SHA, including stable-baseline-to-preview-candidate migrations.
- Opting into the real GitHub skill suite and selecting stable/preview scenarios.
- Replaying every recorded interactive scenario.

Do not reuse `AGENTIC_CHECK_SKILL_MAINTENANCE` as the new install/update opt-in: those existing maintenance tests intentionally do not install skills. Network authorization covers downloading the baseline, reading candidate source already pushed to origin, reading external published sources, and running real installation/update **inside isolated test targets**. The commit/push readiness checkpoint still requires user action or separate explicit authorization; NuGet publication and release/tag creation remain out of scope.

Final implementation report must include changed test and extension files, prepared/failed fixture inventories, baseline provenance, candidate origin branch/SHA, exact package paths/versions/hashes, external source identities, test counts by category and outcome, actual content-transition coverage, recording paths/replay commands, production blockers, and unverified platforms. State whether the pre-release candidate gate actually passed. Do not publish packages, create releases/tags, or commit/push without the separate authorization described above.

## Implementation prompt for a new session

```text
Implement docs/package-fixture-e2e-implementation-plan.md end to end.

Read AGENTS.md and the plan first. Follow its confirmed decisions and preserve unrelated changes.

Prepare all 14 baseline fixtures using the published Agentic.Check 2.2.0 NuGet package. Implement the real-package and real-gh install/update tests, including separate temporary copies for all three preview scenarios.

Keep baseline snapshots immutable and identified by installer version and preparation date. Parameterize reusable helpers and scenario selection for later baselines, keep fresh-install trigger definitions independent, and document the future rolling/historical baseline policy. Prepare only the 2.2.0 baseline collection in this task.

Implement the baseline-preparation utility as a small C#/.NET 10 test-support console project under tests/, reusing appropriate test helpers and invoking real dotnet, git, and gh processes. Do not publish it or add a production command.

Keep fixture/test work under tests/. The only production extension authorized is the hidden --preview-source-ref option and its minimal source-resolution plumbing in Agentic.Check, as specified in the plan. Keep normal help and default behavior unchanged.

Test exact candidate packages together with this repo's skills/directives from the same committed source revision pushed to its development branch at origin. Complete implementation and independent local verification before any source-readiness checkpoint. If committing/pushing is still needed, prepare the concrete result and ask for authorization or user action; do not silently use other source content.

Run the specified regressions and opt-in network tests, preserve terminal recordings, and report genuine skips for unchanged source while still testing reinstall and preservation. Do not weaken meaningful assertions. Fix defects in the authorized extension; report other production defects without expanding scope.

Report fixture provenance, candidate branch/SHA and package paths/hashes, external source identities, test results, recording replay commands, and unresolved issues. Do not publish packages, create releases/tags, or commit/push without separate explicit authorization.
```

## References

- Baseline package: https://www.nuget.org/packages/Agentic.Check/2.2.0
- Exact package bytes: https://api.nuget.org/v3-flatcontainer/agentic.check/2.2.0/agentic.check.2.2.0.nupkg
- GitHub CLI skill installation: https://cli.github.com/manual/gh_skill_install
- GitHub CLI skill updates: https://cli.github.com/manual/gh_skill_update
- Inspected update implementation: https://github.com/cli/cli/blob/v2.100.0/pkg/cmd/skills/update/update.go

Consult current primary documentation or the installed CLI's help when flags differ; do not change the tested product invocation merely to accommodate an incompatible CLI without identifying that compatibility problem.
