# InnoWvate.Agentic

The folder-local `dotnet agentic` tool: deterministic prompt-log commands for coding agents and
humans, plus a `check` shortcut to the latest Agentic.Check.

[Agentic.Check](https://www.nuget.org/packages/Agentic.Check) installs and updates it when you
select the Prompt Log directive, pinned in the target folder's `.config/dotnet-tools.json` so the
tool, directive and skill versions travel together. Specialized folders can have their own version. The optional global
[`dna` shorthand](https://www.nuget.org/packages/InnoWvate.Dna) lets you type `dna` instead of
`dotnet agentic`.

```bash
dotnet agentic --help
dotnet agentic check                                        # run the latest Agentic.Check
dotnet agentic prompt-log                                   # show the latest 20 prompt logs
dotnet agentic prompt-log --limit 50
dotnet agentic prompt-log --all
dotnet agentic prompt-log --since 2026-01-01 --until 2026-02-01
dotnet agentic prompt-log check --commit HEAD
```

## Prompt logging

Your prompts, and your answers to the questions an agent asks, are the most important part of
your source. The Prompt Log directive makes agents record them in the commit message of every
code commit they create, so intent stays in version control and can be replayed later with
better models and tools.

The agent collects and sanitizes the log; the tool does the deterministic part:

- `prompt-log wrap` frames the raw text as a commit-message block, escaping delimiter lines so
  Git cannot corrupt it. The agent includes the block in its commit message. The directive
  invokes it as `dotnet agentic prompt-log wrap --input - -m 2.3`, with the log on stdin.
- `prompt-log show` (the default) reads the blocks back from Git history: the newest 20 by
  default, in chronological order, each with its commit hash and date.
- `prompt-log check` validates the block structure of one commit.

Git access is read-only. The agent stays in control of staging and committing.

A stored block looks like this:

```text
prompt-log:
prompt-log-format: raw-v1
First prompt.

Q: Keep the setting?
A: Yes.
prompt-log-end:
```

Prompt logs in older formats in your history are still displayed.

## Compatibility

Directive invocations pass `-m 2.3` (`--minver`): the installed tool must have that major
version and at least that minor. On a mismatch the command stops and asks to run Agentic.Check
in the target directory. Do not remove `-m` to bypass this. Without `-m`, the running version is assumed.

Exit codes: **0** success, **1** failure, **2** invalid arguments. Diagnostics go to stderr.

## After cloning

```bash
dotnet tool restore
```

restores the exact recorded version. Or run `dna check`, or `dnx agentic.check` on a machine
without `dna`, which offers the restore and the `dna` shorthand. A .NET 10 or later SDK must be selected for the folder.

## Local development feeds

Pack with `dotnet pack src/Agentic/Agentic.csproj -c Release`, add the output folder as a source
in the target's `nuget.config`, then in the target:

```bash
dotnet new tool-manifest
dotnet tool install InnoWvate.Agentic --local --version '2.*' --allow-roll-forward
```

Agentic.Check resolves `2.*` (stable) or `2.*-*` (preview) as the highest match across all
configured feeds, so use unique versions for test packages.
