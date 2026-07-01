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

### Folder Specializing
`agentic-check` supports specializing folders in your repo, e.g. to have common directives and skills in the repo root, but additional and different ones in `backend` and `frontend` subfolders:
1. Start `agentic-check` in the repo root and select the common set of directives and skills to install there
2. Start `agentic-check` in the `backend` subfolder and select the additional specialized set of directives and skills for that subfolder - `agentic-check` will automatically deselect any directives and skills that are already installed above or below the target folder. For `agentic-chech`, above terminates at the repo root or else the drive root.
3. Start `agentic-check` in the `frontend` subfolder and select the specialized set of directives and skills for that subfolder
4. Start your agent in a (sub)folder of choice to use that specialized set of instructions. Multiple harnesses support this, including Codex CLI (composes above) and Claude Code CLI (composes above, as well as below when working on files below it's working dir).
