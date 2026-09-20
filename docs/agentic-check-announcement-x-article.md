# Introducing agentic-check for .NET agentic engineering

If you use coding agents on real .NET repos, the hardest part is not installing one more prompt or skill. It is composing the right set of directives and skills for the codebase in front of you, without adding conflicting instructions or wasting context.

`agentic-check` is a .NET tool that scans a target folder, detects the technologies in use, and recommends a tested set of directives and skills for agentic engineering with .NET-based stacks.

Try it:

```bash
dnx Agentic.Check -- -h
```

Repo: [github.com/VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

NuGet: [nuget.org/packages/Agentic.Check](https://www.nuget.org/packages/Agentic.Check)

[Insert screenshot: terminal running `dnx Agentic.Check`, showing the agentic-check header, target directory table, and recommendation list.]

## What it does

`agentic-check` detects the stack in the current target folder and recommends the directives and skills that match it.

It currently supports:

- Agentic foundation habits
- .NET
- ASP.NET Core
- Microsoft Orleans
- Uno Platform

For Uno Platform, it also detects install gates such as MVVM/MVUX, XAML vs C# markup, and design system choices.

## Directives and skills, selected together

The tool installs and updates directives in `AGENTS.md` and `CLAUDE.md`, and installs skills through `gh skill`.

It composes from tested sources including:

- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

The goal is simple: give agents the guidance they need for this repo, and avoid unrelated or contradictory context.

[Insert screenshot: recommendation list with directives first, skills grouped by repo and plugin, with version column visible.]

## Folder specialization

Many repos are not one uniform codebase. You may have common repo-level guidance, plus different needs in `backend`, `frontend`, `mobile`, or `tests`.

`agentic-check` is target-directory focused. Run it in the repo root for common directives and skills, then run it again in a subfolder to specialize that scope.

By default, target directory specialization is ON. The tool scans upwards and downwards, then deselects actions that are already present in another scope. That makes it easier to keep shared guidance at the root and specialized guidance where it belongs.

[Insert screenshot: target directory specialization ON, with already-present actions highlighted or deselected.]

## Preview and update flows

Stable mode follows released skill versions.

Preview mode can use default branches so you can evaluate the latest directive and skill changes before they are released.

The tool also checks for repo-local skill updates and shows what would change in dry-run mode.

```bash
dnx Agentic.Check -- --dry-run
dnx Agentic.Check -- --preview
```

## Built for repeated use

Run `agentic-check` when:

- starting agentic engineering in a repo
- adding ASP.NET, Orleans, Uno Platform, or other detected stack features
- creating specialized guidance for a subfolder
- checking for skill updates
- validating that your repo-local agent setup is still current

The tool also includes small CLI usability details: clear tables, grouped recommendations, keyboard-driven selection, F1/F2 help links, and terminal end-to-end test coverage.

## Get started

Prerequisites:

- .NET 10 SDK or later
- GitHub CLI with `gh skill`

Run:

```bash
dnx Agentic.Check -- -h
```

Then run it from the folder you want to optimize.

Repo: [github.com/VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

NuGet: [nuget.org/packages/Agentic.Check](https://www.nuget.org/packages/Agentic.Check)
