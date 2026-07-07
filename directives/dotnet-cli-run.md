# .NET CLI Run Directive

This lets you prevent long agent timeout delays from running `dotnet` in a background terminal with insufficient access, where the agent does not detect the failure.

Use the `agentic-check` tool to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-agentic-engineering:dotnet-cli-run:start -->
## Running `dotnet ...`

All `dotnet ...` commands require network access. Always run `dotnet` commands with escalated/network-enabled permissions from the start; do not first try them in the restricted sandbox. Include a concise approval reason such as: “Allow network access for dotnet commands, they need it to function.”
<!-- dotnet-agentic-engineering:dotnet-cli-run:end -->
~~~
