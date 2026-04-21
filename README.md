# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

Agentic engineering setups, directives and skills that I use for building real-world .NET applications.

Everything here has been used with real-world codebases - not generated with prompts and uploaded untested.

Demos:

- [LiveCharts2 with Uno Platform](https://x.com/vincenth_net/status/2033966275324444819)
  (post with app video)
- [GPT 5.4 vs Opus 4.6 for UI with Uno Platform](https://x.com/compose/articles/edit/2031388424310075392)
  (article with side-by-side apps video)

This repo exists to:

- **filter** proven engineering value from untested content and marketing in the agentic ecosystem
- **fill** agentic ecosystem gaps, both technology-independent and for specific technologies
- **extend** the agentic ecosystem with specialized technology/pattern skills for distributed/cross-platform applications

## What's included

Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com), [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno).

### Coming

In the coming months I will be adding content for the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno) as I progress applying agentic engineering to a real-world distributed, cross-platform application that I am building with this technology stack.

👁️/⭐ this repo if this includes what you are looking for!

### dotnet plugin

Skills for .NET development.

| Skill | Description |
|-------|-------------|
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
| `uno-csharp-markup2-setup` | Add a concise, strongly-typed C# Markup 2 ([CSharpForMarkup](https://github.com/VincentH-Net/CSharpForMarkup)) Presentation project to a Uno Platform 6 solution on .NET 10/9 — replacing XAML with declarative fluent-builder C# UI. Covers MVVM/MVUX, Skia/native renderer choice, the bind-without-strings pattern, Spread, conditional children, and the markup/logic partial-class split. |
| `uno-csharp-markup2-page` | Add a new C# Markup 2 page (View + optional Model) to a Uno solution already set up for C# Markup 2 — choose MVVM, MVUX, or none (pure code-behind). Enforces the strict markup/logic split and namespace-separation conventions. |
| `uno-fluent2` | Fluent 2 Design System for Uno Platform — color, typography, geometry, materials, motion, iconography, spacing, elevation, lightweight styling, and responsive breakpoints. |
| `uno-hamburgermenu-databinding` | Data-bound, hierarchical hamburger menu with dynamic navigation using Uno Navigation Extensions `NavigationView` and MVVM. |
| `uno-livecharts2-theme-switching` | Reliable in-app dark/light/system theme switching for LiveCharts2 in Uno Platform — shared palettes, central chart refresh, and rendered-pixel verification. |
| `uno-responsive-spanning-gridwrap-layout` | A responsive, non-virtualizing wrapping grid layout with column spans, proportional stretch-to-fill, and vertically aligned gaps. |
| `uno-test-resize-app-window` | Resize a running Uno Platform desktop app window on macOS for visual testing using the Accessibility API. |

| Directive | Description |
|----------|-------------|
| [`prompt-log`](./directives/prompt-log.md) | Records sanitized user prompts and agent question-and-answer pairs as companion git commits so intent is preserved and can be replayed later. |
| [`uno-build-and-run`](./directives/uno-build-and-run.md) | Standardizes Uno app launch for agents by skipping redundant pre-builds, writing per-run stdout logs, passing `RUN_BY_AGENT`, and verifying or stopping the app with the Uno runtime tools. |

## Installation

Install an optimized combination of models, harnesses, plugins, MCP's, skills and directives for your tech stack with these steps:

1. [Foundation Setup](./setups/foundation.md)
2. For the technologies in your target stack:
    - [x] [.NET Setup](./setups/dotnet.md)
    - [ ] [UI with Uno Platform Setup](./setups/ui-uno-platform.md)

### Plugins / Skills Install

To install the plugins / skills that above setups recommend, follow below steps.

Install skills plugins in Claude Code:

```bash
claude plugin marketplace add VincentH-Net/dotnet-agentic-engineering
claude plugin install dotnet@dotnet-agentic-engineering
claude plugin install orleans@dotnet-agentic-engineering
claude plugin install uno-platform@dotnet-agentic-engineering
```

Install skills in any agent with `npx skills` (requires `Node.js`) :

```bash
# Select skills in this repo and install them in agents you choose
npx skills add VincentH-Net/dotnet-agentic-engineering
```

Note that to install the same skills in both Claude Code and Codex you can install plugins for Claude Code and use `npx skills` for Codex. There is no need to specify Codex as target agent in the `npx skills` command: Codex supports skills under the `.agents` folder, which `npx skills` always installs to, while Claude Code does not support `.agents`.

**Codex plugin** install is coming as soon as [Codex releases plugin install from GitHub repo](https://developers.openai.com/codex/plugins/build#publish-official-public-plugins).

You can also install a single skill in Codex by invoking:

```text
$skill-installer https://github.com/VincentH-Net/dotnet-agentic-engineering/tree/main/plugins/uno-platform/skills/uno-fluent2
```

### Directives Install

To install the directives that above setups recommend, paste the content of a `directives/<name>.md` file in your `AGENTS.MD`. Make sure to follow [Foundation setup](./setups/foundation.md) so you do not have to duplicate directives in `CLAUDE.MD`.

## Structure

```text
dotnet-agentic-engineering/
├── setups/             # Optimized combinations of harness, model, configuration, plugins and MCPs - for specific tech stacks
├── directives/         # Proven agent instruction snippets for AGENTS.MD / CLAUDE.MD
└── plugins/            # Technology/pattern-specific skills
```

## License

MIT
