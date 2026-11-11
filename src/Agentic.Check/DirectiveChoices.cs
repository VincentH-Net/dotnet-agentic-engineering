namespace Agentic.Check;

// What the check asks when a repository has not made a choice between alternative directives.
sealed record DirectiveChoice(string Group, string Question, IReadOnlyList<string> Explanation, IReadOnlyList<DirectiveChoiceOption> Options);

sealed record DirectiveChoiceOption(string DirectiveName, string Label);

static class DirectiveChoices
{
    internal const string ProjectPhase = "project-phase";

    // The value --project-phase takes for a member: its name without the group in front.
    internal static string ValueOf(DirectivePlanItem directive)
        => directive.Name.StartsWith(directive.Group + "-", StringComparison.Ordinal) ? directive.Name[(directive.Group.Length + 1)..] : directive.Name;

    internal static DirectiveChoice Describe(string group, IReadOnlyList<DirectivePlanItem> members)
        => group == ProjectPhase
            ? new(
                group,
                "Which phase is this project in?",
                [
                    "Agents rename and restructure differently depending on it: in a pre-release project they change every name",
                    "consistently; in production they keep stored data, deployed settings and other clients working.",
                    "Switch it with dna check when the project goes to production."
                ],
                [.. members.Select(member => new DirectiveChoiceOption(member.Name, ValueOf(member) switch
                {
                    "pre-release" => "Pre-release: nothing outside this repository depends on it yet",
                    "production" => "In production: stored data, deployed settings or other clients depend on it",
                    _ => member.Name
                }))])
            : new(group, $"Which {group} applies to this repository?", [], [.. members.Select(member => new DirectiveChoiceOption(member.Name, member.Name))]);

    internal static IReadOnlyList<string> RowNotes(DirectivePlanItem directive)
        => directive.Group == ProjectPhase
            ? [directive.Replaces is null
                ? "Choose the phase this project is in; agents rename and restructure by it."
                : "Select when the project's phase changed; it replaces the phase that is installed."]
            : [];
}
