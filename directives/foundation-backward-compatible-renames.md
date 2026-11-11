# Backward-Compatible Renames Directive

This lets you:

- Have agents keep stored data, deployed settings and other clients working when they rename or restructure
- Have the agent say whether it kept an old name or migrated what used it

Deselect this directive in `dna check` while nothing outside the repo depends on the project yet, so a rename can change a name everywhere without compatibility code. `dna check` selects it again on each run.

Use `dna check` to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- foundation-backward-compatible-renames:start -->
## Backward-Compatible Renames
When you rename or restructure, keep every name that leaves the repository working: serialized names, database names, configuration keys, routes and other public contracts. Keep the old name with the mechanism the repository already uses, or migrate the data and settings as part of the change, and say which you did. A name that only this repository's code uses can change freely.
<!-- foundation-backward-compatible-renames:end -->
~~~
