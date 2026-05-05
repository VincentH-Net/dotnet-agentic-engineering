---
name: uno-doctor-directives
description: Ensure a Uno Platform repository has the latest version of all required agent startup directives installed in AGENTS.md and CLAUDE.md by starting from the public dotnet-agentic-engineering setups/ui-uno-platform.md document, following its direct and indirect repo-local setup/directive links, and applying only the discovered CLAUDE.md / AGENTS.md creation or edit instructions. Use before Claude Code or Codex app authoring/running, or when AGENTS.md / CLAUDE.md may be missing or stale. Does not install tools, plugins, skills, MCPs, models, or templates.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0"
  framework: uno-platform
  category: agent-directives
  sources:
    - https://github.com/VincentH-Net/dotnet-agentic-engineering
---

# Ensure Uno Agent Directives

Use this skill to prepare a Uno Platform repository's agent instruction files. This skill intentionally does **not** duplicate setup instructions or dependency lists. It starts from the public GitHub UI setup document, follows repo-local setup/directive links, then applies only the discovered parts that create or edit `AGENTS.md` and `CLAUDE.md`.

## Scope

Allowed:

- Create or update `AGENTS.md` / `AGENTS.MD`.
- Create or update `CLAUDE.md` / `CLAUDE.MD`.
- Insert or refresh directive content discovered from public `directives/*.md` files linked directly or indirectly from the root setup doc.
- Report non-file-edit setup items as links to the fetched setup docs that contain them.

Not allowed:

- Install Claude Code, Codex, MCP servers, plugins, skills, templates, or npm/dotnet tools.
- Create the working folder for the user.
- Modify project source files.
- Continue if the public GitHub docs cannot be read.

## Required Public Docs

Start from this public setup doc at execution time:

- `https://raw.githubusercontent.com/VincentH-Net/dotnet-agentic-engineering/main/setups/ui-uno-platform.md`

From that document, recursively follow only repo-local Markdown links under `setups/` and `directives/`. Fetch each linked public raw file from `https://raw.githubusercontent.com/VincentH-Net/dotnet-agentic-engineering/main/`.

Only execute instructions that edit `AGENTS.md` or `CLAUDE.md`. Treat external links, plugin installs, MCP installs, model choices, IDE guidance, and tool installation as prerequisite links to report.

If the root setup doc or any discovered repo-local `setups/` or `directives/` doc cannot be fetched, stop and tell the user which URL failed. Do not fall back to bundled copies or memory.

## Procedure

1. Fetch the root UI setup doc and discovered repo-local setup/directive docs from public GitHub.
2. From any fetched setup doc that gives `CLAUDE.md` content, extract the fenced Markdown block that contains the `CLAUDE.md` contents.
3. From every fetched `directives/*.md` file that says to copy Markdown into `AGENTS.md`, extract the fenced Markdown block containing that directive.
4. Find existing agent files in the current working folder:
   - For AGENTS, prefer an existing `AGENTS.md` or `AGENTS.MD`; otherwise create `AGENTS.md`.
   - For Claude, prefer an existing `CLAUDE.md` or `CLAUDE.MD`; otherwise create `CLAUDE.md`.
5. Ensure the Claude file imports the selected AGENTS file. If the discovered public Claude-file instruction imports a different case than the selected AGENTS filename, adapt the import line to the selected filename.
6. Insert or refresh each extracted directive block in AGENTS using stable markers based on the directive filename without extension:

```md
<!-- dotnet-agentic-engineering:<directive-name>:start -->
...content extracted from public directives/<directive-name>.md...
<!-- dotnet-agentic-engineering:<directive-name>:end -->
```

7. Preserve any user-authored content outside managed marker blocks.
8. If unmarked copies of a managed directive already exist, do not delete them automatically. Report that duplicates may need manual cleanup.
9. Report the fetched setup docs that contain prerequisite setup items that were not executed; do not list hardcoded prerequisite docs that were not discovered from the root setup doc.

## Validation

After editing:

- Confirm the AGENTS file exists.
- Confirm it contains managed blocks for every extracted directive.
- Confirm the Claude file exists.
- Confirm the Claude file imports the selected AGENTS file.
- Tell the user to restart Claude Code or Codex from the working folder so the updated startup instructions are loaded.
