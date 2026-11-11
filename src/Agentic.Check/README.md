# Agentic.Check

`agentic.check` optimizes your repo for [agentic engineering with .NET - based technologies](https://github.com/VincentH-Net/dotnet-agentic-engineering). You run it as `dna check`.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives, skills and tools for those
- You select which to apply
- Directives are installed / updated in `AGENTS.md` / `CLAUDE.md`, directly from the
  dotnet-agentic-engineering GitHub repo
- For Codex, rules that run `dotnet` outside its sandbox are installed in `.codex/rules`, so
  `dotnet` has network access without approval prompts
- Skills are installed / updated directly from source GitHub skill repo's with `gh skill`, and a skill a newer release no longer offers is removed again
- Tools that directives and skills depend on are installed / updated with `dotnet tool`, pinned once per repository; a tool that is already the latest on nuget.org is reported as up to date and left alone

The skills available for composition are carefully tested and are selected from
best-in-class GitHub skills repo's:

- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [noknowscode/UnoPlatformSkills](https://github.com/noknowscode/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

The composition minimizes context usage and avoids contradictions and ambiguities,
reducing agent mistakes.

Currently supports foundational agentic habits, [.NET](https://dotnet.microsoft.com/), [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet), [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans) and [Uno Platform](https://platform.uno/).

Uno Platform skills are selected depending on detected:

- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Simple / Cupertino design system
- Uno.Sdk version: the current Uno Platform Studio skills from Uno.Sdk 7.0 on, the Studio skills written for it on an earlier Uno.Sdk

The .NET CLI end-to-end testing skill is selected when a project interacts with a terminal: it is packaged as a dotnet tool, references a command-line or terminal UI package such as System.CommandLine, Spectre.Console or Hex1b, or reads console input. A console app that only writes output does not qualify.

With `--preview`, the .NET breaking-changes skill is selected when a project is a published library: it tracks its public API, or it is explicitly packaged and is not a dotnet tool. A class library that only the same repo uses does not qualify. The .NET 11 Process API and System.Text.Json skills are selected, also with `--preview`, when a project targets `net11.0` or later. The .NET 10 to .NET 11 migration skill is selected when a project still targets `net10.0`.

It also asks once which phase the project is in, pre-release or in production, because agents rename and restructure differently by it. Switch it when the project goes to production, in the action list or with `--project-phase production`.

## Prerequisites

- .NET 10 SDK or later ([install](https://dotnet.microsoft.com/download))
- `gh` CLI ([install](https://cli.github.com/))
- Optional: Git ([install](https://git-scm.com/downloads)). Inside a repository that has a `.gitignore`, the file list comes from git, so ignored output folders are never scanned. Otherwise the folder is walked, skipping `bin`, `obj`, `TestResults` and similar output folders.

GitHub login is optional. Without it, public sources are read anonymously, which is enough for
a handful of skills. When GitHub's anonymous rate limit is reached, `agentic.check` stops and asks
you to run `gh auth login` and rerun; completed changes are kept. When you are logged in, the
`gh` credential is reused for all GitHub requests. Automation can set `GH_TOKEN` or `GITHUB_TOKEN`.

## Install, update and usage

See [Get started](https://github.com/VincentH-Net/dotnet-agentic-engineering#get-started)
