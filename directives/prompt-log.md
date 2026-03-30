# Prompt Log Directive

Keep the **most important part of your source** - your prompts and your answers to the question that agents ask you - in git, together, but separate from, the source commit that contains the source that was created from your input.

This preserves **intent** in source control, using the same unit of work as your branching strategy, and makes it possible to **replay** your input at a later stage with better models, harnesses and tools.

Copy below markdown in your `AGENTS.MD`

```md
## Prompt Log

Prompt logs are stored as git commits.

### Prompt Log Habit (MANDATORY after each code commit)

After each code commit, immediately add a Prompt log commit with `--allow-empty`. Only include user prompts (skip continuation prompts) and agent question + user answers since the previous Prompt log commit (or session start if none yet). If there is no meaningful Prompt to log, do not add a Prompt log commit.

Commit format:

```text
prompt-log:

1. "sanitized user prompt"
2. "sanitized next user prompt"
3. Q: "sanitized agent question" → A: "sanitized user answer"
```

Rules:

- Only include prompts/Q&A since the previous session log commit.
- Sanitized means redact secrets, tokens, credentials, private URLs, and personal data. Keep all other text VERBATIM — do NOT summarize.

### Retrieving Prompt Logs

```bash
# all session logs
git log --grep="^prompt-log:" --format="medium"

# filtered by date range
git log --grep="^prompt-log:" --since="2026-01-26" --until="2026-02-07" --format="medium"

# oneline overview
git log --grep="^prompt-log:" --oneline
```
```
