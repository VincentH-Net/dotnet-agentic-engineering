# Setup for engineering cross-platform UI with Uno Platform

Prerequisites:

- [Foundation Setup](./foundation.md)
  You need both Opus and Codex for UI with Uno platform - neither alone is capable enough yet, no matter how much tools and guidance you provide (validated through Opus 4.6 1M and Codex 5.4 High).
- [.NET Setup](./dotnet.md)

Additionally, install:

- Uno Platform and it's MCP's
  - [Uno Platform Get started with Claude Code](https://platform.uno/docs/articles/get-started-ai-claude.html?tabs=macos)
  - [Uno Platform Get started with Codex](https://platform.uno/docs/articles/get-started-ai-codex.html?tabs=macos)
- [Uno Platform Skills](https://github.com/mtmattei/UnoPlatformSkills/tree/main#installation) by Uno's [Matthew Mattei](https://github.com/mtmattei)
- [.NET Agentic Engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering#installation) by [VincentH-Net](https://github.com/VincentH-Net)
  - Uno Platform Claude Code plugin
  - Uno Platform Codex skills
- [Uno Plaform Build and Run Directive](../directives/uno-build-and-run.md)

## Which model for what

- For anything involving UI markup: use latest Claude Code CLI with Opus 1M High effort; no matter what you tell & give Codex, it often makes a visual mess when creating UX. Validated on Codex 5.4 High effort and older.
- If Claude gets stuck in complex **UI logic** issues (e.g. how to use a complex UI library like LiveCharts2), latest Codex CLI with 5.4 High can get you unstuck.
