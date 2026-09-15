# GitHub quota: shared test-run caching gateway

This implemented approach supersedes the earlier quota plan and per-scenario cold-cache
requirement. It changes only test support; Agentic.Check and published package bytes remain
unchanged. Production authentication is deferred to [issue #4](https://github.com/VincentH-Net/dotnet-agentic-engineering/issues/4).

## Runtime and isolation

`PackageFixtureSupport/GitHubCachingProxy.cs` hosts a small .NET 10 Kestrel HTTP gateway on
an owned Unix-domain socket. `GitHubFixtureRun` owns one gateway per test-host/preparation
operation. Real gh uses its documented `http_unix_socket` setting in each temporary
`GH_CONFIG_DIR/config.yml`. The gateway forwards to `https://api.github.com` with normal TLS;
it does not modify gh, Agentic.Check, certificate trust, ambient proxy variables or user gh
configuration. The initial read-only `gh auth token` credential lookup is the only ambient
gh operation. API authentication checks, candidate readiness, fixture children, the source
oracle, manifest verification and discovery/maintenance all use isolated configurations.

Resolve credentials once: GH_TOKEN, GITHUB_TOKEN, then the existing gh login. The preflight
checks active gh authentication, requires evidence that gh reached the configured socket,
and measures an ordinary authenticated repository GET's
rate-limit response headers. `/rate_limit` has previously disagreed with ordinary-request
headers; it is not used as proof of the actual core budget. Missing/invalid credentials
fail with setup guidance; there is no interactive login. Credentials are explicitly forwarded
to redirected processes and PTYs, never printed in diagnostics or recordings.

Hex1b 0.165's Unix PTY did not apply managed environment overrides in local verification.
RecordedTerminal therefore applies the fixture environment using a temporary Bash startup
file created with user-read/write permissions. Bash reads and removes it before test commands;
workspace disposal also removes it after failures. This file can temporarily contain a token;
it is never copied to reports or fixtures. No user shell startup files are read. Tests require
a nonempty marker credential and prove real terminal gh reuses the redirected gh response
through the same gateway while using a different temporary configuration.

## Cache and live validation

Successful GET/HEAD responses and expected HTTP 404s are cached for this run. Keys include
method, path/query, credentials, representation/API-version headers and body hash. Concurrent
identical reads share one upstream response. Status, headers and body are replayed. Auth,
quota and transient error responses are never cached. Mutating methods are forwarded rather
than cached; the suite does not perform remote mutations. gh's authentication GraphQL POST
is executed once by the run-scoped authentication preflight. API writes are not a test flow.

Ordinary branch/tag/release selection, verification and authentication reads use the cache.
The only deliberate read bypasses are current quota probes and run-boundary source validation.
A current quota probe uses `X-Agentic-Test-Cache: bypass`; `/rate_limit` always bypasses.
The private control header is removed before forwarding to GitHub. Boundary checks and quota
probes also use a unique query parameter and `Cache-Control: no-cache`: a proxy miss alone
is insufficient to rule out upstream HTTP response caching. Fresh local verification still
observed different counter/reset sequences on `/releases/latest` versus ordinary repository
requests, both labeled authenticated core. Do not combine those counters or infer stale
responses from the discrepancy alone; preserve their raw headers.

Before scenarios, snapshot default branch names, branch commits and stable release selections
(including repositories with no release) for the four configured source repositories. Record
any additional mutable references encountered during the run. Compare those identities through
fresh requests once at completion. Fixed commit SHAs do not require movement checks. A new
selected release, moved branch/tag, changed default branch, or unavailable final lookup makes
verification **inconclusive; rerun required**. xUnit collection cleanup reports failure even
if individual assertions passed. Scenario-level unchanged-source skips are provisional until
the run validates. Preparation stages new captures under reports and accepts them only after
boundary validation; existing snapshots remain untouched. Interrupted runs lacking a validated
summary have not completed source verification. Start/end checks cannot detect move-and-revert
between observations, and are not claimed to do so.

Product HTTP caching remains separate: one initially empty cache per channel and exact
installer identity, 3600 seconds, populated only by real package execution. First selected
scenario per channel provides the cold check. No cache is seeded from oracle content or reused
across test-host runs. Source archive verification still compares assets and Git blob identities
independently; sharing authentic API responses does not remove any content assertion. Targets,
agent homes, SDK/NuGet, Git state and all preview scenario copies remain separate.

## Evidence and operation

`TestResults/package-fixtures/github-runs/<id>/contexts.json` inventories isolated configurations
and their shared socket. `requests.jsonl` records each hit or upstream request, method/path,
status, authorization presence, authenticated-budget evidence, response hash and quota headers.
Tokens and response bodies are omitted. Cache-hit quota headers are historical, not measurements.
`summary.json` records counts and expected/actual source identities and the validation outcome.
A gateway summary validates network/source consistency, not the individual scenario assertions;
read it alongside TRX results and terminal recordings. Responses live in memory and disappear
at run end; the socket directory is removed. No response cache persists into another run.

`budgets.json` and `budgets.txt` automatically report authenticated and anonymous REST core
budgets before and after each network test host/preparation/packing operation. Both use a fresh
GET to the same ordinary public repository endpoint, with authorization supplied only for the
authenticated sample. The start pair precedes gh authentication and source priming; the end
pair follows source validation, including when source validation fails. These four explicit
probe requests are uncached, carry unique query parameters, and do not add source-boundary
checks. Final gateway counts include the ending probes. Offline-only runs make no probes.

Each sample preserves UTC time, HTTP status, limit, remaining, used, reset, resource and GitHub
request ID. An unavailable, malformed or unexpected authentication-budget response is recorded
as such, never as zero use. A comparable start/end pair reports its observed delta; a reset,
limit/resource change, backwards counter or inconsistent arithmetic suppresses that delta.
Counter sequences from uncached core responses are also grouped by authentication, resource,
limit and reset, with a flag for multiple sequences or decreasing counters. This can represent
a reset or the previously observed differing endpoint counters; it is not silently combined.

Deltas describe the shared account/IP budget, not exclusive test consumption. They include
unrelated activity and the ending probe, and exclude the already-consumed starting probe.
The report separately counts authenticated/anonymous REST/GraphQL upstream attempts and cache
hits. Product direct HTTP requests do not pass through this gh gateway: anonymous upstream
counts in this report count its two probes, while the anonymous budget samples bracket product
activity too. Separate test hosts have separate reports; do not sum overlapping budget deltas.
A crashed/killed host leaves a `started` report without fabricated end measurements. Console
output also gives the before/after budgets and the artifact path.

All real-GitHub tests share the serial xUnit collection. Existing opt-in variables and scenario
selectors remain unchanged. Build both shared-support consumers, run local proxy/authentication
and terminal regressions, then run exact candidate packages with matching committed/pushed
content. Preserve package/baseline hashes, TRX and asciinema recordings. Do not weaken assertion
failures or reclassify quota failures as unchanged-source skips. Ordinary non-test gh remains
outside this test configuration. The gateway requires Unix-domain socket support; real PTY
verification is supported on macOS/Linux.

Expected anonymous candidate-matrix API cost remains approximately 8 preview + 9 stable = 17
with no expiry/retries; these direct product calls do not pass through the gh gateway. The
shared gateway reduces authenticated traffic according to actual repeated requests. Earlier
600–1200 authenticated full-run estimates were extrapolations, not measured guarantees.
Use run traces for actual totals; unrelated processes can also consume the account/IP budget.

Historical preview preparation and existing production update defects are separate from the
quota implementation. The preview capture was subsequently completed as described below.

### Completed one-time historical-preview exception

On 2026-09-15 the missing preview fixture was captured as an authentic selective installation:
the unchanged published 2.2.0 package ran interactively with only
`dotnet/skills:plugins/dotnet-test/skills/dotnet-test-frameworks` deselected. Its embedded source
commit `43e68fd7a975dde9a1dbf7933fe13776d61a8428` lists no other skill depending on that entry.
The real package exited successfully and installed all fifteen selected skills. Every selected
skill and directive was independently verified; source identities were rechecked before acceptance.

This was an explicitly approved one-time exception to accepting every baseline recommendation.
A temporary C# harness reused the existing preparation helpers; no preparation-tool or test
assertion changes were retained. Provenance records the omitted identity, reason, actual
selections and UTC capture time. The terminal recording and failed attempt's evidence remain
local test artifacts. All thirteen earlier accepted snapshots remain unchanged. See the
[capture record](baselines/agentic-check-2.2.0-2026-09-14-capture02/preview-web-cli/README.md).

Candidate preview tests still require the current `test-analysis-extensions` skill and its
declared dependencies. Stable coverage of `dotnet-test-frameworks` from the published stable
source remains required. There is no global skill-name ignore list. This is a selective
historical starting state, not a complete unmodified-manifest 2.2.0 preview installation.
