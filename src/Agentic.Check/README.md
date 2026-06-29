# Agentic.Check

The `agentic-check` .NET tool optimizes your repo for agentic engineering with .NET - based technologies.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives and skills for those
- You select which to apply
- Directives are installed / updated in AGENTS.md, directly from the
    dotnet-agentic-engineering GitHub repo
- Skills are installed / updated directly from source GitHub skill repo's with 
  `gh skill`

The skills available for composition are carefully tested and are selected from 
best-in-class GitHub skills repo's:
- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

The composition minimizes context usage and avoids
contradictions and ambiguities, reducing agent mistakes.

Currently supports foundational agentic habits, [.NET](https://dotnet.microsoft.com/), [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet), [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans) and [Uno Platform](https://platform.uno/)

Uno Platform skills are selected depending on detected:
- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Cupertino design system

## Prerequisites

- .NET 10 SDK or later ([install](https://dotnet.microsoft.com/download))
- `gh` CLI ([install](https://cli.github.com/))

## Usage

In a target folder: 
```bash
dnx agentic.check -- -h
```

or

```bash
dotnet tool install --global agentic.check
agentic-check -h
```
