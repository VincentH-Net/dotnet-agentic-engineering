# Agentic.Check

`agentic.check` optimizes your repo for [agentic engineering with .NET - based technologies](https://github.com/VincentH-Net/dotnet-agentic-engineering). You run it as `dna check`.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives, skills and tools for those
- You select which to apply
- Directives are installed / updated in `AGENTS.md` / `CLAUDE.md`, directly from the
  dotnet-agentic-engineering GitHub repo
- For Codex, rules that run `dotnet` outside its sandbox are installed in `.codex/rules`, so
  `dotnet` has network access without approval prompts
- Skills are installed / updated directly from source GitHub skill repo's with `gh skill`
- Tools that directives and skills depend on are installed / updated with `dotnet tool`, pinned once per repository

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

## Install, update and usage

See [Get started](https://github.com/VincentH-Net/dotnet-agentic-engineering#get-started)
