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

The incomplete historical preview fixture and existing production update defects remain
separate work. This quota implementation does not prepare or modify baseline snapshots.

### Proposed historical-preview exception

The missing preview fixture can instead be prepared as an authentic selective installation:
run the unchanged published 2.2.0 package interactively and deselect only
`dotnet/skills:plugins/dotnet-test/skills/dotnet-test-frameworks`. Its embedded source commit
`43e68fd7a975dde9a1dbf7933fe13776d61a8428` lists no other skill depending on that entry,
and its workflow accepts selected recommendations through the normal UI. Real-package
execution must still confirm this behavior before accepting a snapshot.

This is a proposed exception to accepting every baseline recommendation, not a general
assertion exclusion. Record the omitted identity, reason, actual selections and UTC capture
time in baseline provenance, and preserve a terminal recording. Require success and complete
content verification for every selected item; do not merely ignore a failed installation.
Retain the failed attempt's evidence and leave all thirteen accepted snapshots untouched.

Candidate preview tests must still require the current `test-analysis-extensions` skill and
its declared dependencies. Stable coverage of `dotnet-test-frameworks` from the published
stable source remains required. The exception must never become a global skill-name ignore
list. It removes one obsolete preview recommendation from the historical starting state;
it does not claim a complete unmodified-manifest 2.2.0 preview installation.
