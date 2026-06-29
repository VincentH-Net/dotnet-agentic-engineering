# Manually installing directives and skills

The `agentic-check` tool can do this automatically for you, but you can also do it manually. Note that without `agentic-check`, you will have to manually compose the right set of directives and skills for your repo; simply installing everything wastes context and may cause agent mistakes.

## Directives install

Copy the markdown snippets from [directives](/directives/) that you select into your AGENTS.md / CLAUDE.md.

## Skills Install

Recommended is to use `gh skill` ([install](https://cli.github.com/)) to select and install skills from this repo as well as the other repo's that `agentic-check` installs skills from. You can then update all skills later with a single `gh skill` update command.

Alternatively, you can install the skills as plugins in codex or claude code; follow below steps.

Install plugins in Claude Code:

```bash
claude plugin marketplace add VincentH-Net/dotnet-agentic-engineering
claude plugin install dotnet@dotnet-agentic-engineering
claude plugin install orleans@dotnet-agentic-engineering
claude plugin install uno-platform@dotnet-agentic-engineering
```
To update a plugin, use `update` instead of `install`

Install plugins in Codex:

```bash
codex plugin marketplace add VincentH-Net/dotnet-agentic-engineering
```

Then enable the plugins you want with the `/plugins` command in codex (you can filter on "dotnet-agentic-engineering" in the plugins list).

To update plugins in Codex:
```bash
codex plugin marketplace upgrade dotnet-agentic-engineering
```


```bash
# Select skills in this repo and install them in agents you choose
npx skills add VincentH-Net/dotnet-agentic-engineering
```
