# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

- Agentic engineering `setups`, `directives` and `skills` that I use for building real-world .NET applications. Everything here has been used with real-world codebases - not generated with prompts and published untested.

- The `agentic-check` tool scans your repo and automaticaly composes, installs and updates an optimized set of directives and skills for your tech stack, directly from best-in-class GitHub skills repo's.
  ![Terminal window on macOS running dnx agentic.check in the Api3 project folder, showing large cyan and green agentic-check ASCII art. The terminal text reads ~/Projects/Api3, dnx agentic.check, .NET Agentic Engineering Check 0.3.1, Optimizes your repo for agentic engineering with .NET - based technologies. The surrounding environment is a dark terminal window with standard macOS window controls. The tone is technical, polished, and reassuring.](docs/images/agentic-check.png)

  The skills available for composition are carefully tested and are selected from 
best-in-class GitHub skills repo's:
  - [dotnet/skills](https://github.com/dotnet/skills)
  - [unoplatform/studio](https://github.com/unoplatform/studio)
  - [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
  - [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)
  
  Learn more:
  ```bash
  dnx agentic.check -- -h
  ```

Demos:

- [.NET Agentic Engineering](https://x.com/vincenth_net/status/2060378391459586239)
  (post with steps to get started)
- [LiveCharts2 with Uno Platform](https://x.com/vincenth_net/status/2033966275324444819)
  (post with app video)
- [GPT 5.4 vs Opus 4.6 for UI with Uno Platform](https://x.com/compose/articles/edit/2031388424310075392)
  (article with side-by-side apps video)

This repo saves .NET software engineers a significant, ongoing effort:

- **filter** proven engineering value from untested content and marketing in the agentic ecosystem
- **fill** agentic ecosystem gaps, both technology-independent and for specific technologies
- **extend** the agentic ecosystem with specialized technology/pattern skills for distributed/cross-platform applications
- **compose** an optimal set of directives and skills for your repo and tech stack, while avoiding contradictions and ambiguities that may cause agent mistakes.
## What's included

Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com), [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno).

In the coming months I will be adding content for the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno) as I progress applying agentic engineering to a real-world distributed, cross-platform application that I am building with this technology stack.

👁️/⭐ this repo if this includes what you are looking for!

## Installation

Install an optimized combination of models, harnesses, plugins, MCP's, skills and directives for your tech stack with these steps:

1. Check your [Agentic Development Environment Setup](/docs/dev-environment-setup.md)
2. From within a folder in your repo, run:
   ```bash
   dnx agentic.check
   ```
   Run this regularly to catch skills updates and to get add directives and skills when you expand your tech stack.

   Alternatively, you can [manually compose and install directives and skills](/docs/manual-install.md)

## Directives catalog
This repo offers the following directives (markdown snippets to include in your AGENTS.md / CLAUDE.md):

| Directive | Description |
|----------|-------------|
| [`foundation-prompt-log`](./directives/foundation-prompt-log.md) | Records sanitized user prompts and agent question-and-answer pairs as companion git commits so intent is preserved and can be replayed later. |
| [`dotnet-cli-run`](./directives/dotnet-cli-run.md) | Prevents long agent timeout delays from running `dotnet` in the background. |
| [`dotnet-build-errors-and-warnings`](./directives/dotnet-build-errors-and-warnings.md) | Configures .NET build warnings and errors and a modern C# .editorconfig, then directs agents to fix build errors and warnings or document rare justified suppressions. |
| [`uno-build-and-run`](./directives/uno-build-and-run.md) | Standardizes Uno app launch for agents by skipping redundant pre-builds, writing per-run stdout logs via `AGENT_CONSOLE_LOG`, and verifying or stopping the app with the Uno runtime tools. |

## Skills catalog
This repo offers below skills. They are grouped in plugins for ease of [manual installation](/docs/manual-install.md), however the `agentic-check` tool selects or excludes each skill individually.

### dotnet plugin

Skills for .NET development.

| Skill | Description |
|-------|-------------|
| `ensure-directives` | Create or update AGENTS.md and CLAUDE.md with agentic engineering directives for your tech stack. |
| `dotnet-livecharts2` | LiveCharts2 development guide — installation, XAML source generator integration, theme config, gotchas, and sample index with exact repo file paths. Covers all platforms (WinUI, Uno, Avalonia, MAUI, WPF, Blazor, WinForms, Eto). |
| `dotnet-modern-csharp-editorconfig` | Drop-in opinionated `.editorconfig` for modern C# (C# 14 / .NET 10, also works with C# 10–13) — formatting, naming, style, and preview analyzer severities. Covers required `.csproj` flags and the .NET 8 vs .NET 9+ build-respect-editorconfig distinction. |

### orleans plugin

Skills for [Microsoft Orleans 10](https://learn.microsoft.com/en-us/dotnet/orleans) actor-based distributed applications.

| Skill | Description |
|-------|-------------|
| `orleans-result-pattern` | Concise, version-tolerant result pattern for Orleans 8+ grain calls — `Result` / `Result<T>` with `enum ErrorNr` + `string` errors, `[Immutable]` for zero-copy within-silo calls, implicit conversions, and RFC7807 `ValidationProblemDetails` via `TryAsValidationErrors`. |
| `orleans-multiservice-pattern` | Modular-monolith pattern for Orleans 10 — host multiple logical services in one silo with strict `Apis → Contracts`, `Apis → Service`, `Service → Contracts` dependency rules so any logical service can later be extracted to its own physical microservice with minimal changes. |

### uno-platform plugin

Skills for [Uno Platform](https://platform.uno) cross-platform app development.

| Skill | Description |
|-------|-------------|
| `uno-agentic-support` | In-app support for agent-driven Uno app runs — detects `AGENT_CONSOLE_LOG`, captures early stdout/stderr logging, and disables Uno Studio Hot Reload / Hot Design UI during agent UI testing. |
| `uno-csharpmarkup2` | Build a Uno Platform 6 UI in pure C# with [C# Markup 2 (CSharpForMarkup)](https://github.com/VincentH-Net/CSharpForMarkup) — covers both the initial Presentation-project setup and ongoing per-page authoring via the included `New-View.ps1` helper. MVVM/MVUX, Skia/native renderer, bind-without-strings, Spread, conditional children, and the markup/logic partial-class split. |
| `uno-fluent2` | Fluent 2 Design System for Uno Platform — color, typography, geometry, materials, motion, iconography, spacing, elevation, lightweight styling, and responsive breakpoints. |
| `uno-hamburgermenu-databinding` | Data-bound, hierarchical hamburger menu with dynamic navigation using Uno Navigation Extensions `NavigationView` and MVVM. |
| `uno-livecharts2-theme-switching` | Reliable in-app dark/light/system theme switching for LiveCharts2 in Uno Platform — shared palettes, central chart refresh, and rendered-pixel verification. |
| `uno-mvvm` | Uno Platform MVVM with CommunityToolkit.Mvvm — partial observable properties, generated commands, constructor DI, x:Bind patterns, and Uno Navigation from ViewModels. |
| `uno-xaml` | Uno Platform XAML correctness and performance — deferred loading, virtualized templates, UI lifecycle cleanup, UI-thread safety, input scopes, keyboard accelerators, focus, and drag/drop caveats without overriding selected MVVM, navigation, or design-system guidance. |
| `uno-responsive-spanning-gridwrap-layout` | A responsive, non-virtualizing wrapping grid layout with column spans, proportional stretch-to-fill, and vertically aligned gaps. |
| `uno-test-resize-app-window` | Resize a running Uno Platform desktop app window on macOS for visual testing using the Accessibility API. |

## License

MIT
