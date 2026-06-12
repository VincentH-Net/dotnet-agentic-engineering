# Agentic-Check spec

I want to create an `agentic-check` cli tool as a dotnet tool (so a NuGet .NET 10 tool package Agentic.Check) that runs on mac, linux and windows machines with .NET 10 SDK installed.

Create the cli tool project in src/Agentic.Check

agentic-check must use NuGets System.CommandLine for command line parsing and Spectre.Console for polished terminal UX; if you need an example for how the two can be combined, look at the Aspire.Dev CLI tool.

agentic-check is a companion tool to the dotnet-agentic-engineering repo in the working folder, it's purpose is to verify and (when approved to fix) install/update the optimal set of agentic tools and instructions for a local target repo: installed harnesses, harnesses configuration, MCP's, skills and directives. For tools that are not easily automatically installed / updated it is fine to just give the user instructions to manually do that.

This first phase spec is to implement skills install and skills update for foundation, dotnet and uno technologies; agentic-check must use `gh skill` to ensure that the latest version of specific skills from below github repo's are installed as repo-local skills:
1. dotnet     = https://github.com/dotnet/skills
2. Uno Studio = https://github.com/unoplatform/studio
3. Matt       = https://github.com/mtmattei/UnoPlatformSkills
4. Vincent    = https://github.com/VincentH-Net/dotnet-agentic-engineering

Align the names of command line parameters to well known .NET cli tools such as `dotnet`, `aspire` and check/doctor tools like `uno-check`.
Include typical check/doctor tool parameters and functionality: dry run, report and ask the user confirmation to fix as you go, an option to skip interactive confirmation and what else may seem appropriate for the purpose and scope of this tool.

agentic-check must inspect the local target repo content to detect skill install gates values:

1. ensure prerequisites and report versions
   - git --version
   - gh --version
   - gh skill --help
   If no recent versions are installed, agentic-check must direct the user how to install / update, and then abort.

2. determine and report the git repo root folder from the optionally specified target dir, or if that is not specified the working dir. if no git repo root dir is found, offer to make the specified target dir or if not specified the working dir into a repo by running `git init`, and only proceed if the user confirms and git init succeeds.

3. agentic-check must detect which .NET technologies are available in the `stack`: `foundation` is always present, then detect `dotnet`, `uno` presence using the same criteria as the plugins/dotnet/skills/ensure-directives/SKILL.md skill. Detect `orleans` from the presence of any `Microsoft.Orleans.*` NuGet package references in a project.

4. If `uno` is detected, agentic-check must scan the uno platform projects to detect the uno skill install gates values.
   The install gates and their possible values (each gate can have 0 or more of it's values) are:

   - presentation
     - mvux if .csproj has <UnoFeatures> that contains mvux (any mix of upper/lower case) or <PackageReference> with Uno.Extensions.Reactive.WinUI
     - mvvm if .csproj has <UnoFeatures> that contains mvvm (any mix of upper/lower case) or <PackageReference> with CommunityToolkit.Mvvm
   - markup
     - xaml always
     - csharp if .csproj has <UnoFeatures> that contains csharpmarkup (any mix of upper/lower case) or <PackageReference> with Uno.WinUI.Markup
     - csharp2 if .csproj has PackageReference with CSharpMarkup.WinUI
   - theme
     - cupertino if .csproj has <UnoFeatures> that contains Cupertino (any mix of upper/lower case) or <PackageReference> with Uno.Cupertino.WinUI
     - material if .csproj has <UnoFeatures> that contains Material (any mix of upper/lower case) or <PackageReference> with Uno.Material.WinUI
     - simple if .csproj has <UnoFeatures> that contains SimpleTheme (any mix of upper/lower case)
     - fluent otherwise

   If agentic-check detected multiple values for the same gate, it must warn the user that agents may become confused by this and list which projects have which of the multiple gate values.

5. agentic-check must build a collection of skills to check for repo-local presence and version, based on the detected technologies and install gates:
   - always all `foundation` technology skills from Vincent repo
   - if `dotnet` technology is detected all dotnet skills from Vincent repo
   - if `orleans` technology is detected all orleans skills from Vincent repo
   - if `uno` technology is detected, install skills according to gates in below table (`none` gates always install):

   Skill repo    Skill                                      Install gate
  ━━━━━━━━━━━━  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   Vincent       uno-agentic-support                        none
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-mvvm                                   -presentation mvvm
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-csharpmarkup2                          -markup csharp2
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-xaml                                   -markup xaml
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-fluent2                                -theme fluent
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-hamburgermenu-databinding              -presentation mvvm
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-livecharts2-theme-switching            none
  ────────────  ─────────────────────────────────────────  ──────────────────────────────────
   Vincent       uno-responsive-spanning-gridwrap-layout    none
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Vincent       uno-test-resize-app-window                 none
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Matt          uno-extensions-services                    none
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Matt          uno-csharp-markup                          -markup csharp
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-mvux-* skills                          -presentation mvux
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-navigation-* skills                    none
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-testing-* skills                       none
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-themes-material                        -theme material
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-themes-simple                          -theme simple
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-themes-semantic-colors-brushes         -theme material or -theme simple
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-toolkit-material-theme                 -theme material
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    uno-toolkit-csharp-markup                  -markup csharp
  ────────────  ─────────────────────────────────────────────────────────────────────────────
   Uno Studio    other uno-toolkit-* skills                 none

6. Then agentic-check must check the repo-local presence of all skills in the collection and report either all skills present, or display an interactive multi-selection list of all missing skills like `gh skills` presents in interactive installs (Found <nr> recommended skills missing, select skill(s) to install:  [Use arrows to move, space to select, <right> to all, <left> to none, type to filter]), with by default all items selected for install, with enter to confirm the selection. The list must show the repo and skill name parameters as would be passed to `gh skill`.

7. If the user confirms to install any missing recommended skills, install them using `gh skills` as repo-local skills.  
  
8. If `dotnet` was detected, advise the user to install relevant skills for dotnet skills repo with command `gh skill install dotnet/skills`

9. Advise the user to update all repo-local skills with `gh skill update`
