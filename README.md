# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

Agentic engineering setups, directives and skills that I use for building real-world .NET applications.

Everything here has been used with real-world codebases - not generated with prompts and uploaded untested.

This repo exists to:

- filter proven engineering value from untested content and marketing in the agentic ecosystem
- fill agentic ecosystem gaps, both technology-independent and for specific technologies
- extend the agentic ecosystem with specialized technology/pattern skills for distributed/cross-platform applications

## What's included

Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com) and [Uno Platform](https://platform.uno).

### Coming

In the coming months I will be adding content for the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno) as I progress applying agentic engineering to a real-world distributed, cross-platform application that I am building with this technology stack.

👁️/⭐ this repo if this includes what you are looking for!

### dotnet plugin

Skills for .NET development.

| Skill | Description |
|-------|-------------|
| `dotnet-livecharts2` | LiveCharts2 development guide — installation, XAML source generator integration, theme config, gotchas, and sample index with exact repo file paths. Covers all platforms (WinUI, Uno, Avalonia, MAUI, WPF, Blazor, WinForms, Eto). |

### uno-platform plugin

Skills for [Uno Platform](https://platform.uno) cross-platform app development.

| Skill | Description |
|-------|-------------|
| `uno-fluent2` | Fluent 2 Design System for Uno Platform — color, typography, geometry, materials, motion, iconography, spacing, elevation, lightweight styling, and responsive breakpoints. |
| `uno-hamburgermenu-databinding` | Data-bound, hierarchical hamburger menu with dynamic navigation using Uno Navigation Extensions `NavigationView` and MVVM. |
| `uno-livecharts2-theme-switching` | Reliable in-app dark/light/system theme switching for LiveCharts2 in Uno Platform — shared palettes, central chart refresh, and rendered-pixel verification. |
| `uno-responsive-spanning-gridwrap-layout` | A responsive, non-virtualizing wrapping grid layout with column spans, proportional stretch-to-fill, and vertically aligned gaps. |
| `uno-test-resize-app-window` | Resize a running Uno Platform desktop app window on macOS for visual testing using the Accessibility API. |

## Installation

Install an optimized combination of models, harnesses, plugins, MCP's, skills and directives for your tech stack with these steps:

1. [Foundation Setup](./setups/foundation.md)
2. For the technologies in your target stack:
    - [.NET Setup](./setups/dotnet.md)
    - [UI with Uno Platform Setup](./setups/ui-uno-platform.md)

Install only the directives and skills relevant for your use case.

### Plugins / Skills install

To install the plugins / skills that above setups recommend, follow below steps.

Install a skills plugin in Claude Code:

```bash
claude plugin marketplace add VincentH-Net/dotnet-agentic-engineering
claude plugin install dotnet@dotnet-agentic-engineering
claude plugin install uno-platform@dotnet-agentic-engineering
```

For Codex you need to install per skill for now; **Codex plugin** install is coming as soon as Codex releases plugin install from GitHub repo.

Install a skill in Codex by invoking:

```text
$skill-installer https://github.com/VincentH-Net/dotnet-agentic-engineering/tree/main/plugins/uno-platform/skills/uno-fluent2
```

Install a skill / all skills in any agent with `npx skills` (requires `Node.js`) :

```bash
# List skills in this repo
npx skills add VincentH-Net/dotnet-agentic-engineering --list

# Install a specific skill
npx skills add VincentH-Net/dotnet-agentic-engineering --skill uno-fluent2
```

Note that to install the same skills in both Claude Code and Codex you can install plugins for Claude Code and use `npx skills` for Codex. There is no need to specify Codex as target agent in the `npx skills` command: Codex supports skills under the `.agents` folder, which `npx skills` always installs to, while Claude Code does not support `.agents`.

### Directive install

Paste the content of a `directives/<name>.md` file in your `AGENTS.MD`. Make sure to follow [Foundation setup](./setups/foundation.md) so you do not have to duplicate directives in `CLAUDE.MD`.

## Structure

```text
dotnet-agentic-engineering/
├── setups/             # Optimized combinations of harness, model, configuration, plugins, MCPs and skill libraries - for specific use cases
├── directives/         # Proven agent instruction snippets for AGENTS.MD / CLAUDE.MD
└── plugins/            # Technology/pattern-specific knowledge
```

## License

MIT
