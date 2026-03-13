# dotnet-agentic-engineering

Battle-tested agentic engineering for [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno)

## What this is

Agentic engineering setups, directives and skills that I use for building real-world .NET applications.

Everything here has been used with real-world codebases - not generated with prompts and uploaded untested.

This repo exists to:
- filter proven engineering value from untested content and marketing in the agentic ecosystem
- fill agentic ecosystem gaps, both technology-independent and for specific technologies
- extend the agentic ecosystem with specialized technology/pattern skills for distributed/cross-platform applications

## What's included
Currently this repo includes technology-independent content and content specific for [.NET](https://dotnet.microsoft.com) and [Uno Platform](https://platform.uno).

### Coming
In the coming months I will be adding content as I progress applying agentic engineering to a real-world distributed, cross-platform application built with the latest [.NET](https://dotnet.microsoft.com), [Aspire](https://aspire.dev), [Azure](https://azure.microsoft.com), [Fabric](https://www.microsoft.com/en-us/microsoft-fabric), [Orleans](https://learn.microsoft.com/en-us/dotnet/orleans) and [Uno Platform](https://platform.uno).

Watch and star this repo if this includes what you are looking for!

## How to use
Install only the directives and skills that are relevant for your use case.

Install a directive:
Paste the content of a `directives/<name>.md` file in your `AGENTS.MD` / `CLAUDE.md`

Install a skill in Codex:
`$skill-installer install https://github.com/VincentH-Net/dotnet-agentic-engineering/tree/main/skills/a-skill`

Install a skill / all skills in any agent with `npx skills` (requires `Node.js`) :
```bash
# List available skills in this repo
npx skills add VincentH-Net/dotnet-agentic-engineering --list

# Install a specific skill to Claude Code
npx skills add VincentH-Net/dotnet-agentic-engineering --skill a-skill -a claude-code

# Install a specific skill to Codex
npx skills add VincentH-Net/dotnet-agentic-engineering --skill a-skill -a codex

# Install a specific skill to all detected agents
npx skills add VincentH-Net/dotnet-agentic-engineering --skill a-skill

# Install all skills from this repo
npx skills add VincentH-Net/dotnet-agentic-engineering --all
```

## Articles
Posts with relevant results, experience and learnings:
- TODO Article link

## Structure

```
dotnet-agentic-engineering/
├── setups/             # Optimized combinations of harness, model, configuration, plugins, MCPs and skill libraries - for specific use cases
├── directives/         # Proven agent instruction snippets for agents.md or claude.md
├── skills/             # Technology-specific knowledge
└── examples/           # Sample projects demonstrating usage
```

## Contributing

A core value of this repo is that it only includes things that I have verified in real-world work,
so the best way to contribute is to register an issue `Consider including X` with a (link to) a directive or skill (library) 
that you have created or found. It's also useful to summarize your experience using it (if any) - e.g. what was good / bad.

Greatly appreciated!

Things that are in scope of this repo are:
- technology-independent agent directives
- distributed .NET Orleans applications
- cross-platform Uno Platform apps
- Aspire, .NET SignalR, 
- Azure (Container Apps, EventHub incl MQTT Broker), Entra ID, Fabric (RTI, Event House, Kusto)

## License

MIT
