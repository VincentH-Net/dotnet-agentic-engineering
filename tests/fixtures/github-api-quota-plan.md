# GitHub quota: simplified test-support approach

The user approved this reduced scope on 2026-09-15; it supersedes the earlier elaborate
proposal and the original per-scenario product HTTP isolation requirement.

- Share one initially empty product HTTP cache per channel across the serial candidate
  scenarios in one test-host run. Keep the existing 3600-second setting. The first selected
  scenario for each channel is its cold check; do not add separate cold probes.
- Give each run a unique artifact directory and bind it to the installer package hash.
  Only real package invocations populate the caches. Preserve caches for diagnostics;
  never seed or reuse them across runs. Every target, installed skill tree, SDK/NuGet,
  Git configuration and agent home stays independent, including all preview copies.
- Resolve available credentials once (GH_TOKEN, GITHUB_TOKEN, existing gh login, in that
  order). Verify active gh authentication and live authenticated quota once in an isolated
  workspace before network work. Forward those same credentials explicitly to each
  redirected/terminal child. Missing/invalid authentication fails with setup guidance;
  connectivity failures are reported separately. Never record credentials or invoke login
  interactively. Cover credential precedence, failures and both process paths in tests.
- Keep the independent source oracle and all content, preservation, reinstall and source
  identity assertions. Its caches and expected content remain separate from product caches.
- Change no Agentic.Check behavior or package bytes. Direct product HTTP remains anonymous.
  Keep workflow triggering manual-only. Do not add run locks, batching, bounded quota
  scheduling, oracle-cache changes or production authentication as part of this change.

Expected candidate-matrix anonymous API cost with unchanged sources and fresh cached
responses throughout: approximately 8 preview + 9 stable = 17 requests, versus approximately
600 with a cold cache per scenario. These are code-derived estimates, not measurements.
Authenticated gh installation/update and oracle traffic are additional and unchanged.
Expiry, source movement, failures and retries can increase request counts. Separate test-host
invocations create separate caches. Quota failures remain failures, not unchanged-source skips.

Verification: run focused cache isolation/reuse, credential and terminal regressions, build
both consumers of shared support, verify real isolated gh authentication, then exercise the
candidate matrix using exact packages and matching committed/pushed content. Record actual
outcomes, immutable baseline/package hashes and terminal replay paths; do not weaken failures.

Production authentication is deferred to [issue #4](https://github.com/VincentH-Net/dotnet-agentic-engineering/issues/4).
The incomplete historical preview fixture and existing production update defects remain
separate work. The earlier proposed historical exception is retained below for reference;
this simplified quota implementation does not prepare or modify baseline snapshots.

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
