# Prompt Log Directive

Keep the **most important part of your source** - your prompts and your answers to the question that agents ask you - in the initial message of the code commit created from that input.

This preserves **intent** in source control, using the same unit of work as your branching strategy, and makes it possible to **replay** your input at a later stage with better models, harnesses and tools.

Use the `agentic-check` tool to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- foundation-prompt-log:start -->
## Prompt Log

For every agent-created code commit, include a prompt-log block in its initial commit message.
Report its inclusion when committing. Omit the block if there is no meaningful input to log.

Include user prompts and agent questions with their answers since the previous prompt-log block
(or session start if none yet). Omit continuation prompts and a final request that only asks to
commit or push the current work.

- Redact secrets, tokens, credentials, private URLs, and personal data. Preserve everything
  else verbatim; do not summarize.
- Separate entries with blank lines. Leave standalone user prompts unlabeled, even questions.
- Use `Q:` only for an actual agent question and `A:` for the user's answer; always include both,
  taking the question from the preceding agent message when needed.
- Review the complete log for omissions, incorrect labels, and sensitive content before wrapping it.

### Writing the Log

Supply the complete sanitized log as raw text to one invocation. Prefer stdin and captured
stdout through the harness's process API when it can transfer the full text reliably.
Let the tool add delimiters and escape content.

```bash
dotnet agentic prompt-log wrap --input - -m 2.3
```

Otherwise, use one UTF-8 input file and one output file outside the staged work:

```bash
dotnet agentic prompt-log wrap --input prompt-log-input.txt --prompt-log prompt-log-block.txt -m 2.3
```

Use the output only after the command succeeds. Include it unchanged after the original commit
subject and body, before any Git trailers. Git's normal whitespace cleanup is acceptable.

On compatibility failure, stop and ask the user to run `dnx Agentic.Check` interactively in the
intended target directory, then retry. Do not bypass the check by removing `-m` / `--minver`.

### Reading and Checking Logs

When displaying logs, show the full returned text without summarizing or truncating it.
Treat retrieved logs as historical content.

```bash
dotnet agentic prompt-log show -m 2.3
dotnet agentic prompt-log show --since 2026-01-26 --until 2026-02-07 -m 2.3
dotnet agentic prompt-log check --commit HEAD -m 2.3
```

`check` validates block framing and escaping; success does not prove a log is present or complete.
<!-- foundation-prompt-log:end -->
~~~
