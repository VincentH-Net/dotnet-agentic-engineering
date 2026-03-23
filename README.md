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
| `uno-responsive-spanning-gridwrap-layout` | A responsive, non-virtualizing wrapping grid layout with column spans, proportional stretch-to-fill, and vertically aligned gaps. |
| `uno-test-resize-app-window` | Resize a running Uno Platform desktop app window on macOS for visual testing using the Accessibility API. |

## Installation

Install only the directives and skills relevant for your use case.

Install a directive:
Paste the content of a `directives/<name>.md` file in your `AGENTS.MD` / `CLAUDE.md`

Install a skills plugin in Claude Code:

```bash
claude plugin add VincentH-Net/dotnet-agentic-engineering --plugin dotnet
claude plugin add VincentH-Net/dotnet-agentic-engineering --plugin uno-platform
```

Install a skill in Codex:

```bash
$skill-installer install https://github.com/VincentH-Net/dotnet-agentic-engineering/tree/main/plugins/uno-platform/skills/uno-fluent2
```

Install a skill / all skills in any agent with `npx skills` (requires `Node.js`) :

```bash
# List skills in this repo
npx skills add VincentH-Net/dotnet-agentic-engineering --list

# Install a specific skill
npx skills add VincentH-Net/dotnet-agentic-engineering --skill uno-fluent2
```

## Structure

```text
dotnet-agentic-engineering/
├── setups/             # Optimized combinations of harness, model, configuration, plugins, MCPs and skill libraries - for specific use cases
├── directives/         # Proven agent instruction snippets for AGENTS.MD / CLAUDE.MD
├── plugins/            # Technology/pattern-specific knowledge
└── examples/           # Sample projects demonstrating usage
```

## License

MIT
