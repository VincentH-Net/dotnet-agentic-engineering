# Prompt Log Directive

Keep the **most important part of your source** - your prompts and your answers to the question that agents ask you - in an accompanying git commit to the source commit that contains the source that was created from your input.

This preserves **intent** in source control, using the same unit of work as your branching strategy, and makes it possible to **replay** your input at a later stage with better models, harnesses and tools.

Use the `agentic-check` tool to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-agentic-engineering:foundation-prompt-log:start -->
## Prompt Log

Prompt logs are stored in the git commit messages of the code commits they describe.

### Prompt Log Habit (MANDATORY for each agent-created code commit)

When creating a code commit, include a Prompt log block in that commit's initial commit message.
Report that the prompt log is included when committing, so the user can ask for a repair if needed.
Only include user prompts and agent questions + user answers since the previous Prompt log block 
(or session start if none yet). Omit continuation prompts. If the last prompt only asks to commit or
push the current work, omit that last prompt from the Prompt log. If there is no meaningful Prompt to
log, do not include a Prompt log block.

Commit format:

```text
original code commit subject

original code commit body

prompt-log:
"sanitized user prompt"

"sanitized next user prompt"

"Q: sanitized agent question -> A: sanitized user answer"
prompt-log-end:
```

Rules:

- Only include prompts/Q&A since the previous Prompt log block.
- Sanitized means redact secrets, tokens, credentials, private URLs, and personal data. 
  Keep all other text in user prompts, agent questions, and user answers VERBATIM - do NOT summarize. Encode each prompt or Q&A entry with the encoding script below for safety.
- For every user message since the previous Prompt log block:
  - If the user message answers an agent question, log it as:
    `"Q: sanitized agent question -> A: sanitized user answer"`
- Pre-commit self-check:
  - No prompt-log entry may be only a terse answer like `7`, `yes`, or `no` unless
    it was not answering an agent question.
  - If a user prompt is short or ambiguous, inspect the immediately preceding agent
    message and convert it to a Q&A entry when applicable.
- Preserve the original commit message verbatim before the Prompt log block.
- Keep `prompt-log-end:` immediately after the Prompt log entries, so tools can distinguish Prompt logs from later commit-message metadata such as Git trailers.
- The lines between `prompt-log:` and `prompt-log-end:` MUST be JSON string lines produced by the encoding script below, with blank separator lines between entries. Do not write raw prompt text directly in the commit message body and do not hand-escape JSON.

### Encoding Prompt Log Entries

Use this helper for every prompt or Q&A entry. It reads exactly one sanitized entry from standard input, writes each physical line as one JSON string line, and then writes one blank separator line.

```bash
prompt_log_entry() {
  perl -MJSON::PP=encode_json -0777 -we '
    my $text = do { local $/; <STDIN> };
    print encode_json($_), "\n" for split /\n/, $text, -1;
    print "\n";
  '
}

# examples
printf '%s' 'sanitized user prompt' | prompt_log_entry
printf '%s' 'Q: sanitized agent question -> A: sanitized user answer' | prompt_log_entry
```

### Retrieving Prompt Logs

When the user asks to show prompt logs, display the decoded prompt-log entries in full.
Do not abbreviate, summarize, replace text with `...`, or omit lines from any entry.
Preserve multiline entries and blank lines so the displayed text matches the decoded
prompt log content.

```bash
prompt_logs() {
  git log --reverse "$@" --format='%x00%H%x00%cI%x00%B' |
    perl -MJSON::PP=decode_json,encode_json -0777 -we '
      my $input = do { local $/; <STDIN> };
      my @fields = split /\0/, $input, -1;
      shift @fields while @fields && $fields[0] eq "";

      while (@fields >= 3) {
        my ($hash, $date, $body) = splice @fields, 0, 3;
        $hash =~ s/\A\n+//;
        $body =~ s/\n+\z//;

        next unless $hash =~ /\A[0-9a-f]{40}\z/;
        next unless $body =~ /^prompt-log:\n(.*?)^prompt-log-end:\n?/ms;

        print "$hash $date\n";
        my @entry_lines;
        for my $line (split /\n/, $1, -1) {
          if ($line =~ /\A[ \t]*\z/) {
            if (@entry_lines) {
              print encode_json(join "\n", @entry_lines), "\n";
              @entry_lines = ();
            }

            next;
          }

          my $text = decode_json($line);
          die "prompt-log entry line must be a JSON string\n" if ref $text;

          push @entry_lines, $text;
        }

        if (@entry_lines) {
          print encode_json(join "\n", @entry_lines), "\n";
        }

        print "\n";
      }
    '
}

# all prompt logs, showing only prompt log content
prompt_logs

# prompt logs since a date, showing only prompt log content
prompt_logs --since="2026-01-26"

# prompt logs in a date range, showing only prompt log content
prompt_logs --since="2026-01-26" --until="2026-02-07"
```
<!-- dotnet-agentic-engineering:foundation-prompt-log:end -->
~~~
