Exact supplied-package installation/migration verification is documented in [the fixture suite](../fixtures/README.md).

# Companion package and terminal tests

Run `dotnet test tests/Agentic.LiveTests/Agentic.LiveTests.csproj` from the checkout.
Tests pack current working-tree source into a temporary directory. Unique package metadata
versions exercise the real SDK's stable/preview floating resolution, update, same-major and
wrong-major downgrade, preview-to-stable switch, exact restore, and minimum-version rejection.
The fixture payload remains the real companion; runtime compatibility is checked separately.
Temporary `nuget.config`, package/HTTP caches, CLI home, and Git identity isolate local installs.
Global shorthand install/update tests use the same isolated CLI home and local feed; the
user's global tools are untouched. No publishing or GitHub calls are required.

The Hex1b test invokes both `dotnet agentic` and the globally installed `dna` from a local
manifest and a child folder. It records help,
complete raw-log framing from files and stdin, stdout output, raw and historical Git history,
default history display, limits and truncation notices, unlimited history, validation,
argument errors, and incompatible invocation output.
It also asserts file contents, command exit codes, and absence of forbidden mutations.

Recordings are preserved in `TestResults/recordings/` and their paths appear in test output:

```bash
asciinema play tests/Agentic.LiveTests/TestResults/recordings/companion-<id>.cast
asciinema play tests/Agentic.LiveTests/TestResults/recordings/dna-<id>.cast
```

A separate shell-independent test sends a large Unicode log through real process stdin, captures
stdout, creates a fixture commit from that output, and verifies the exact displayed body plus
commit hash and committer datetime. It also checks file-free wrapping, delimiter/escape collisions,
explicit stdout, file output, and clearing stale output for empty input. No per-entry files or
shell quoting are involved in that test. A second fixture commit uses Git's normal cleanup and
verifies that the displayed log matches the resulting stored text. The terminal test also uses
normal cleanup for its raw-log commit.

The current PTY harness uses Bash on macOS/Linux. Windows terminal execution and later-runtime
roll-forward require separate platform validation. Hex1b is a test dependency only.

`DnaPackageTests` verifies real packaged shorthand installation and update, local-tool absence,
large Unicode stdin, stdout/stderr, quoted/space-containing arguments, working directories,
and child failure codes. Both check shortcuts execute the real Agentic.Check package from
an isolated feed, including help forwarding and argument validation. No assertions attempt
to retest .NET's latest-version selection or caching policy.

Missing local companions produce the exact friendly `dna check` guidance on stderr with
exit code 1. Coverage includes discovery through a parent manifest, a nearer root manifest
that blocks that scope, and the restored scope when `isRoot` is false. Declared but unrestored
tools, invalid manifests, and unavailable SDK selections are compared against the direct SDK
invocation to ensure their diagnostics and exit codes are preserved.
