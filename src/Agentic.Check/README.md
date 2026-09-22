# Agentic.Check

`agentic.check` optimizes your repo for agentic engineering with .NET - based technologies. You run it as `dna check`.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives, skills and tools for those
- You select which to apply
- Directives are installed / updated in `AGENTS.md` / `CLAUDE.md`, directly from the
  dotnet-agentic-engineering GitHub repo
- For Codex, rules that run `dotnet` outside its sandbox are installed in `.codex/rules`, so
  `dotnet` has network access without approval prompts
- Skills are installed / updated directly from source GitHub skill repo's with `gh skill`
- Tools that directives and skills depend on are installed / updated with `dotnet tool`, pinned in the target folder

The skills available for composition are carefully tested and are selected from
best-in-class GitHub skills repo's:

- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

The composition minimizes context usage and avoids contradictions and ambiguities,
reducing agent mistakes.

Currently supports foundational agentic habits, [.NET](https://dotnet.microsoft.com/), [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet), [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans) and [Uno Platform](https://platform.uno/).

Uno Platform skills are selected depending on detected:

- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Cupertino design system

## Prerequisites

- .NET 10 SDK or later ([install](https://dotnet.microsoft.com/download))
- `gh` CLI ([install](https://cli.github.com/))

GitHub login is optional. Without it, public sources are read anonymously, which is enough for
a handful of skills. When GitHub's anonymous rate limit is reached, `agentic.check` stops and asks
you to run `gh auth login` and rerun; completed changes are kept. When you are logged in, the
`gh` credential is reused for all GitHub requests. Automation can set `GH_TOKEN` or `GITHUB_TOKEN`.

## Usage

Install the `dna` shorthand once per machine, then run the check from the folder you want to set up:

```bash
dotnet tool install --global InnoWvate.Dna
dna check
```

`dna check` runs the latest stable `agentic.check`; so does `dotnet agentic check` once the local
tool exists. Without `dna`, run `dnx agentic.check` with the same options after `--`.

```bash
dna check --dry-run   # report recommended actions without applying them
dna check --preview   # latest directives and skills from each source's default branch
dna check --yes       # apply all recommended actions without prompting
dna check -h          # all options
```

### Tools

Directives and skills can depend on tools, which are installed before the content that needs them.
Currently the **Prompt Log** directive (`foundation-prompt-log`) does; selecting it also selects:

- **InnoWvate.Agentic**: the folder-local `dotnet agentic` tool that the directive invokes.
  It is installed in the target's `.config/dotnet-tools.json` at the newest version compatible
  with the selected directives and skills (`major.*`, or `major.*-*` with `--preview`). If that
  fails, the dependent directives and skills are skipped and reported.
- **`dna` shorthand for `dotnet agentic`**: the global `InnoWvate.Dna` launcher, offered when
  `agentic.check` is started with `dnx agentic.check` or `dotnet agentic check`. Deselect it to
  opt out. If another `dna` command is already on your PATH, `agentic.check` shows where it is
  and asks before installing. A run started with `dna check` never replaces the running `dna`;
  it stops with an update command when `dna` is too old.

Deselecting a directive or skill deselects the tools it pulled in, unless something else needs them.

### Updating

Run `dna check` regularly, and after adding a technology to your repo. Directives, skills and
tools are updated together so they stay compatible. In a fresh clone, `dotnet tool restore`
restores the recorded tool version; `dna check` offers this too.

### Folder specializing

`agentic.check` supports specializing folders in your repo, e.g. to have common directives and
skills in the repo root, but additional and different ones in `backend` and `frontend` subfolders:

1. Start `agentic.check` in the repo root and select the common set of directives and skills
2. Start `agentic.check` in the `backend` subfolder and select the additional specialized set
   for that subfolder. Directives and skills already installed above or below the target folder
   are deselected automatically; "above" stops at the repo root, or else the drive root.
3. Start `agentic.check` in the `frontend` subfolder and select the specialized set for that subfolder
4. Start your agent in the (sub)folder whose instructions you want. Codex CLI composes
   instructions from parent folders; Claude Code CLI does too, and also from child folders when
   working on files below its working directory.

## Development

To test an unpublished `InnoWvate.Agentic` package, add its packed folder as a source in the
target's `nuget.config`; see the
[InnoWvate.Agentic README](https://github.com/VincentH-Net/dotnet-agentic-engineering/blob/main/src/Agentic/README.md).
