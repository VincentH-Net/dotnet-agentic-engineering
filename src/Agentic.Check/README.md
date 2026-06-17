# Agentic.Check

`agentic-check` checks a local repository for recommended agentic engineering directives and skills.

It can create or update `AGENTS.md` with stack-specific directives, ensure `CLAUDE.md` imports `AGENTS.md`, and install missing repo-local skills with `gh skill`.

Recommended directive updates and missing skills are shown in one selection list. The directive section appears first, followed by skills. All recommended items start selected; `<right>` selects every directive and skill, and `<left>` clears every directive and skill.

Use `--yes` to apply all recommended directive updates and missing skills without prompting. Use `--dry-run` to report intended directive and skill actions without writing files or running installs.
