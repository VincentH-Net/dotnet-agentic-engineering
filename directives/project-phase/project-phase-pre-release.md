# Project Phase: Pre-release Directive

This lets you:

- Tell agents that nothing outside the repo depends on it yet, so they keep names consistent everywhere when they rename or restructure
- Avoid compatibility shims and migration code that a project without users does not need

A repo has one project phase: this directive or [in production](project-phase-production.md). `dna check` asks which one applies and replaces one with the other when you switch. Switch to in production when the project gets stored data, deployed settings or other clients that must keep working.

Use `dna check` to choose or switch the project phase, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- project-phase-pre-release:start -->
## Project Phase
This project is pre-release: no stored data, deployed settings or other clients depend on it yet.

- When you rename or restructure, change every place that uses a name, including serialized names, database names, configuration keys and routes. Do NOT add compatibility shims, aliases or migration code for old names.
- When the project goes to production this directive must change. Tell the user to switch it with `dna check --project-phase production`.
<!-- project-phase-pre-release:end -->
~~~
