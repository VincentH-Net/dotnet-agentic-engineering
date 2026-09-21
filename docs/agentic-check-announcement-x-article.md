# .NET Agentic Engineering in One Command

If you use coding agents on real .NET repos, the hard part is not finding one more skill. It is composing the right set of directives and skills for your tech stack and patterns, keeping that set current, and not burying the agent in instructions that contradict each other. That is the work I kept redoing on my own repos, so I moved it into a tool.

`agentic.check` scans a folder, detects the technologies in use, and installs directives and skills for them, tested and selected from best-in-class GitHub skills repos. Since 2.3 it also installs the tooling those directives and skills need, and you run everything through `dna`, a global shorthand you install once per machine. Follow the steps below - or just watch me do that in this video:

[Insert video: install dna, create the Api repo, run dna check, prompt the agent, show dna prompt-log.]

Repo: [github.com/VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

NuGet: [nuget.org/packages/Agentic.Check](https://www.nuget.org/packages/Agentic.Check)

## Add Agentic Engineering to a Repo

First make sure you have the .NET 10 SDK or later and the GitHub CLI. Skills are installed with `gh skill`, which uses GitHub's API quota. Anonymous access is enough for a handful of skills; `gh auth login` gives you a much higher quota, and the tool tells you when you need it.

Install the `dna` shorthand once per machine, then create a repo with a .NET project and run the check:

```bash
dotnet tool install --global InnoWvate.Dna
mkdir Api && cd Api && git init && dotnet new gitignore
dotnet new webapi --use-minimal-apis
git add . && git commit -m "create new api from template"
dna check
```

`dna check` runs the latest `agentic.check`, which detects a .NET web API and lists what fits it:

- **Directives**: short, tested instructions in `AGENTS.md` / `CLAUDE.md`, such as how to run `dotnet` without agent timeouts, how to fix build warnings, which documentation source to consult first, and the prompt log.
- **Skills**, installed with `gh skill` from tested sources: [dotnet/skills](https://github.com/dotnet/skills), [unoplatform/studio](https://github.com/unoplatform/studio), [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills) and [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering).
- **Tools** that directives and skills depend on, installed with `dotnet tool` and pinned in the target folder.

Directives and skills compose like your stack: Orleans adds to .NET, which adds to Foundation. `agentic.check` currently detects .NET, ASP.NET Core, Microsoft Orleans and Uno Platform. For Uno Platform it also detects MVVM or MVUX, XAML or C# Markup, and the design system. You select what to apply. The goal is to give the agent exactly what this repo needs, and nothing that conflicts with it.

[Insert screenshot: recommendation list for the Api repo with directives, skills, and the InnoWvate.Agentic and dna tool actions.]

Selecting the Prompt Log directive also selects the tool it uses: `InnoWvate.Agentic`, the `dotnet agentic` tool that the directive calls, pinned in the target folder just like the directive, so specialized folders can have their own version. `dna` is the shorthand for it: `dna <args>` runs that folder's `dotnet agentic <args>`, and `dna check` works before it exists. If you cannot install a global tool, or another `dna` is on your PATH, `dnx agentic.check` does the same as `dna check`.

Now start your agent. If it was already running, start it again so the new instructions are active.

## Preserve Intent with the Prompt Log

The prompts you write, and the answers you give to the questions an agent asks, are the most important part of the source in agentic engineering. Normally they never make it into source control. The Prompt Log directive makes agents record them in the commit message of every code commit they create.

Try this prompt and answer the agent when it asks you:

```plaintext
ask me which one of the forecasts to remove,
then remove that and commit the change
```

The agent makes one commit. Its message has the subject for the code change, followed by a prompt-log block with what you told the agent. If you followed my earlier post: there is no separate prompt-log commit anymore, the log lives in the code commit itself. To see the log:

```bash
dna prompt-log
```

[Insert screenshot: dna prompt-log output for the Api repo showing the commit hash, date and the logged prompt with the question and answer.]

The prompt log transcends sessions, agents and harnesses, and it survives any merge that keeps your commits. It preserves intent, together with the code produced from it, for the humans and agents that maintain the code later. And it lets you regenerate an implementation when better models and tools become available.

## Why a Tool Next to Directives and Skills

Directives and skills are text. An agent executes text imperfectly, and pays for it in tokens on every run. Escaping and framing the prompt log is deterministic work, so the directive delegates it to `dotnet agentic`: the agent collects and sanitizes the log, the tool frames it so Git cannot corrupt it.

That is the direction: deterministic steps move out of directive and skill text into code that runs the same way every time. Prompt logging is the first one. More will follow, so directives and skills get shorter and more reliable.

## Keep Current with One Command

Skills and directives change, and the tool they depend on changes with them. Instead of tracking that yourself:

```bash
dna check
```

This runs the latest `agentic.check` and updates directives, skills and the local tool together, so they stay compatible. Run it in any folder you want to set up, when you add a technology, and now and then to pick up skill updates. Teammates who clone the repo get the same pinned tool version with `dotnet tool restore`.

## Specialize Folders

Many repos are not one uniform codebase. Run `dna check` in the repo root for the common set, then in `backend` or `frontend` for what belongs there. It deselects what is already installed above or below, so shared guidance stays at the root and specialized guidance where it applies. Start your agent in the folder whose instructions you want.

Check out the .NET Agentic Engineering repo for more. NJoy!

Repo: [github.com/VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

NuGet: [nuget.org/packages/Agentic.Check](https://www.nuget.org/packages/Agentic.Check)

#AgenticEngineering #AgenticAI #SoftwareEngineering #dotnet
