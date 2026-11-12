---
name: dotnet-refactoring
description: What a green build does not check when C# code is refactored. Use when renaming, moving, extracting, inlining, merging or deduplicating C# / .NET code. Covers names read at runtime, edits to text that only looks like a reference, behavior drift while deduplicating, generated and partial code, and targets the default build skips.
metadata:
  author: https://github.com/VincentH-Net
  version: "1.0.0"
  framework: dotnet
  category: refactoring
  sources:
    - github.com/dotnet/skills/tree/main/plugins/dotnet/skills/csharp-refactoring
---

# .NET Refactoring: What the Build Does Not Check

A refactor changes structure, not behavior. The compiler finds every reference you missed, and the tests find the behavior they cover. This skill is about the rest: changes that compile, pass, and still break something.

Apply the rules that match the change. Renaming a private member in one file needs none of them.

## Names Read at Runtime

A name can be used by code the compiler does not see. Before renaming or moving a type, member or parameter, search for its name as text and check whether any of these reads it:

- Serialization: JSON, XML or database column names taken from the member name
- Configuration binding: section and key names bound to an options class
- Dependency injection or routing by name or convention: keyed services, named options, controller and action names
- Reflection, `dynamic`, and data binding paths in markup, such as XAML `{Binding Name}`
- Source generators and analyzers that match on a name or attribute

When one does, change every place in the repository that names it, so the code stays consistent. Then run something that goes through that path: a round-trip test, a configuration load, the page that binds it. If nothing covers it, say so in your report instead of calling the rename safe.

### Names That Cross a Boundary

Some of these names leave the repository: a serialized member that is stored or sent over the wire, a table or column name from a database mapping, a configuration key that an environment can also set, a route or another public contract. Whether anything outside already depends on such a name is not visible in the code, so your instructions decide:

- With the Backward-Compatible Renames directive: keep the external name with the mechanism the repository already uses, for example `[JsonPropertyName("oldName")]`, or migrate the stored data and settings as part of the change. Say in your report which one you did.
- Without it, or when the request sets it aside by name: rename the external name too, and add no compatibility code. List in your report each renamed name that leaves the repository.

## Change References, Not Text

A missed reference fails the build. An extra edit does not: after a find-and-replace, a string literal, a comment that documents a wire format, a log message that a test or a dashboard matches, and an unrelated overload with the same name all still compile. Change the references the compiler or a language server reports, and treat every other textual match as a separate decision.

## Deduplicate Without Changing Behavior

- Keep the existing owner. When `B` repeats logic that `A` owns, make `B` call `A`. Do not move ownership because either direction compiles.
- Pass each caller's values through unchanged. Extracted code must not adopt one caller's defaults, rounding, culture, evaluation order or side effects for all callers.
- When merging near-identical types, turn only what differs into parameters and keep every old value exactly.

## Generated and Partial Code

- Find every `partial` declaration of a type before changing it. They can be in other files and other folders.
- Do not edit generated files, such as `*.g.cs` or anything under `obj/`: the next build overwrites them without an error. Change what the generator reads.

## Targets the Default Build Skips

Code under `#if`, in a multi-targeted project, or in a file that only some target frameworks compile is not checked by a build of one target.

- Update every branch, including the ones that are inactive for the target you are building.
- Build each target framework that compiles the changed code, the way the repository's instructions build it. Use `dotnet build -f <target framework>` when they say nothing.
- Report each affected target you could not build on this machine, and why.

## Keep Behavior Changes Visible

When a request combines restructuring with a change in behavior, do both, as separate steps, and say in your report which change is which. Never edit a test only to make it pass after a refactor: a test that fails after a structural change is reporting a behavior change.
