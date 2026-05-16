---
name: ensure-directives
description: Create or update AGENTS.md and CLAUDE.md with agentic engineering directives for your tech stack. Use before Claude Code or Codex app authoring/running, or when AGENTS.md / CLAUDE.md may be missing or stale.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0.2"
  category: agent-directives
  sources:
    - https://github.com/VincentH-Net/dotnet-agentic-engineering
---

# Ensure Directives

Use this skill to prepare a working folder's CLAUDE.md and AGENTS.md. It ensures that CLAUDE.md refers to AGENTS.md, detects which directives from the public `dotnet-agentic-engineering` repository are relevant to the tech stack in the working folder, and inserts or refreshes them in `AGENTS.md`. The directives have stable markers so they can be updated without disrupting user content. You can also put skip markers in AGENTS.md to exclude specific directives if you do not want them.

Run this skill from the target repository root. If the current working folder is a subfolder, first identify the repository root and use that as the working folder for all detection and file edits.

## Scope

Allowed:

- Fetch directive content from the public `dotnet-agentic-engineering` GitHub repository.
- Create or update `AGENTS.md` / `AGENTS.MD`.
- Create or update `CLAUDE.md` / `CLAUDE.MD`.

Not allowed:

- Continue if the public GitHub docs cannot be read.
- Create the working folder for the user.
- Modify any files other than `AGENTS.md` / `AGENTS.MD` and `CLAUDE.md` / `CLAUDE.MD`.

## Procedure

Start from the public directives folder listing via the GitHub Contents API:
`curl -s 'https://api.github.com/repos/VincentH-Net/dotnet-agentic-engineering/contents/directives?ref=main'`

1. From that folder listing, collect the `download_url` for each directive `.md` file in that folder.
2. Detect project files recursively under the current working folder, excluding `.git`, `.vs`, `bin`, `obj`, and `node_modules`.
3. If no `.csproj` file is found, REMOVE the directives from the list whose filename starts with "dotnet-"
4. If no `.csproj`, `.props`, or `.targets` file contains `Uno.Sdk`, REMOVE the directives from the list whose filename starts with "uno-"
5. Now fetch the content of each remaining directive `.md` file from its `download_url`, extract the fenced Markdown block that contains the directive content to be inserted in `AGENTS.md`, and validate that the extracted directive block is delimited by stable markers included in the fenced markdown block. The marker format is `<!-- dotnet-agentic-engineering:<directive-name>:start -->` and `<!-- dotnet-agentic-engineering:<directive-name>:end -->`
6. If the public directives folder listing or any of the remaining directives cannot be fetched or any of the extracted directive blocks are not enclosed by the expected markers, stop and tell the user which URL failed. Do not fall back to bundled copies or memory.
7. Find existing agent files in the current working folder:
   - Prefer an existing `AGENTS.md` or `AGENTS.MD`; otherwise create `AGENTS.md`.
   - Prefer an existing `CLAUDE.md` or `CLAUDE.MD`; otherwise create `CLAUDE.md`.
8. Ensure the CLAUDE file has exactly one import line for the selected AGENTS file, for example `@AGENTS.md`. If the selected AGENTS file uses different casing, adapt the import line to that exact filename. If a matching AGENTS import already exists, update it if needed instead of adding a duplicate.
9. For each extracted directive block: IF AGENTS does NOT contain `<!-- dotnet-agentic-engineering:<directive-name>:skip -->`, insert or refresh the block in AGENTS using the stable markers:
   - IF exactly one block with the same directive name already exists, replace it with the newly fetched block.
   - IF no block with the same directive name exists, append the new block to the end of the file.
   - IF multiple blocks with the same directive name exist, or only one of the start/end markers exists, stop and tell the user which directive marker is inconsistent.
10. Preserve any user-authored content outside managed marker blocks.
11. Report which directives were skipped / added / updated.

## Validation

After editing:

- Confirm the AGENTS file exists.
- Confirm it contains either a managed block or a skip marker for each extracted directive.
- Confirm the Claude file exists.
- Confirm the Claude file imports the selected AGENTS file.
- Tell the user to restart the agent harness from the working folder so the updated startup instructions are loaded.
