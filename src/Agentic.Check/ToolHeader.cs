using System.Reflection;
using Spectre.Console;

namespace Agentic.Check;

sealed record ToolHeaderLine(string Agentic, string Separator, string Check);

static class ToolHeader
{
    public const string Name = "agentic-check";
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
        => $"{Name} checks a repo for agentic directives and skills, then installs or updates selected recommendations.";

    public static string ProductLine
        => $".NET Agentic Engineering Check {Version}";

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
        => $"Tool repo: [link={RepositoryUrl}]{Markup.Escape(RepositoryUrl)}[/]";
}
