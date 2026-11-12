# Backward-Compatible Renames Directive

This lets you:

- Have agents keep stored data, deployed settings and other clients working when they rename or restructure
- Have the agent say whether it kept an old name or migrated what used it
- Allow a breaking rename for one request, by naming this directive in it: "skip backward-compatible renames for this change"

Deselect this directive in `dna check` while nothing outside the repo depends on the project yet, so a rename can change a name everywhere without compatibility code. `dna check` selects it again on each run.

This directive uses the `dotnet-refactoring` skill; `dna check` selects the two together.

Use `dna check` to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dotnet-backward-compatible-renames:start -->
## Backward-Compatible Renames
When you rename or restructure, keep every name that leaves the repository working: serialized names, database names, configuration keys, routes and other public contracts. The only exception is a request that names this directive and says to skip it. Follow the `dotnet-refactoring` skill for both cases. A name that only this repository's code uses can change freely.
<!-- dotnet-backward-compatible-renames:end -->
~~~
