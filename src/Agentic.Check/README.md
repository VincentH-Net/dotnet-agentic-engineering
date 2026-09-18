# Agentic.Check

The `agentic-check` .NET tool optimizes your repo for agentic engineering with .NET - based technologies.

- Detects which .NET based technologies and features you use
- Recommends an optimal set of agentic directives and skills for those
- You select which to apply
- Directives are installed / updated in AGENTS.md, directly from the
    dotnet-agentic-engineering GitHub repo
- Skills are installed / updated directly from source GitHub skill repo's with 
  `gh skill`

The skills available for composition are carefully tested and are selected from 
best-in-class GitHub skills repo's:
- [dotnet/skills](https://github.com/dotnet/skills)
- [unoplatform/studio](https://github.com/unoplatform/studio)
- [mtmattei/UnoPlatformSkills](https://github.com/mtmattei/UnoPlatformSkills)
- [VincentH-Net/dotnet-agentic-engineering](https://github.com/VincentH-Net/dotnet-agentic-engineering)

The composition minimizes context usage and avoids
contradictions and ambiguities, reducing agent mistakes.

Currently supports foundational agentic habits, [.NET](https://dotnet.microsoft.com/), [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet), [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans) and [Uno Platform](https://platform.uno/)

Uno Platform skills are selected depending on detected:
- MVVM or MVUX update pattern
- Pure XAML markup or XAML combined with either Uno C# Markup or C# Markup 2
- Fluent / Material / Cupertino design system

## Prerequisites

- .NET 10 SDK or later ([install](https://dotnet.microsoft.com/download))
- `gh` CLI ([install](https://cli.github.com/))

GitHub login is optional. When no credential is available, Agentic.Check reads public sources
anonymously without a startup prompt. When you are logged in, it validates and reuses GitHub
CLI’s effective credential for both skill commands and direct GitHub API reads. Automation can
supply `GH_TOKEN` or `GITHUB_TOKEN`; these take precedence over the stored login. Invalid
credentials are reported rather than silently falling back to anonymous access.

Credentials stay in memory and are not saved or logged; downloads to other hosts do not receive
them. Authentication validation needs network access even when source responses are cached.

If GitHub’s anonymous quota is exhausted, Agentic.Check stops and asks you to run `gh auth login`
and rerun, or wait for the quota to reset. No reset timestamp is displayed and no additional
quota lookup or command retry is made. Completed changes are retained and recorded;
the run exits unsuccessfully so you can rerun to finish. Authenticated quota exhaustion, temporary
throttling, permission errors, and invalid credentials have separate guidance.

## Usage

In a target folder: 
```bash
dnx agentic.check -- -h
```

or

```bash
dotnet tool install --global agentic.check
agentic-check -h
```

### Folder Specializing
`agentic-check` supports specializing folders in your repo, e.g. to have common directives and skills in the repo root, but additional and different ones in `backend` and `frontend` subfolders:
1. Start `agentic-check` in the repo root and select the common set of directives and skills to install there
2. Start `agentic-check` in the `backend` subfolder and select the additional specialized set of directives and skills for that subfolder - `agentic-check` will automatically deselect any directives and skills that are already installed above or below the target folder. For `agentic-chech`, above terminates at the repo root or else the drive root.
3. Start `agentic-check` in the `frontend` subfolder and select the specialized set of directives and skills for that subfolder
4. Start your agent in a (sub)folder of choice to use that specialized set of instructions. Multiple harnesses support this, including Codex CLI (composes above) and Claude Code CLI (composes above, as well as below when working on files below it's working dir).

## Local companion prerequisites (2.3.0)

Selecting `foundation-prompt-log` also selects `InnoWvate.Agentic` in the same dependency list.
Deselecting the prerequisite deselects its consumers. Agentic.Check prepares it once per target,
before changing dependent content, using the target's explicit `.config/dotnet-tools.json` and
normal `nuget.config` sources. New nested manifests preserve discovery of unrelated parent tools.
Existing tool entries and `isRoot` settings are retained.

The required major/minor comes from the literal `src/Agentic/Agentic.csproj` version at the same
source revision as the selected content. Stable installs/updates use `major.*`; `--preview` uses
`major.*-*`, allowing stable and prerelease packages. The SDK resolves the newest matching package,
including downgrades when repairing a different major or switching back to stable. Agentic.Check
verifies the exact manifest version and minimum before applying consumers. Failures skip dependent
actions and report any partial manifest change. Dry-run reports a pattern and minimum without
creating manifests, restoring tools, or changing content. JSON reports include `companion` status.

After cloning, `dotnet tool restore` restores the exact recorded version. Repair of an unrestored,
compatible installation uses that version; repair of missing/incompatible prerequisites reads the
installed consumer's literal `-m`/`--minver` requirement. Confirmed stable skill updates enforce the
same prerequisites even when no recommendations were selected. Declining updates does not install
their prerequisites. No global launcher, PATH edits, or activation scripts are needed.

## Optional dna shorthand command

The `dna` shorthand for `dotnet agentic` action is selected by default with the companion.
You can deselect it while keeping the companion; deselecting the companion also deselects
its shorthand. Repos with an existing companion can opt into the shorthand without
reinstalling the companion. It is a separate global launcher, `InnoWvate.Dna`, independently
versioned from 1.0.0. Its selected action installs the latest stable package, or updates
an existing verified installation to the latest stable version, including during preview.

Before installation, Agentic.Check inspects PATH (and Windows PATHEXT) without executing
any discovered `dna`. A conflicting command's path is shown with an “Install anyway?” choice
and a warning that it may hide the shorthand. Declining deselects only the shorthand.
`--yes` and noninteractive runs skip a conflicting shorthand action with a warning.
Shell profiles and PATH ordering are never changed. JSON reports include `dna` status,
conflicts, installed/resolved version, and skip/error information.

`dna <args>` uses the repo-local companion. Both `dna check <args>` and
`dotnet agentic check <args>` invoke the latest stable Agentic.Check through
`dotnet tool exec`, preserving arguments and standard streams. `dna check` works without
a local companion. A .NET 10 or later SDK must be selected in the current directory.

For development, add the locally packed companion folder to the target's `nuget.config`; see the
[companion README](../Agentic/README.md). Highest-version resolution spans all configured feeds, so
use unique test versions and isolated caches. Publish the matching package before exposing new
consumer content in a future release.
