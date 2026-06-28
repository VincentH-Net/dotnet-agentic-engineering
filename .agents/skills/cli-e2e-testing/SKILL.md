---
name: cli-e2e-testing
description: Use when creating, modifying, debugging, or reviewing end-to-end tests for any CLI tool that must run in a real terminal, accept keyboard input, and expose observable terminal output. Prefer Hex1b terminal automation for PTY/headless terminal flows.
---

# CLI End-to-End Testing

Use this skill when a CLI test must exercise the real executable through a terminal-like environment instead of calling internal APIs or redirecting standard input/output.

## Fit

Hex1b is a good fit when tests need any of these:

- A real PTY-backed child process such as `bash`, `zsh`, `pwsh`, or the CLI itself.
- Terminal screen inspection after ANSI output, cursor movement, tables, progress bars, or interactive prompts.
- Keyboard input such as arrows, Enter, Escape, Ctrl+C, text typing, or selection-list navigation.
- Headless CI execution without opening a real terminal window.
- Debuggable failures with a terminal snapshot and step history.

Avoid Hex1b for simple non-interactive commands where `ProcessStartInfo` with redirected output is enough.

## Test Architecture

1. Create an isolated temporary workspace.
2. Create deterministic test fixtures and fake external tools/services as needed.
3. Start a `Hex1bTerminal` with:
   - `WithHeadless()`
   - fixed dimensions large enough for the expected output
   - `WithPtyProcess(...)` for the shell or CLI process under test
4. Wrap it in `Hex1bTerminalAutomator`.
5. Type commands, send keys, and wait for specific output.
6. Inspect terminal snapshots and side effects.
7. Stop the shell/process in `finally`.

## Basic Pattern

```csharp
using Hex1b;
using Hex1b.Automation;
using Hex1b.Input;

await using var terminal = Hex1bTerminal.CreateBuilder()
    .WithHeadless()
    .WithDimensions(160, 80)
    .WithPtyProcess(options =>
    {
        options.FileName = "/bin/bash";
        options.Arguments = ["--noprofile", "--norc", "-i"];
        options.WorkingDirectory = workspace;
        options.Environment = new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
            ["PATH"] = testBin + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "")
        };
    })
    .Build();

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var runTask = terminal.RunAsync(cts.Token);
var auto = new Hex1bTerminalAutomator(terminal, defaultTimeout: TimeSpan.FromSeconds(30));

try
{
    await auto.TypeAsync("my-cli --dry-run; printf '\\n__DONE:%s__\\n' \"$?\"");
    await auto.EnterAsync();
    await auto.WaitUntilTextAsync("__DONE:0__", timeout: TimeSpan.FromSeconds(60));

    using var snapshot = auto.CreateSnapshot();
    string screen = snapshot.GetScreenText();
    Assert.Contains("Expected output", screen, StringComparison.Ordinal);
}
finally
{
    await auto.TypeAsync("exit");
    await auto.EnterAsync();
    await runTask.WaitAsync(TimeSpan.FromSeconds(5));
}
```

## Waiting

Prefer deterministic waits over sleeps:

```csharp
await auto.WaitUntilTextAsync("Select an option");
await auto.WaitUntilAsync(
    snapshot => snapshot.ContainsText("Ready") && !snapshot.ContainsText("Loading"),
    timeout: TimeSpan.FromSeconds(30),
    description: "CLI to finish loading");
```

Use a unique command-completion sentinel when driving a shell:

```bash
my-cli args; printf '\n__DONE:%s__\n' "$?"
```

Wait for `__DONE:0__` for success. If testing failures, wait for the expected non-zero sentinel.

## Keyboard Input

Wait for the prompt/list before sending input:

```csharp
await auto.WaitUntilTextAsync("select which to apply:");
await auto.LeftAsync();
await auto.WaitUntilTextAsync("[ ] first-item");
await auto.EnterAsync();
```

Useful automator methods:

- `TypeAsync("text")`
- `EnterAsync()`
- `UpAsync()`, `DownAsync()`, `LeftAsync()`, `RightAsync()`
- `SpaceAsync()`
- `EscapeAsync()`
- `Ctrl().KeyAsync(Hex1bKey.C)`

After keyboard input that changes the UI, wait for a visible result before continuing.

## External Dependencies

Keep e2e tests deterministic:

- Stub external CLIs by putting scripts in a temporary `bin` directory and prepending it to `PATH`.
- Log stub invocations to a temp file when assertions need to prove what was called.
- Avoid network calls unless the test category explicitly says it is live.
- Use per-test temp directories for HOME, config, cache, and package locations when the CLI reads global state.
- Prefer real local files and real process execution over mocks for the CLI itself.

## Assertions

Assert both terminal output and side effects:

- Output: summary lines, prompts, selected item state, error messages, completion sentinel.
- Side effects: files created/updated, command log contents, exit code sentinel.
- Negative checks: no install/update command was invoked, no directive block was written, no unexpected prompt appeared.

Use `GetScreenText()` for visible terminal contents. If important output can scroll out of view, use larger fixed dimensions, add a sentinel near the end, or add logging/recording.

## Failure Diagnostics

Hex1b automation failures include step history and terminal snapshots. Make wait descriptions specific enough to identify the failed phase.

When diagnosing a failing test:

1. Check the failed wait description.
2. Inspect the terminal snapshot from the exception.
3. Verify the wait text is literal and not accidentally matching echoed command text.
4. Verify the CLI process inherited the intended `PATH`, HOME/config variables, and working directory.
5. Confirm the test waited for the UI to rerender after each key input.

## Common Pitfalls

- Matching command text echoed by the shell instead of output produced after execution.
- Pressing Enter immediately after a key input without waiting for the terminal UI to update.
- Relying on arbitrary delays instead of `WaitUntilTextAsync` or `WaitUntilAsync`.
- Letting tests use the developer machine's real CLI extensions, global config, or network credentials.
- Using a terminal height too small for the output under test.
- Asserting a file was not created when the CLI intentionally creates scaffolding even with no selected actions; assert the meaningful content instead.
