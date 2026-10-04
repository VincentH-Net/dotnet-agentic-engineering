# Agentic Development Environment Setup

Recommendations for setting up your agentic engineering dev environment

## 1 Foundational Engineering Setup

For any target technology.

### Model

The best models at the time of writing (Oct 4, 2026) are Fable and Astra, with effort Extra High (validated through Fable 5.1 and GPT 6 Astra).

Fable is best all around:

- UI Design. Less mess, looks better.
- Functional design; "getting" your core intent from functional prompts and creating plans for that

- Logic - both writing and codebase analysis
 
Astra is comparable for:

- Logic - both writing and codebase analysis

#### Other models & compute

The frontier models are good enough for sustainable agentic engineering **most of the time**, and they still need a lot of tools and human expertise and assist to ensure reliable and sustainable engineering. This is hard enough to do well as it is; don't bother wasting time with lesser models - yet.

At some point the frontier model labs are going to stop heavily subsidizing tokens, and depending on your budget that is the moment to evaluate the state of OSS models and running those on your own hardware - both are improving rapidly, but still lag behind frontier model labs. However, the continuing price increase of inference capable hardware keeps postponing the break-even point (ram prices went to 400% over the past year).

### Harness

The harness choice has just as much impact on the quality of the output as the model choice. For best results, use the native app harness of the model maker (Claude App / ChatGPT App). This used to be the CLI, but since the race to gain computer work market share has led to investments prioritizing the desktop app, the CLI became 2nd class citizen. Also the architecture of both apps has improved to the point where for code the harness behaves identical - there is no context pollution from the non-code features that the apps offer. The benefit of the apps are the additional productivity features for software engineering work:

- Best dictation: faster than typing and works equally well on devices without keyboard
- Best remote support: controlling sessions on your dev machine from mobile works most reliably when they are started in the desktop app

- Best diff / changes view: I used to use VS code for diffs, but the built-in views in the apps are now even better

Cost is also a factor that favors the brand-owned harnesses: they benefit from the heaviest token-subsidizing subscriptions. API tokens used by 3rd party harnesses are much more expensive.

See:

[Claude Get Started](https://code.claude.com/docs/en/overview#desktop-app)

[Codex Get Started](https://chatgpt.com/features/codex-get-started/)

#### Automatic approvals

Automatic approvals is probably the single most important way to increase your productivity. By now this is the default in the apps - check yours are on `auto`.

### OS

Mac, linux or WSL on Windows works best - the harnesses 1st class shell is bash, as is the bulk of the model's training data. Even if a harness understands another shell well, such as Claude understanding PowerShell, it makes a lot more mistakes using that shell on Windows due to OS-specific differences.

### IDE

IDE's are still useful for UI that has good UX with git on local changes, commits, branches and worktrees. Harness apps are getting there but they are not as complete and user friendly as mature IDE's yet.

Occasionally an IDE is useful for reading and navigating code quickly and to understand / validate the implementation architecture. Agents are becoming better at visualizing and explaining implementation and architecture, but IDE's are still better for navigating the code.

Actual manual editing and debugging is more exception than rule in agentic engineering, but you do need it when the agents don't cut it.

[VS Code](https://code.visualstudio.com/) and it's plugin ecosystem works well for all of the above, for many technologies including markdown, web, dotnet, C#, PowerShell and bash. VS Code keeps pace with the latest models and agentic engineering practices (weekly release cycle).

### GitHub

[Install](https://cli.github.com/) the `gh` CLI - `dnx agentic.check` requires it for it's `skill` command.

## 2 .NET Engineering Setup

Prerequisites:

- [x] [Foundation Setup](#1-foundational-engineering-setup)

Recommended:

- [x] [Microsoft Learn MCP](https://learn.microsoft.com/en-us/training/support/mcp-get-started) for agent guidance on all Microsoft technologies
- [x] [Context7 MCP](https://github.com/upstash/context7#installation) for up to date agent guidance on GitHub OSS libraries

## 3 Cross-platform UI with Uno Platform Engineering Setup

Prerequisites:

- [x] [Foundation Setup](#1-foundational-engineering-setup)
 Fable can do both UI and logic well with Uno platform; Astra can do the UI logic but is not so good at design, no matter how much tools and guidance you provide (validated through Fable 5.1 and GPT 6 Astra).
- [x] [.NET Setup](#2-net-engineering-setup)

Required:

- [x] Uno Platform and it's MCP's
  - [x] [Uno Platform Get started with Claude Code](https://platform.uno/docs/articles/get-started-ai-claude.html?tabs=macos)
  - [x] [Uno Platform Get started with Codex](https://platform.uno/docs/articles/get-started-ai-codex.html?tabs=macos)

### Which model for what

- For anything involving UI markup: use latest Claude App with Fable; no matter what you tell & give Codex with Astra, it often makes a visual mess when creating UX. Validated on Extra High effort with Fable 5.1 / GPT 6 Astra and older.
- If Claude gets stuck in complex **UI logic** issues (e.g. how to use a complex UI library like LiveCharts2), latest Astra can get you unstuck.
