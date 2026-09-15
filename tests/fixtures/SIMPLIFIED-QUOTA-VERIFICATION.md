# Simplified quota implementation verification — 2026-09-15

Implemented the reduced scope: one initially empty product cache per channel per candidate
test-host process, with first use providing the cold check; no separate cold probes.
Credentials are resolved once, checked in an isolated workspace, and explicitly forwarded
to redirected and terminal children. The independent oracle and meaningful assertions remain.
Production code, workflow triggers and immutable baselines are unchanged. No commit, push,
package publication, release or tag was performed.

## Verification

Both the CLI live-test project and preparation console build with zero warnings/errors.
The focused regressions cover cache reuse after target disposal, channel/run/package
isolation, credential precedence and failures, and actual credential propagation through
redirected and recorded terminal processes.

| Check | Result | TRX under the relevant test project's TestResults |
| --- | --- | --- |
| Focused local regressions | 16 passed, 1 network opt-out skip | simplified-quota-local.trx |
| Complete local CLI/helper suite | 79 passed, 1 known missing-preview-baseline failure, 80 opt-out skips | simplified-quota-offline.trx |
| Existing Agentic.Check product regressions | 177 passed | simplified-quota-product-regressions.trx |
| Real isolated gh authentication and terminal API access | 1 passed | simplified-quota-auth-network.trx |
| Initial selected candidate run | 1 passed, 3 source-movement/stale-pin failures, 1 unchanged-source skip, 64 fixture-selection skips | simplified-quota-candidate-proof.trx |
| One manual retry with new empty caches | 4 passed, 1 unchanged-source skip, 64 fixture-selection skips | simplified-quota-candidate-retry.trx |
| Exact companion round trip and clean-cache clone/restore | 1 passed | simplified-quota-companion.trx |

The successful candidate retry covered `dotnet-cli` fresh installation, migration,
actual migration content transition, and stable verification/preservation. Its stable
content-transition case genuinely skipped because the selected stable sources were unchanged.
The complete candidate matrix was not rerun in this change; the filtered cases establish
real cold/shared-cache behavior in both channels, not a complete release gate.

During the first run, `dotnet/skills` moved from
`460a01882f8dbea3281fe68f2cc66ec196e49edc` to
`43dec99816ae6bfafae082698a01437d237a3f90`. The first migration reported SOURCE MOVEMENT;
later scenarios rejected the older cached pin. All actual installations in those cases
succeeded, but verification correctly failed. The retry used a new run/cache and succeeded.
Both runs and their evidence remain; no invalidation/retry framework or assertion exclusion
was introduced. Neither focused run had an HTTP 403 failure.

## Cache evidence and limits

Retained product caches are under
`../Agentic.Check.LiveTests/TestResults/package-fixtures/runs/`:

- Initial run: `ce2faa65e8e84664b0a8cf8fa7c3aae1`.
- Successful retry: `749931b12c2c4e6c91810c0bb388b060`.

Each contains exactly **8 distinct preview API URLs and 9 distinct stable API URLs**,
plus raw-file responses. Logs show the first cold use and subsequent reuse across separate
targets. Channel metadata records the installer hash and start time. Only the real packaged
product populated these caches.

These are retained response counts, not a measured total HTTP-request trace. The sparse
anonymous quota snapshots went from 60 remaining before the first run to 18 during the
retry; that shared-IP delta cannot be attributed precisely to individual runs/requests.
The estimate of roughly 17 anonymous API requests per full matrix assumes unchanged sources,
no expiry and no retries. The first run demonstrates that source movement can invalidate
that assumption. Authenticated gh and independent oracle traffic are additional.

## Exact source, packages and baseline

Candidate source was already committed and pushed at
`origin/v2-3-dna@59d5ef6911b0d73a136acfd53d4edd7a24aa6a47`; remote identity was verified by
`git ls-remote` and the existing candidate-readiness helper. Only test-support/docs changes
were uncommitted. Both Release packages were built from that source, with the same revision's
skills/directives selected by the hidden preview override. Production/content inputs were clean.

Packages and `candidate-build.json` are in
`../Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/59d5ef6-quota-release/`:

| Package | SHA256 |
| --- | --- |
| Agentic.Check.2.3.0.nupkg | c29ced2b5cdc3f659bcbe0b3b1f4283e05bb5cc306f4dacec9d55563f4706885 |
| InnoWvate.Agentic.2.3.0.nupkg | c6be3682d2a3473e7bb2e9acc5bd968f8b56551e4dfbaba0b46bd172e99b3d04 |

Both package hashes were independently checked after the tests. The existing published
2.2.0 baseline collection `agentic-check-2.2.0-2026-09-14-capture02` was used unchanged;
all 13 accepted archive hashes were rechecked. Full baseline provenance remains in
[the original implementation report](IMPLEMENTATION-REPORT.md). The fourteenth preview
fixture remains incomplete and was not prepared in this quota change.

Successful retry source identities:

- Own preview: candidate SHA above.
- External preview: `dotnet/skills@43dec99816ae6bfafae082698a01437d237a3f90`.
- Own stable: `v2.2.0@ebc711fb6db0912cae2b0c2bc4991d57d5afa007`.
- External stable: `dotnet/skills v1.0.0@cb7c10bd32b250459632df9e6fa1350cdaf00b18`.

## Replay and rerun

Relevant credential/real-authentication recordings and replay commands are listed in
[the replay artifact](../Agentic.Check.LiveTests/TestResults/package-fixtures/simplified-quota-replay.md).
The selected CLI package cases are noninteractive; their command output, reports and
source identities remain alongside the cache artifacts. Machine-readable verification is in
[simplified-quota-verification.json](../Agentic.Check.LiveTests/TestResults/package-fixtures/simplified-quota-verification.json).

Example real authentication replay:

```sh
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/authenticated-gh-ec5f033ca5954239900d9b3a4b4f4a9b.cast
```

Use the candidate environment inputs and commands in [README.md](README.md), with the
candidate directory above, baseline `agentic-check-2.2.0-2026-09-14-capture02`, and
`AGENTIC_E2E_FIXTURE=dotnet-cli` to reproduce the focused run. Each invocation starts a new
cache; it does not resume either retained cache. Testing was on macOS; Windows PTY coverage
remains unsupported. Existing missing-baseline and production-update issues remain unresolved.
