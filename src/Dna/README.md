# dna shorthand command

Install the optional global launcher with `dotnet tool install --global InnoWvate.Dna`,
or select the `dna` shorthand action in Agentic.Check. It is versioned independently,
starting at 1.0.0.

`dna <args>` forwards to `dotnet tool run agentic -- <args>` in the current directory.
The local tool manifest determines the InnoWvate.Agentic version. For example:

```sh
dna
dna -h
dna prompt-log show -m 2.3
dna prompt-log show --since 2026-01-26 --until 2026-02-07 -m 2.3
dna prompt-log check --commit HEAD -m 2.3
```

`dna check <args>` runs `dotnet tool exec Agentic.Check -- <args>` directly, using
normal .NET stable-package resolution, NuGet settings, and caching. It works without
a local companion installation and is equivalent to `dotnet agentic check <args>`
when the companion is installed. Both require a .NET 10 or later SDK selected for
the current directory, including any `global.json` selection.

The launcher preserves arguments, working directory, standard input/output/error,
and the child exit code. All other commands require a restored local agentic tool.
Before a normal invocation, a local `dotnet tool list --local --format json` query checks
the current folder's tool scope. If InnoWvate.Agentic is absent, dna exits with code 1
and writes this message to stderr:

```text
`dotnet agentic` is not installed in the scope of the current working folder.
Please run `dna check` to install it.
```

Tools declared in a manifest but needing restoration retain .NET's restore diagnostics.
SDK and manifest errors retain their original diagnostics too. `dna check` bypasses this
availability query and can install the companion. The query uses local metadata only.
It does not install the companion automatically or modify PATH or shell profiles.

Agentic.Check offers a global update when this package is already installed. An
unrelated `dna` found on PATH is reported without executing it. You may install
anyway, but the other command may hide this shorthand. In unattended mode a
conflicting shorthand action is skipped. Update manually with
`dotnet tool update --global InnoWvate.Dna`.
