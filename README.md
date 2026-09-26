# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

Agentic engineering `directives`, `skills` and `tools` that I use for building real-world .NET applications, plus the [dev environment setup](docs/dev-environment-setup.md) I use them with. Everything here has been used with real-world codebases - not generated with prompts and published untested.

> [!NOTE]
> [Directives](#directives-catalog) are small markdown snippets with agent instructions. They are included in `AGENTS.md`to ensure that agents consistently apply key habits, skills and tools.

Install `dna` once per machine, then one command does everything:

- **`dna check`** scans your repo, detects your tech stack, and composes, installs and updates an optimized set of directives and skills, directly from best-in-class GitHub skills repo's. Where a directive or skill needs deterministic tooling, it installs that tool too, pinned once per repository. Run it again any time: directives, skills and tools are updated together, so they stay compatible.
- **`dotnet agentic`** is the repo-local tool that directives and skills call for their deterministic steps; `dna check` installs it and keeps it versioned with them, and then `dna` acts as a shortcut for the `dotnet agentic` commands so you have less to remember and type.
- **`dna prompt-log`** shows the prompts and answers that produced your commits.

> [!NOTE]
> `dna`: [dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering) in one command

<!-- TODO screenshot: replace with a 2.3 capture of `dna check` showing the action list with directives, skills, and the InnoWvate.Agentic and dna tool actions. -->
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
- eliminate papercuts: prevents long codex timeout delays from repeatedly trying to run `dotnet` without network access
- **improve agent reliability and reduce context usage** by moving deterministic logic out of directives and skills into `dotnet agentic`, a repo-local tool

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

### 1. Install or update in a repo

From a repo root folder, run:

```bash
dnx agentic.check
```

This detects .NET, ASP.NET Core, Orleans and Uno Platform usage, lists the recommended directives, skills and tools, and lets you select which to install or update.

For Uno Platform, the offered skills are further refined depending on detected:

- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Cupertino design system


By default skills come from the latest stable release in each source repo; `--preview` switches that to each source repo's default branch. Target agents default to the ones found on your machine; `--agents` overrides that and decides the skills directories, such as `.claude/skills` and `.agents/skills`. For more options such as `--dry-run` and `--preview`, run:

```bash
dnx agentic.check -- -h
```

Directives are installed in `AGENTS.md` (which is included in `CLAUDE.md`), skills with `gh skill`, tools with `dotnet tool`; for Codex, rules to run `dotnet` with network access go in `.codex/rules`.

GitHub login is optional. Without it, public sources are read anonymously, which is enough for
a handful of skills. If GitHub's anonymous rate limit is reached, `agentic.check` stops and asks
you to run `gh auth login`or wait, and then rerun; completed changes are kept. When you are logged in, the
`gh` credential is reused for all GitHub requests. Automation can set `GH_TOKEN` or `GITHUB_TOKEN`.

`agentic.check` offers to install or update `dotnet agentic` in your repo when a selected directive or skill depends on it.

It also offers to install the global `dna`shorthand command for `agentic.check` and `dotnet agentic` (the repo setup tool and the repo-local tool that directives and skills call). After this, you can also install or update repo's with:

```bash
dna check
```

> [!TIP]
> The `dna`shorthand is not _required_; if you cannot install a global tool, or another `dna` command is on your PATH, use `dnx agentic.check` instead of `dna check` and use `dotnet agentic <command>`for any other `dna <command>`.

#### Specialize folders

You can specialize folders within your repo with subsets of directives and skills local to that context, e.g. frontend or backend folders. This reduces your agent context size as compared to installing all directives and skills in the repo root. For details on this:
```bash
dna check -h
```

#### Keep your repo current

Run `dna check` again after adding a technology, and regularly to pick up skill updates. Directives, skills and tools are updated together. Teammates who clone the repo install `dna` the same way and run `dna check`, which restores the pinned tool; `dotnet tool restore` restores it alone.

Alternatively, you can [manually compose and install directives and skills](docs/manual-install.md).

### 2. Work with your agent

Agents follow the installed directives and skills. Directives may use skills; directives and skills may use `dotnet agentic`. Humans use `dna`; to see the available commands, run:

```bash
dna -h
```

#### The prompt log

With the [Prompt Log directive](directives/foundation-prompt-log.md), every code commit an agent creates carries the prompts and answers that produced it - the most important part of your source : your input.

This preserves intent across sessions and agents; it enables reusing intent for context in later sessions, or even replaying intent when more capable models appear. Read it back any time:

```bash
dna prompt-log -h
dna prompt-log
dna prompt-log --since 2026-09-01
```

## The tools

| Package | Command | Installed | Purpose |
|---|---|---|---|
| [Agentic.Check](src/Agentic.Check/README.md) | `dna check` | Not installed; `dna check` runs the latest stable version, as does `dnx agentic.check` | Detects your stack; composes, installs and updates directives, skills and tools |
| [InnoWvate.Agentic](src/Agentic/README.md) | `dna <command>` | Once per repository, pinned in the repo root's `.config/dotnet-tools.json` | Deterministic logic that directives and skills delegate to, as commands for agents and humans; today prompt logging, plus `check` for the latest Agentic.Check |
| [InnoWvate.Dna](src/Dna/README.md) | `dna` | Global, once per machine | Shorthand for `agentic.check`and the repo-local `dotnet agentic` |

## What's included

Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com), [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno).

In the coming months I will be adding content for the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno) as I progress applying agentic engineering to a real-world distributed, cross-platform application that I am building with this technology stack.

## Directives catalog

This repo offers the following directives (markdown snippets to include in your AGENTS.md / CLAUDE.md):

| Directive | Description |
|----------|-------------|
| [`foundation-documentation-sources`](./directives/foundation-documentation-sources.md) | Prioritizes first-party vendor MCPs for external documentation, e.g. using Microsoft Learn before Context7 for Microsoft technologies. |
| [`foundation-prompt-log`](./directives/foundation-prompt-log.md) | Records sanitized user prompts and agent question-and-answer pairs in code commit messages so intent is preserved and can be replayed later. Uses the `dotnet agentic` tool. |
| [`dotnet-cli-run`](./directives/dotnet-cli-run.md) | Prevents long agent timeout delays from running `dotnet` without network access. For Codex, `dna check` also offers rules that run `dotnet` outside the sandbox without approval prompts. |
| [`dotnet-build-errors-and-warnings`](./directives/dotnet-build-errors-and-warnings.md) | Configures .NET build warnings and errors and a modern C# .editorconfig, then directs agents to fix build errors and warnings or document rare justified suppressions. |
| [`uno-build-and-run`](./directives/uno-build-and-run.md) | Standardizes Uno app launch for agents by skipping redundant pre-builds, writing per-run stdout logs via `AGENT_CONSOLE_LOG`, and verifying or stopping the app with the Uno runtime tools. |

## Skills catalog

To complement the skills from the other GitHub repo's (listed above), this repo offers below skills. They are grouped in plugins for ease of [manual installation](docs/manual-install.md), however `dna check` selects or excludes each skill individually.

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
