# Test-only GitHub caching gateway verification — 2026-09-15

Implemented entirely under `tests/`. Agentic.Check, its default behavior/help, the exact
NuGet package bytes, source skills/directives, and the manual-only workflow are unchanged.
No commit, push, package publication, release or tag was created for this work.
The unrelated announcement draft remains untouched.

## Results

| Verification | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Agentic.Check.Tests regressions | 177 | 0 | 0 |
| Agentic.Tests regressions | 86 | 0 | 0 |
| Agentic.LiveTests SDK/terminal regressions | 4 | 0 | 0 |
| Full Agentic.Check.LiveTests, both network opt-ins | 148 | 14 | 5 |
| Final proxy/authentication/PTY support checks | 21 | 0 | 0 |
| Corrected broad-stack fresh package scenario | 1 | 68 selector skips | 0 |
| Small dotnet-library fixture plus real-gh verification | 2 | 68 selector skips | 0 |

The full four-project run totals **415 passed, 14 skipped, 5 failed**. One failure was a
harness navigation defect, fixed and independently rerun successfully: the broad-stack test
waited for a dependency outside the selector's visible page. It now filters to that item
before requiring the same checked-state assertion. The full run was not repeated after
this correction. Its original failure and recording remain available.

Four pre-existing failures remain, representing three issues:

- Two failures reject the missing `preview-web-cli` historical snapshot: collection inventory
  and the explicit baseline-preparation-failed row. No historical exception was implemented
  and no snapshot was invented. Historical preview migration coverage remains blocked.
- Broad-stack `stable-current` still unnecessarily reinstalls skills. The assertion remains.
- Discovery reports seven unreviewed skills in `dotnet/skills`. The review gate remains.

The fourteen full-run skips are genuine: thirteen stable content-transition cases have
unchanged authored sources, and there is no published update for the stable-decline case.
All thirteen corresponding stable reinstall/preservation cases passed independently.
No quota error became a skip. **There were no current-run quota errors.**

Both shared-support consumers build without errors or warnings. Final support verification
includes real Unix-socket transport, concurrent cache reuse, credential/representation/method
separation, error non-caching, live quota reads, moved/new release detection, unavailable final
checks, explicit credential forwarding, and real redirected-to-PTY gh cache reuse.

Two test-support defects were fixed during verification: Hex1b's Unix PTY ignored managed
environment overrides, and the old credential test could pass with two empty variables.
Private Bash startup now applies the environment; tests require a nonempty credential marker.
The preflight also fails if real gh does not reach the socket. No credentials are recorded.

## Measured full-run traffic

Gateway run: `d33ec776363848cc9b1607160448bbf8`.

| Consumer / test interval | Authenticated upstream REST requests | Cached REST responses |
| --- | ---: | ---: |
| Maintenance, including initial authentication preflight | 343 | 1,263 |
| Candidate scenarios: product gh, oracle and readiness | 341 | 18,519 |
| Published-baseline helper verification | 10 | 11 |
| Authentication regression | 0 | 2 |
| Test-only source boundary checks (direct HTTPS) | 38 | 0 |
| **Total REST** | **732** | **19,795** |

There was also **one authenticated GraphQL request**, made by gh authentication preflight.
All **733** upstream responses carried a supplied authorization header and an authenticated
5,000-request budget. The trace records status, GitHub request ID, resource, limit, remaining,
used and reset. Expected missing-ref/release 404s are retained and cached; no authentication,
quota or transport failure occurred. Consumer attribution uses serial TRX test intervals;
product and oracle calls inside a candidate case are deliberately reported together.

The gateway served approximately **96.4%** of observed requests from memory. This is measured
cache reuse, not an assertion that every uncached request would consume one quota unit.
Release endpoints still showed different counter/reset sequences from ordinary repository
endpoints after unique-query/no-cache probes, despite both reporting authenticated core.
That discrepancy is retained as unresolved evidence; it is not assumed to mean stale responses
or failed authentication. Do not combine those counter sequences into a single quota delta.

Anonymous product traffic stayed outside the gh gateway. The full run created **17 anonymous
API cache entries: 9 stable and 8 preview**. An ordinary uncached anonymous repository probe
reported used=26 before package scenarios and used=52 afterward, with the same reset.
The difference of 26 accounts for 17 full-run requests, 8 additional cold requests in the
broad-stack rerun, and the ending measurement itself. This agrees with both run cache
inventories, with no residual; attribution is corroborated by cache contents and budget
measurements, rather than an anonymous packet trace. Raw/codeload/NuGet downloads are not
included in REST counts.

The table describes the full process only. Separate diagnostic/support reruns and the manual
final source verification consume additional authenticated requests and have separate logs.
The final broad-stack rerun used a new empty gateway (360 upstream requests, 417 hits), not
responses borrowed from the full run.

## Source consistency and isolation

The full run validated 24 mutable source selections. Afterward, all 24 were independently
rechecked with unique query parameters and `Cache-Control: no-cache`; every identity matched.
The final implementation's freshness and fail-fast routing refinements were verified with
21 support tests, including real gh; the corrected broad-stack run also exercised unique-query
source checks. The original full-run ledger is preserved without rewriting it.

All 638 full-run gh configuration directories were temporary and pointed at the shared socket.
After completion, every configuration directory and the socket were gone. The initial read-only
credential lookup is the only operation using the user's ordinary gh context. Branch/tag/release
checks and current quota probes are the deliberate live read paths; normal selection and oracle
reads share cached authentic responses. Source movement or unavailable end validation fails the
run with rerun guidance, even if individual cases pass or report unchanged sources.

| Source | Selection / resolved commit |
| --- | --- |
| Candidate own source, origin/v2-3-dna | `59feba1bf050e338e0de9fb35f54007944f78fc4` |
| Own stable v2.2.0 | `ebc711fb6db0912cae2b0c2bc4991d57d5afa007` |
| Own default main, separate real-gh helper tests | `af190c3474079ea5b5143e170deb331c431c59a9` |
| dotnet/skills main | `43dec99816ae6bfafae082698a01437d237a3f90` |
| dotnet/skills v1.0.0 | `cb7c10bd32b250459632df9e6fa1350cdaf00b18` |
| mtmattei/UnoPlatformSkills main; no release | `802045a45ae73eaae9b9ac70c01d0ff0e6c5d401` |
| unoplatform/studio main; no release | `d57c60a1ce66a5acb899aa15e2344e614b807983` |

Candidate verification repeatedly checked HEAD against the actual development branch at origin.
Only test-support files changed locally; candidate production/content inputs remain exactly the
committed and pushed source above. No alternate source content was used for candidate installs.

## Package and fixture provenance

Both Release candidates are under
`tests/Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/59feba1-full-release/`:

| Package | SHA256 |
| --- | --- |
| Agentic.Check.2.3.0.nupkg | `1fd55aec1d35f91d24757f56226547fcfbcfc4eb3056df73ccddace10c4c99a5` |
| InnoWvate.Agentic.2.3.0.nupkg | `c935ca9ef34eb03bbab1c0cc86c7ccfe778f2dd17ed1932da0790a2342891c22` |

Baseline `agentic-check-2.2.0-2026-09-14-capture02` retains thirteen accepted captures and the
explicit failed preview capture. All **58** collection files match their pre-run hashes.
The published Agentic.Check 2.2.0 package SHA256 is
`6ae632d7585d0db13884267244776a60a704cd63b448aec5f372c85606348931`, with embedded source commit
`43e68fd7a975dde9a1dbf7933fe13776d61a8428`, retrieved `2026-09-14T14:24:52.5112166Z`.
No baseline was regenerated or edited. Preparation now stages new captures until global
source validation succeeds; rolling/historical baseline policy remains in the fixture README.

## Evidence and replay

Evidence root:
`tests/Agentic.Check.LiveTests/TestResults/package-fixtures/proxy-verification-59feba1/`.
It contains `test-results.json`, `request-counts.json`, `provenance.json`, before/after baseline
hashes, `product-cache-requests.json`, anonymous quota probes, `independent-fresh-end.json`,
and an index with hashes and replay commands for 39 recordings, including initial failures.
TRX files named `proxy-*.trx` remain in each test project's TestResults directory.
Gateway request ledgers, configuration inventories and source summaries remain under
`package-fixtures/github-runs/`.

Replay the successful corrected broad-stack scenario from the checkout:

```sh
asciinema play tests/Agentic.Check.LiveTests/TestResults/package-fixtures/recordings/broad-stack-fresh-58aa2fc5645c435b9afde88bf3ef8c3a-apply.cast
```

The full list is in `proxy-verification-59feba1/recording-replay.md`.

Reproduce the full opt-in process using the candidate paths above:

```sh
AGENTIC_E2E_NETWORK=1 AGENTIC_CHECK_SKILL_MAINTENANCE=1 \
AGENTIC_E2E_BASELINE=agentic-check-2.2.0-2026-09-14-capture02 \
AGENTIC_E2E_CHECK_PACKAGE="$PWD/tests/Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/59feba1-full-release/Agentic.Check.2.3.0.nupkg" \
AGENTIC_E2E_COMPANION_PACKAGE="$PWD/tests/Agentic.Check.LiveTests/TestResults/package-fixtures/candidates/59feba1-full-release/InnoWvate.Agentic.2.3.0.nupkg" \
dotnet test tests/Agentic.Check.LiveTests/Agentic.Check.LiveTests.csproj \
  --logger 'trx;LogFileName=proxy-full-suite.trx'
```

Use a new TRX filename to preserve existing evidence. Every test-host invocation creates
fresh caches; current anonymous quota remaining was 8 at the final probe, so another full
cold run needs a reset or sufficient quota first. This is a recorded observation, not a
permanent precondition or a claim about the current budget when this document is read.
