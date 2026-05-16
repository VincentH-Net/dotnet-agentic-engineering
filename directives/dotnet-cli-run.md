# .NET CLI Run Directive

This lets you prevent long agent timeout delays from running `dotnet` in a background terminal with insufficient access, where the agent does not detect the failure.

Use the `ensure-directives` skill to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-agentic-engineering:dotnet-cli-run:start -->
## Running `dotnet ...`

  All `dotnet ...` commands MUST be run directly in the foreground.

  Do NOT run `dotnet ...` commands in:
  - background terminals
  - persistent shell sessions
  - detached jobs
  - long-running interactive sessions

  Reason: background execution can hide immediate sandbox/access failures until a long timeout.

  If sandboxed access blocks the foreground command, rerun the same foreground command with the required escalation.
<!-- dotnet-agentic-engineering:dotnet-cli-run:end -->
~~~
