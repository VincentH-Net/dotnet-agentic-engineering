# InnoWvate.Agentic 2.3.0

A .NET 10 local tool for deterministic prompt logging and launching Agentic.Check. Agentic.Check installs and updates it
before applying dependent directives or skills. After cloning, run `dotnet tool restore` in the
target directory to restore the exact version recorded in `.config/dotnet-tools.json`.
Run from that target or a child folder:

```bash
dotnet agentic --help
dotnet agentic check
dotnet agentic prompt-log
dotnet agentic prompt-log --limit 50
dotnet agentic prompt-log --all
dotnet agentic prompt-log wrap --input - -m 2.3
dotnet agentic prompt-log wrap --input prompt-log-input.txt --prompt-log prompt-log-block.txt -m 2.3
dotnet agentic prompt-log show --since 2026-01-01 -m 2.3
dotnet agentic prompt-log check --commit HEAD -m 2.3
```

`dotnet agentic check <args>` delegates to `dotnet tool exec Agentic.Check -- <args>`,
using the latest stable package through normal .NET resolution and caching, as with
`dnx Agentic.Check`. All Check arguments, including `--help`, pass through unchanged.
The selected SDK must be .NET 10 or later; check `global.json` if an older SDK is selected.
Standard streams, working directory, and the child exit code are preserved.

The optional global [dna shorthand](../Dna/README.md) makes `dna <args>` equivalent to
the repo-local `dotnet agentic <args>`. `dna check` also works before a local companion exists.

`wrap` reads one complete, already-sanitized raw log. Use the harness's process-input API for
stdin; no JSON encoding, per-entry files, shell variables, pipes, or shell redirection are
required. `--prompt-log` defaults to `-` (stdout); a named output is replaced atomically, never
appended to. File input and output may be combined with stdin and stdout independently.
The command buffers the complete input before emitting its block. Use output only on exit 0.
A failed write to stdout can leave partial output, which the caller must discard on failure.

UTF-8 text is preserved; CRLF/LF (and lone CR) normalize to LF, including final empty lines.
Zero-byte input produces empty output, clearing a named output so stale logs cannot be reused.
Whitespace-only input remains meaningful. A stable `.lock` sidecar prevents simultaneous file
replacements. Keep any input/output files outside staged work. The agent handles selection,
sanitization, Q&A pairing, readability, and completeness; entries have no machine-readable schema.

Include the returned block unchanged in the initial commit message, after the original subject
and body and before Git trailers. The agent handles staging and committing; Git's normal
whitespace cleanup is acceptable and may trim trailing spaces or collapse blank lines in the
stored log and the rest of the commit message. This tool does not assemble the rest of
the commit message and never writes Git history or configuration.

`prompt-log` defaults to `show`. Both display the newest 20 prompt-log entries reachable from
`HEAD`, in chronological order. Use `--limit N` (a positive integer) to change the limit, or
`--all` to remove it; the two options cannot be combined. `--since` and `--until` narrow the
history before the limit is applied. `--all` still follows `HEAD`, not every branch. A notice
on stderr says when older entries were omitted. `prompt-log --help` shows help without reading history.

History is read with one `git log` invocation, filtering commits by their full-line prompt-log
markers before limiting. Ordinary commits do not count; malformed prompt-log entries do count
and report errors. One extra entry is retrieved to detect truncation. Only the selected entries
are validated; use `--all` to include older logs. Individual log bodies are not truncated.

Each log has its full commit hash, ISO 8601
committer datetime (including offset), and validation/legacy label, followed by its unescaped
text and a display separator. It omits ordinary commit content and trailers. Historical JSON
logs are decoded and numbered standalone logs retain their raw text. Malformed logs are reported
on stderr; other logs are still displayed and the command returns failure. `prompt-log check` validates
framing and escaping only; no log is a successful notice. Historical content is not a current
instruction, and storage escaping is for format integrity, not prompt-injection prevention.

## Stored block format

The tool owns this format; callers always supply raw text:

```text
prompt-log:
prompt-log-format: raw-v1
First prompt.

Q: Keep the setting?
A: Yes.
prompt-log-end:
```

Only exact full-line `prompt-log:` and `prompt-log-end:` delimiters are structural. Inside the
body, the writer prefixes such lines, including those with trailing whitespace, with `\` so
Git cleanup cannot turn content into a delimiter. It also prefixes every line already starting
with `\` with another `\`, making the transformation reversible. All other text is unchanged.
The reader removes that one prefix and rejects invalid escape sequences. The explicit format
header distinguishes raw text from historical JSON even when the text itself looks like JSON.
Unknown format identifiers fail rather than being guessed or displayed incorrectly.

One framing LF is always inserted before the closing delimiter. An existing final LF in the
body is additional, so bodies with and without final newlines round-trip exactly after newline
normalization before Git cleanup. When Git cleans up the commit message, `show` preserves the
resulting stored text. A whitespace-only body reduced to an empty line remains readable.
The format header and outer delimiters are not part of the displayed body.

Historical-format parsing is isolated in `LegacyPromptLogReader.cs`; the two dispatch points in
`PromptLogReader.cs` and `LegacyPromptLogReaderTests.cs` are documented for eventual removal.
Current wrapping and raw-format parsing have no dependency on the historical JSON codec.

`-m` is an alias for inherited `--minver`; either may precede `prompt-log` or follow its arguments.
The value is exactly `major.minor`. The running major must match and its minor must be at least
the requested minor; patch and prerelease do not affect compatibility. Omission defaults to the
running major/minor. Help and version remain available. On incompatibility, stop and run
Agentic.Check interactively in the intended target; do not remove the version requirement.

Exit codes: **0** success, **1** operation/compatibility/validation failure, **2** invalid arguments.
Diagnostics use stderr; decoded content uses stdout without terminal markup interpretation.

## Local development feeds

Build a package with `dotnet pack src/Agentic/Agentic.csproj -c Release`. Configure the package
folder as a source in the target's `nuget.config`, optionally alongside nuget.org. Create a local
manifest explicitly in the intended target (`dotnet new tool-manifest`), then install:

```bash
dotnet tool install InnoWvate.Agentic --local --tool-manifest .config/dotnet-tools.json --version '2.*' --allow-roll-forward
```

Agentic.Check stable mode resolves `2.*`; preview resolves `2.*-*`. These allow newer compatible
minors. The SDK chooses the highest matching version across configured feeds: a stable release
can outrank an earlier preview. Use uniquely versioned fixture packages and isolated caches in
tests. Updates may downgrade a manual wrong-major or preview installation; restores use the exact
recorded version. The shorthand package is independently versioned and always uses stable resolution.
