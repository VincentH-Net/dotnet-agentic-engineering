# dna

`dna` is the shorthand for `dotnet agentic`: a global launcher for that folder-local tool, so you type less:

```bash
dna check                     # run the latest Agentic.Check to set up or update this repo
dna prompt-log                # show the latest 20 prompt logs
dna prompt-log --limit 50
dna prompt-log --all
dna prompt-log --since 2026-01-26 --until 2026-02-07
dna prompt-log check --commit HEAD
dna -h
```

`dna <args>` runs `dotnet agentic <args>` in the current folder, so that folder's pinned
[InnoWvate.Agentic](https://www.nuget.org/packages/InnoWvate.Agentic) version is used.
`dna check` runs the latest stable [Agentic.Check](https://www.nuget.org/packages/Agentic.Check)
directly and works before the local tool is installed. Once `dna` is on a machine, `dna check`
replaces `dnx agentic.check` there.

## Install

Agentic.Check selects the `dna` shorthand by default when it installs the local tool; deselect it
to opt out. Or install it yourself:

```bash
dotnet tool install --global InnoWvate.Dna
```

Requires a .NET 10 or later SDK selected for the current folder. If another `dna` command is
already on your PATH, Agentic.Check shows where it is and asks before installing, because that
command may hide this one.

## Update

```bash
dotnet tool update --global InnoWvate.Dna
```

Agentic.Check also offers this update when you start it with `dnx agentic.check` or
`dotnet agentic check`. Started with `dna check`, it cannot replace the running `dna`, so it only
verifies that `dna` is recent enough and stops with this update command when it is not.

## When the local tool is missing

In a folder without InnoWvate.Agentic, `dna` exits with code 1 and prints:

```text
`dotnet agentic` is not installed in the scope of the current working folder.
Please run `dna check` to install it.
```

The launcher is deliberately tiny and versioned independently. Functionality lives in the
folder-pinned InnoWvate.Agentic and the latest Agentic.Check.
