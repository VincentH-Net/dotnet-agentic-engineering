# Releasing

How a new version of the three NuGet packages gets published: `Agentic.Check`, whose version names
the release, and `InnoWvate.Agentic` and `InnoWvate.Dna`, each versioned independently and published
only when it changed.

## Versions

- **A version number names published bytes.** Once a number is on nuget.org it is taken: the next
  change to that package needs a new number. Until then the number is free to reuse.
- **The inner dev loop reuses the number.** `tests/manual-test.cs` packs the working tree under the
  version in the project file, and the full suite does the same for every package whose number is not
  on nuget.org yet, as often as needed, without bumping. That is safe there because both isolate the
  NuGet caches and the CLI home per run and verify the installed package hashes against the packed
  candidate, so the bytes that run are always the bytes just packed. Do not install a dev pack under a
  number that nuget.org already has anywhere else.
- **A version moves only when its package changes.** `InnoWvate.Agentic` and `InnoWvate.Dna` keep their
  published number for as long as nothing that goes into them changes, and take the next number with
  their first change. The full suite enforces both halves. It does not pack a package whose number is
  already on nuget.org: it tests the published package itself, which is what users have. And it fails
  when the sources of that package (its project folder, the shared files it links and the build files)
  differ from the commit it was published from, naming what changed.
- **Bump `Agentic.Check` right after publishing, then leave it alone.** Its version names the release
  and it is published every time, so immediately after a publication set its next patch version
  (step 6); the full suite refuses to pack it under a number that nuget.org already has. That number
  is then used for every dev build until it is published in turn. Raise it from patch to minor or
  major later when the work turns out to need that; never bump for a dev build.
- **Majors are independent too.** Nothing compares the version of `Agentic.Check` with the version
  of `InnoWvate.Agentic`: a check reads the companion's own `<Version>` from GitHub and resolves it
  with `major.*` from that number, and the only link between the packages is the `-m` literal in
  content that calls the companion, which names its major and the lowest minor that content needs. So
  the two majors may differ, as they already do for `InnoWvate.Dna`.
- **Which part to bump.**
  - `Agentic.Check`: it is published on every release because the release tag carries its version, so
    it always moves. Patch for fixes, minor for new capabilities such as new detection or a new
    install item, major for a changed contract with the directives, skills or companion.
  - `InnoWvate.Agentic`: patch for fixes that leave its commands unchanged. Minor whenever directives
    or skills need a new or changed command: only the content that uses it raises its `-m` to the new
    major.minor, and content that was not touched keeps its value. `-m` is a minimum within the major,
    so content at different minors is served by one tool at or above the highest, and a pinned tool
    follows the newest version in its major on its own. A test keeps every `-m` in the tool's major and
    not above its version. Major for an incompatible change, which moves every `-m`; a check accepts a
    companion only with the same major and an equal or higher minor.
  - `InnoWvate.Dna`: patch or minor as its own behavior changes, major only when the launch protocol
    changes, together with `DnaInstaller.MinimumLauncherVersion`.

## Invariants

- **Publish only candidates from a full-suite run.** They are packed from a committed, pushed and
  clean commit, in Release with repository-relative source paths, with hashes and the commit recorded
  in `candidate-build.json`, and the whole suite ran against those exact bytes. Packages from
  `tests/manual-test.cs` are for local testing only.
- **When `InnoWvate.Agentic` changed, publish it before `Agentic.Check`.** A published check installs
  the local tool with `major.*` from nuget.org; while the companion is missing there, every run that
  selects the Prompt Log directive fails.
- **Tag the packed commit and keep it reachable.** The Source Link map and repository commit inside
  the published packages point at that commit, so merge `main` by fast-forward, never by rebase.
- **`main` never carries a companion major.minor that nuget.org does not have.** The preview channel
  reads the default branch and takes the companion requirement from the `<Version>` it finds there, so
  every `--preview` run that selects the Prompt Log directive fails while that major.minor is
  unpublished. Publishing before the fast-forward (steps 3 and 4) guarantees this for a release; do
  not merge a branch that raised the companion's minor or major into `main` any earlier. A patch that
  is ahead of nuget.org is harmless, because only the major.minor is required.

## Steps

1. **Finish the content.** Confirm the version of every package that changed since its last
   publication is bumped as described under Versions; unchanged packages keep their published number.
   The full suite fails when either is not the case.
   The literal `<Version>` in `src/Agentic/Agentic.csproj` is what Agentic.Check reads from GitHub, and
   every `-m` in directives and skills must be in its major and not above its major.minor (a test
   enforces this). A new skill of this repository enters the manifest's stable set with the release that
   ships it as its minimum release, so the stable channel offers it from that release on and the suite
   does not look for it in an older one; the preview channel has it at once. The maintenance tests read
   this repository's preview content from the pushed branch of the checkout, which is what `main`
   becomes at the merge. Fill the README placeholders and, if it ships the same day, the article. Commit
   with a prompt log and push.
2. **Run the full suite on that commit, against the candidates CI packed.** Start the `Tests`
   workflow for the branch with `gh workflow run tests.yml --ref <branch>`, then run
   `dotnet run --file tests/run-full-suite.cs -- --ci-run <run id>`. CI packs the candidates once and
   runs the package scenarios against them on Ubuntu and Windows, while the full suite runs against
   the same files here. Require zero failures in all three; network and maintenance tests are enabled
   by the runner. Keep the run folder: its `candidates/` holds the packages and `candidate-build.json`,
   and the summary says for each package whether it was packed from this commit or is the unchanged
   package already on nuget.org. Without `--ci-run` the suite packs locally, which is what everyday
   runs use.
3. **Publish in dependency order.** Upload only what the run packed; a package that
   `candidate-build.json` lists under `published` is already on nuget.org. Compare each file's SHA-256
   with `candidate-build.json`, then upload `InnoWvate.Agentic` and `InnoWvate.Dna` when they were
   packed. Wait until nuget.org lists them, usually within 15 minutes. Only then upload
   `Agentic.Check`. Upload through the portal or with
   `dotnet nuget push <package> --source https://api.nuget.org/v3/index.json --api-key <key>`.
4. **Fast-forward `main` and tag.** Merge the branch into `main` by fast-forward, tag the packed commit
   `v<Agentic.Check version>`, and create the GitHub release at that tag; the package READMEs and the
   nuspec release-notes link point to `releases/tag/v<version>`. The stable channel reads the latest
   release, so the tag is what exposes the new directives and skills to users; that is why the
   packages go first.
5. **Verify from nuget.org.**
   ```sh
   dotnet run --file tests/manual-test.cs -- --published --fresh --fixture dotnet-cli
   ```
   In the window it opens: `dna check` must show the new Agentic.Check version and the release content
   and install `InnoWvate.Agentic` from nuget.org; then `dna prompt-log` and one agent-created commit.
6. **Afterwards.** Publish the article, and set the next patch version of `Agentic.Check`, so its dev
   builds never reuse a published number. The other packages keep their number until they change.
   Capture a new baseline collection with the published Agentic.Check (see
   [tests/fixtures/README.md](../tests/fixtures/README.md)) only when the release changed the shape of
   what a check writes into a repository: the markers or layout of managed blocks, the metadata of
   installed skills, the tool manifest entries or pins, or the prompt-log format; or when a fixture
   definition changed. A baseline is the state the update scenarios start from, and an older baseline
   cannot hold such a state. Changed or added directive and skill text needs no baseline: updating it
   from an older baseline is exactly what the update scenarios test, and a newer baseline would only
   make their content-transition rows skip until the next change.
