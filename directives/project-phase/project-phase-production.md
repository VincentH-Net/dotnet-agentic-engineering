# Project Phase: In Production Directive

This lets you:

- Tell agents that stored data, deployed settings or other clients depend on the project, so a rename or restructure keeps them working
- Have the agent say whether it kept an old name or migrated what used it

A repo has one project phase: this directive or [pre-release](project-phase-pre-release.md). `dna check` asks which one applies and replaces one with the other when you switch.

Use `dna check` to choose or switch the project phase, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- project-phase-production:start -->
## Project Phase
This project is in production: stored data, deployed settings or other clients depend on it and MUST keep working.

- When you rename or restructure, keep every name that leaves the repository working: serialized names, database names, configuration keys, routes and other public contracts. Keep the old name with the mechanism the repository already uses, or migrate the data and settings as part of the change, and say which you did.
- A name that only this repository's code uses can change freely.
<!-- project-phase-production:end -->
~~~
