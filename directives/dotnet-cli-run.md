# .NET CLI Run Directive

This lets you prevent long agent timeout delays from running `dotnet` in a background terminal with insufficient access, where the agent does not detect the failure.

For Codex, `dna check` also offers **Codex rules**: a `.codex/rules/dna-dotnet.rules` file with allow rules for `dotnet`, `dnx` and `dna` (and the Uno `New-View.ps1` script), so Codex runs them outside its sandbox from the first attempt, without approval prompts. It is a separate item in the selection list; rules you already keep in any `.codex/rules` file count as installed.

Use `dna check` to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-cli-run:start -->
## Running `dotnet ...`

`dotnet ...` commands require network access. Always run `dotnet` commands with escalated/network-enabled permissions from the start; do not first try them in the restricted sandbox. Include a concise approval reason such as: “Allow network access for dotnet commands, they need it to function.”
<!-- dotnet-cli-run:end -->
~~~
