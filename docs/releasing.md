# Releasing

How a new version of the three NuGet packages gets published: `Agentic.Check`, whose version names
the release, and `InnoWvate.Agentic` and `InnoWvate.Dna`, each versioned independently and published
only when it changed.

## Versions

- **A version number names published bytes.** Once a number is on nuget.org it is taken: the next
  change to that package needs a new number. Until then the number is free to reuse.
- **The inner dev loop reuses the number.** `tests/manual-test.cs` and the full suite pack the working
  tree under the version in the project file, as often as needed, without bumping. That is safe there
  because both isolate the NuGet caches and the CLI home per run and verify the installed package
  hashes against the packed candidate, so the bytes that run are always the bytes just packed. Do not
  install a dev pack under a number that nuget.org already has anywhere else.
- **Bump right after publishing, then leave it alone.** Immediately after a publication, set the next
  patch version of every package that was published (step 6). That number is then used for every dev
  build until it is published in turn. Raise it from patch to minor or major later when the work turns
  out to need that; never bump for a dev build.
- **Majors are independent too.** Nothing compares the version of `Agentic.Check` with the version
  of `InnoWvate.Agentic`: a check reads the companion's own `<Version>` from GitHub and resolves it
  with `major.*` from that number, and the only link between the packages is the `-m` literal in the
  Prompt Log directive, which is the companion's major.minor. So the two majors may differ, as they
  already do for `InnoWvate.Dna`.
- **Which part to bump.**
  - `Agentic.Check`: it is published on every release because the release tag carries its version, so
    it always moves. Patch for fixes, minor for new capabilities such as new detection or a new
    install item, major for a changed contract with the directives, skills or companion.
  - `InnoWvate.Agentic`: patch for fixes that leave its commands unchanged. Minor whenever directives
    or skills need a new or changed command: the `-m` values they carry are its major.minor and a test
    keeps them equal, so a minor bump rewrites the Prompt Log directive in every consumer, which is the
    intended signal that agents need the newer tool. Major for an incompatible change; a check accepts a
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

## Steps

1. **Finish the content.** Confirm the version of every package that changed since its last
   publication is bumped as described under Versions; unchanged packages keep their published number.
   The literal `<Version>` in `src/Agentic/Agentic.csproj` is what Agentic.Check reads from GitHub, and
   the `-m` values in directives and skills must match its major.minor (a test enforces this). Fill the
   README placeholders and, if it ships the same day, the article. Commit with a prompt log and push.
2. **Run the full suite on that commit.** `dotnet run --file tests/run-full-suite.cs`. Require zero
   failures; network and maintenance tests are enabled by the runner. Keep the run folder: its
   `candidates/` holds the packages and `candidate-build.json`.
3. **Publish in dependency order.** Compare each file's SHA-256 with `candidate-build.json`, then upload
   `InnoWvate.Agentic` and `InnoWvate.Dna` when they changed. Wait until nuget.org lists them, usually
   within 15 minutes. Only then upload `Agentic.Check`. Upload through the portal or with
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
6. **Afterwards.** Publish the article. Capture a new baseline collection with the published
   Agentic.Check for future update tests (see [tests/fixtures/README.md](../tests/fixtures/README.md)),
   and set the next patch version of every package that was published, so dev builds never reuse a
   published number.
