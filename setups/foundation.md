# Foundational engineering setup for any target technology

## Model

The best models are the latest Opus and Codex, with effort High and biggest context available (validated through Opus 4.6 1M and Codex 5.4 High).

Opus is best for:

- UI Design. Less mess, looks better.
- Functional design; "getting" your core intent from functional prompts and creating plans for that

Codex is best for:

- Logic - both writing and codebase analysis
- Edge cases - misses a lot less that Opus, up to the point of overengineering

### Other models & compute

The frontier models are barely good enough for sustainable agentic engineering, and they still need a lot of tools and human expertise and assist to deliver that. This is hard enough to do well as it is - don't bother wasting time with lesser models - yet.

At some point the frontier model labs are going to stop heavily subsidizing tokens, and depending on your budget that is the moment to evaluate the state of OSS models and running those on your own hardware - both are improving rapidly, but still lag behind frontier model labs.

## Harness

The harness choice has just as much impact on the quality of the output as the model choice. For best results, use the CLI harness of the model maker (Claude Code CLI / Codex CLI). The brand owned CLI remains the 1st class harness for each company; it receives earliest updates and fixes, has the most features, and is tested most heavily (because agents can use and test CLI's much better than apps).

Cost is also a factor that favors the brand-owned harnesses: they benefit from the heaviest token-subsidizing subscriptions. API tokens used by 3rd party harnesses are much more expensive.

[Claude Code Get Started](https://code.claude.com/docs/en/overview#get-started)
[Codex CLI Setup](https://developers.openai.com/codex/cli#cli-setup)

## OS

Mac, linux or WSL on Windows works best - the harnesses 1st class shell is bash, as is the bulk of the model's training data. Even if a harness understands another shell well, such as Claude understanding PowerShell, it makes a lot more mistakes using that shell on Windows due to OS-specific differences.

## IDE

You mostly need a UI that has good UX with git on local changes, commits and branches. Git worktree support is useful for parallel agent workstreams.

Reading and navigating code quickly and easily is useful to understand / validate the implementation architecture.

Actual manual editing and debugging is more exception than rule in agentic engineering, but you do need it when the agents don't cut it.

[VS Code](https://code.visualstudio.com/) and it's plugin ecosystem works well for all of the above, for many technologies including markdown, web, dotnet, C#, PowerShell and bash. VS Code keeps pace with the latest models and agentic engineering practices (weekly release cycle).

## CLAUDE.ME and AGENTS.MD

Use below content in `CLAUDE.MD`, in the same folder as `AGENTS.MD`, to have Claude Code, Codex and other agents all use `AGENTS.MD` for directives:

```text
# CLAUDE.md
@AGENTS.md

This file provides any instructions specific to Claude Code (claude.ai/code), when working with code in this repository. These instructions are supplemental to the generic agents instructions (imported above) 
```
