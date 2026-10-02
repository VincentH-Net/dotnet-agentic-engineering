# dna Directive

Marks the directive blocks in `AGENTS.md` as managed by dna, with a link to this repository, so readers know where they come from and that `dna check` updates them.

Use `dna check` to install or update directives for your technology, or manually copy below markdown in your `AGENTS.MD`:

~~~md
<!-- dna:start -->
The directive blocks below are managed by [dna](https://github.com/VincentH-Net/dotnet-agentic-engineering); run `dna check` to update them instead of editing them by hand.
<!-- dna:end -->
~~~
