# InnoWvate.Agentic

This repo-local `dotnet agentic` tool is part of [dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering); it contains commands that replace deterministic logic in agent skills and directives. This enables agents to execute more reliably and use less context; agent instructions contain only the nondeterministic parts, the tool contains the deterministic parts.

This first version contains a `check` shortcut to the latest [Agentic.Check](https://www.nuget.org/packages/Agentic.Check), plus `prompt-log` commands for humans and agents.

## Commands

For humans:
```bash
dna --help
dna check                                        # run the latest Agentic.Check
dna prompt-log                                   # show the latest 20 prompt logs
dna prompt-log --limit 50
dna prompt-log --all
dna prompt-log --since 2026-01-01 --until 2026-02-01
```

The optional `dna` shortcut is for humans only; agents must use `dotnet agentic` instead.
Agents can see all available commands, including additional agent-only commands, with `dotnet agentic --help`.

## Compatibility

`dotnet agentic` invocations in directives or skills pass `-m <major>.<minor>` (`--minver`): the installed tool must have the same major version and at least that minor. On a version mismatch the command stops and tells the agent to have you exit the session, run `dna check` in the target directory, and restart or resume it. Without `-m`, the running version is assumed.

Prompt logs in older formats in your history are still displayed.

## Prerequisites

- .NET 10 SDK or later
- `git` on the PATH

## Install, update and usage

See [Get started](https://github.com/VincentH-Net/dotnet-agentic-engineering#get-started)
