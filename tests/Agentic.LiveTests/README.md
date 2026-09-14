Exact supplied-package installation/migration verification is documented in [the fixture suite](../fixtures/README.md).

# Companion package and terminal tests

Run `dotnet test tests/Agentic.LiveTests/Agentic.LiveTests.csproj` from the checkout.
Tests pack current working-tree source into a temporary directory. Unique package metadata
versions exercise the real SDK's stable/preview floating resolution, update, same-major and
wrong-major downgrade, preview-to-stable switch, exact restore, and minimum-version rejection.
The fixture payload remains the real companion; runtime compatibility is checked separately.
Temporary `nuget.config`, package/HTTP caches, CLI home, and Git identity isolate local installs.
No publishing or GitHub calls are required.

The Hex1b test invokes `dotnet agentic` from a local manifest and a child folder. It records help,
complete raw-log framing from files and stdin, stdout output, raw and historical Git history,
validation, argument errors, and incompatible invocation output.
It also asserts file contents, command exit codes, and absence of forbidden mutations.

Recordings are preserved in `TestResults/recordings/` and their paths appear in test output:

```bash
asciinema play tests/Agentic.LiveTests/TestResults/recordings/companion-<id>.cast
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
