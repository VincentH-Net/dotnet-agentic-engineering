# Releasing

How a new version of the three NuGet packages gets published: `Agentic.Check`, `InnoWvate.Agentic`
(versioned together with the directives and skills that invoke it) and `InnoWvate.Dna` (versioned
independently, published only when it changed).

## Invariants

- **Publish only candidates from a full-suite run.** They are packed from a committed, pushed and
  clean commit, in Release with repository-relative source paths, with hashes and the commit recorded
  in `candidate-build.json`, and the whole suite ran against those exact bytes. Packages from
  `tests/manual-test.cs` are for local testing only.
- **Publish `InnoWvate.Agentic` before `Agentic.Check`.** A published check installs the local tool
  with `major.*` from nuget.org; while the companion is missing there, every run that selects the
  Prompt Log directive fails.
- **Tag the packed commit and keep it reachable.** The Source Link map and repository commit inside
  the published packages point at that commit, so merge `main` by fast-forward, never by rebase.

## Steps

1. **Finish the content.** Set the versions in the three project files; the literal `<Version>` in
   `src/Agentic/Agentic.csproj` is what Agentic.Check reads from GitHub, and the `-m` values in
   directives and skills must match its major.minor (a test enforces this). Fill the README
   placeholders and, if it ships the same day, the article. Commit with a prompt log and push.
2. **Run the full suite on that commit.** `dotnet run --file tests/run-full-suite.cs`. Require zero
   failures; network and maintenance tests are enabled by the runner. Keep the run folder: its
   `candidates/` holds the packages and `candidate-build.json`.
3. **Publish in dependency order.** Compare each file's SHA-256 with `candidate-build.json`, then upload
   `InnoWvate.Agentic` and, when changed, `InnoWvate.Dna`. Wait until nuget.org lists them, usually
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
   and set the next development versions.
