# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

Agentic engineering `directives`, `skills` and `tools` that I use for building real-world .NET applications, plus the [dev environment setup](docs/dev-environment-setup.md) I use them with. Everything here has been used with real-world codebases - not generated with prompts and published untested.

Run `dnx agentic.check` once per machine; after that, `dna` does everything:

- **`dnx agentic.check`** scans your repo, detects your tech stack, and composes, installs and updates an optimized set of directives and skills, directly from best-in-class GitHub skills repo's. Where a directive or skill needs deterministic tooling, it installs that tool too, pinned in the target folder. It also installs the `dna` shorthand, so you only run it once per machine.
- **`dna check`** does the same check in any folder from then on: set up a repo, specialize a folder, or update. Directives, skills and tools are updated together, so they stay compatible.
- **`dna prompt-log`** shows the prompts and answers that produced your commits.

<!-- TODO screenshot: replace with a 2.3 capture of `dnx agentic.check` showing the action list with directives, skills, and the InnoWvate.Agentic and dna tool actions. -->
![Terminal window on macOS running dnx agentic.check in the Api3 project folder, showing large cyan and green agentic-check ASCII art. The terminal text reads ~/Projects/Api3, dnx agentic.check, .NET Agentic Engineering Check 0.3.1, Optimizes your repo for agentic engineering with .NET - based technologies. The surrounding environment is a dark terminal window with standard macOS window controls. The tone is technical, polished, and reassuring.](docs/images/agentic-check.png)

The skills available for composition are carefully tested and are selected from best-in-class GitHub skills repo's:

- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

This repo saves .NET software engineers a significant, ongoing effort:

- **filter** proven engineering value from untested content and marketing in the agentic ecosystem
- **fill** agentic ecosystem gaps, both technology-independent and for specific technologies
- **extend** the agentic ecosystem with specialized technology/pattern skills for distributed/cross-platform applications
- **compose** an optimal set of directives, skills and tools for your repo and tech stack, while avoiding contradictions and ambiguities that may cause agent mistakes

Demos:

- [.NET Agentic Engineering](https://x.com/vincenth_net/status/2060378391459586239)
  (post with steps to get started)
- [LiveCharts2 with Uno Platform](https://x.com/vincenth_net/status/2033966275324444819)
  (post with app video)
- [GPT 5.4 vs Opus 4.6 for UI with Uno Platform](TODO-replace-with-public-article-url)
  (article with side-by-side apps video)

👁️/⭐ this repo if this includes what you are looking for!

## Get started

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [`gh` CLI](https://cli.github.com/). Skills are installed with `gh skill`, which uses GitHub's API quota. Anonymous access is enough for a handful of skills; `gh auth login` gives you a much higher quota, and `agentic.check` tells you when you need it.
- Recommended: check the [Agentic Development Environment Setup](docs/dev-environment-setup.md) for models, harnesses and MCPs

### 1. Once per machine

From a folder in your repo, run:

```bash
dnx agentic.check
```

It detects .NET, ASP.NET Core, Orleans and Uno Platform usage, lists the recommended directives, skills and tools, and lets you select which to apply. Directives are installed in `AGENTS.md` / `CLAUDE.md`, skills with `gh skill`, tools with `dotnet tool`. Run `dnx agentic.check -- -h` for options such as `--dry-run` and `--preview`.

Selecting the **Prompt Log** directive also selects the tools it uses:

- `InnoWvate.Agentic`, the folder-local `dotnet agentic` tool. It is pinned in the target folder's `.config/dotnet-tools.json`, so it travels with your source and matches the directives and skills installed there. Specialized folders can have their own version.
- `InnoWvate.Dna`, the `dna` shorthand for `dotnet agentic`: a small global launcher so you type less. Deselect it to opt out.

With `dna` installed you will not need `dnx agentic.check` again on this machine. To install `dna` without running a check: `dotnet tool install --global InnoWvate.Dna`.

Alternatively, you can [manually compose and install directives and skills](docs/manual-install.md).

### 2. Work with your agent

Agents follow the installed directives and skills. With the Prompt Log directive, every code commit an agent creates carries the prompts and answers that produced it - the most important part of your source. This preserves intent across sessions and agents. Read them back any time:

```bash
dna prompt-log
dna prompt-log --since 2026-09-01
```

### 3. Other repos, folders and updates

```bash
dna check
```

Run it in any folder to set it up, after adding a technology, and regularly to pick up skill updates. It runs the latest `agentic.check` and updates directives, skills and tools together. `dotnet agentic check` and `dnx agentic.check` do the same.

Teammates who clone the repo run `dnx agentic.check` once on their machine too; it restores the pinned `dotnet agentic` version and installs `dna`. `dotnet tool restore` restores the tool alone.

### Specialize folders

Run `dna check` in the repo root for the common directives and skills, then in a subfolder such as `backend` or `frontend` for the additional ones that belong there. It deselects what is already installed above or below the target folder. Start your agent in the folder whose instructions you want; Codex CLI and Claude Code CLI both compose instructions from parent folders.

## The tools

| Package | Command | Installed | Purpose |
|---|---|---|---|
| [Agentic.Check](src/Agentic.Check/README.md) | `dnx agentic.check` | Not installed; run once per machine with `dnx`, afterwards via `dna check` | Detects your stack; composes, installs and updates directives, skills and tools |
| [InnoWvate.Agentic](src/Agentic/README.md) | `dotnet agentic` | Per target folder, pinned in its `.config/dotnet-tools.json` | Deterministic prompt-log commands for agents and humans; `check` runs the latest Agentic.Check |
| [InnoWvate.Dna](src/Dna/README.md) | `dna` | Global, optional | Shorthand for the folder-local `dotnet agentic`; `dna check` works everywhere |

Directives and skills tell agents what to do in words. Words cost tokens on every run, and agents
follow them imperfectly. `dotnet agentic` is where deterministic steps move out of that text into
code that runs the same way every time. Prompt logging is the first step; more directive and skill
logic will follow, making both shorter and more reliable.

## What's included

Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com), [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno).

In the coming months I will be adding content for the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno) as I progress applying agentic engineering to a real-world distributed, cross-platform application that I am building with this technology stack.

## Directives catalog

This repo offers the following directives (markdown snippets to include in your AGENTS.md / CLAUDE.md):

| Directive | Description |
|----------|-------------|
| [`foundation-documentation-sources`](./directives/foundation-documentation-sources.md) | Prioritizes first-party vendor MCPs for external documentation, using Microsoft Learn before Context7 for Microsoft technologies. |
| [`foundation-prompt-log`](./directives/foundation-prompt-log.md) | Records sanitized user prompts and agent question-and-answer pairs in code commit messages so intent is preserved and can be replayed later. Uses the `dotnet agentic` tool. |
| [`dotnet-cli-run`](./directives/dotnet-cli-run.md) | Prevents long agent timeout delays from running `dotnet` in the background. |
| [`dotnet-build-errors-and-warnings`](./directives/dotnet-build-errors-and-warnings.md) | Configures .NET build warnings and errors and a modern C# .editorconfig, then directs agents to fix build errors and warnings or document rare justified suppressions. |
| [`uno-build-and-run`](./directives/uno-build-and-run.md) | Standardizes Uno app launch for agents by skipping redundant pre-builds, writing per-run stdout logs via `AGENT_CONSOLE_LOG`, and verifying or stopping the app with the Uno runtime tools. |

Directive blocks are delimited by `<!-- directive-name:start -->` and `<!-- directive-name:end -->`.
`agentic.check` also recognizes the older `dotnet-agentic-engineering:` prefix and removes it when
you select a directive for installation or update. Unselected directives are preserved.

## Skills catalog

This repo offers below skills. They are grouped in plugins for ease of [manual installation](docs/manual-install.md), however `agentic.check` selects or excludes each skill individually.

### dotnet plugin

Skills for .NET development.

| Skill | Description |
|-------|-------------|
| `cli-e2e-testing` | CLI end-to-end testing guide for real terminal flows with Hex1b automation, keyboard input, observable terminal output, and asciinema recordings. |
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
