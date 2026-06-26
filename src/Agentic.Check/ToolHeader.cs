using System.Reflection;
using Spectre.Console;

namespace Agentic.Check;

sealed record ToolHeaderLine(string Agentic, string Separator, string Check);

static class ToolHeader
{
    public const string AgenticColor = "cyan";
    public const string CheckColor = "green";
    public const string RepositoryUrl = "https://github.com/VincentH-Net/dotnet-agentic-engineering";

    public static IReadOnlyList<ToolHeaderLine> Lines { get; } =
    [
        new(@"                        _   _      ", @"      ", @"      _               _    "),
        new(@"  __ _  __ _  ___ _ __ | |_(_) ___ ", @"      ", @"  ___| |__   ___  ___| | __"),
        new(@" / _` |/ _` |/ _ \ '_ \| __| |/ __|", @" ____ ", @" / __| '_ \ / _ \/ __| |/ /"),
        new(@"| (_| | (_| |  __/ | | | |_| | (__ ", @"|____|", @"| (__| | | |  __/ (__|   < "),
        new(@" \__,_|\__, |\___|_| |_|\__|_|\___|", @"      ", @" \___|_| |_|\___|\___|_|\_\"),
        new(@"       |___/                       ", @"      ", @"                           ")
    ];

    public static string Description
        => $"\nThis tool composes a repo-optimized set of agentic directives and skills from best-in-class github skill repo's, based on which .NET technologies and features are used.\n\n" +
            "The skill composition minimizes context usage and avoids contradictions and ambiguities to reduce agent mistakes.\n\n" +
            "The tool lets you select what to install or update; for skills it uses 'gh skill' to install / update directly from the source repo's.\n\n" +
            "Currently supports foundational agentic habits, .NET, ASP.NET, Microsoft Orleans and Uno Platform.\n" +
            "Uno Platform skills are selected depending on repo usage of MVVM or MVUX update pattern, pure XAML markup or combined with either Uno C# Markup or C# Markup 2, and Fluent / Material / Cupertino design system.\n";
        //=> $"{Name} checks a repo for agentic directives and skills, then installs or updates selected recommendations.";

    public static string ProductLine
        => $"\n.NET Agentic Engineering Check {Version}";

    static string Version
    {
        get
        {
            string? informationalVersion = typeof(ToolHeader).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            string version = string.IsNullOrWhiteSpace(informationalVersion)
                ? typeof(ToolHeader).Assembly.GetName().Version?.ToString() ?? "unknown"
                : informationalVersion;
            int metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex < 0 ? version : version[..metadataIndex];
        }
    }

    public static string RepositoryLinkMarkup
        => $"See: [link={RepositoryUrl}]{Markup.Escape(RepositoryUrl)}[/]";
}
