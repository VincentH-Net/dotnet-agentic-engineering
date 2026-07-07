# .NET Build Errors and Warnings Directive

This lets you:

- configure build errors and warnings (nullable analysis, treat warnings as errors, enable all code analyzers and enforce code style in build)
- fix all errors and fix or (in rare, justified cases) suppress warnings, documenting the rationale
- enforce concise, readable code leveraging the latest C# language features

Use the `agentic-check` tool to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-agentic-engineering:dotnet-build-errors-and-warnings:start -->
## Configure and Fix .NET build errors and warnings

This directive does NOT govern WHEN to initiate a build, only WHAT to do before a build and when to repeat a build.

All `dotnet ...` commands MUST follow the separate "Running `dotnet ...`" directive.

1. Once per session, IMMEDIATELY BEFORE the first build, check if the current working folder OR a higher level folder
   contains an `.editorconfig` that contains the text
   `https://github.com/VincentH-Net/Modern.CSharp.Templates/blob/main/Editorconfig.md`.
   Do not count `.editorconfig` files inside `.git`, `.vs`, `bin`, `obj`, or `node_modules`.
   If NO, use the `dotnet-modern-csharp-editorconfig` skill EXACTLY as written to configure build errors and warnings in `.editorconfig` and MSBuild properties. Do NOT satisfy this check by manually adding the URL to an existing `.editorconfig`.

2. IMMEDIATELY AFTER building, IF any build errors or warnings are reported:

   1. IF there are multiple IDE formatting/style diagnostics that `dotnet format` can fix, run
      `dotnet format --include <files>` first IN THE FOREGROUND as described in the "Running `dotnet ...`" directive,
      ONLY including files that contain those diagnostics, to fix many diagnostics quickly. 
      Rebuild after formatting.

   2. Fix ALL remaining build errors and warnings by following these steps for each diagnostic:

      1. FIRST try HARD to fix the diagnostic by editing the code to comply with the rule that generated it.

      2. ONLY in RARE cases, where a fix would make the code much less readable AND the Microsoft Learn MCP or the
         https://learn.microsoft.com/ documentation of the error/warning describes a valid exclusion reason that
         CLEARLY applies in the context of the error/warning location, you can suppress diagnostics or warnings.
         If the Microsoft Learn MCP and https://learn.microsoft.com/ documentation are unavailable,
         do NOT suppress the diagnostic.
         When suppressing:
         - ALWAYS include the exclusion reason as rationale with the suppression
         - PREFER to do exclusions with the `[SuppressMessage]` attribute if possible; 
           IF the exclusion rationale applies to a whole project, add the attribute to a `GlobalSuppressions.cs`
           file (check for an existing file anywhere in the project, only create a new one if not found)

   3. Do not broaden the change beyond the files needed to fix the reported diagnostics,
      unless a wider mechanical format pass is explicitly required.
<!-- dotnet-agentic-engineering:dotnet-build-errors-and-warnings:end -->
~~~
