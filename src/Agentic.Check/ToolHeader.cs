using System.Reflection;
using Spectre.Console;

namespace Agentic.Check;

sealed record ToolHeaderLine(string Agentic, string Separator, string Check);

static class ToolHeader
{
    public const string AgenticColor = "cyan";
    public const string CheckColor = "green";
    public const string DotNetColor = "#b197fc";
    public const int MaxSeparatorWidth = 100;
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

    public static string Description => """
        Optimizes your repo for agentic engineering with .NET - based technologies

        Use 'agentic-check -h' for full tool description and parameter usage

        """;

    public static string ProductLine
        => $"\n✓ .NET Agentic Engineering Check {Version}";

    public static string ProductLineMarkup
        => "\n"
            + Styled($"{CheckColor}", "✓ ")
            + Styled($"underline {DotNetColor}", ".NET ")
            + Styled($"underline {AgenticColor}", "Agentic")
            + Styled($"underline {DotNetColor}", " Engineering ")
            + Styled($"underline {CheckColor}", "Check")
            + Subdued($" {Version}");

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

    public static string RepositoryHelp
        => $"F1 to learn more at {RepositoryUrl}";

    public static string RepositoryHelpMarkup
        => $"{KeyMarkup("F1")} to learn more at [link={RepositoryUrl}]{Markup.Escape(RepositoryUrl)}[/]";

    public static IReadOnlyList<string> DescriptionLines
        => Description.Split('\n');

    public static int HeaderContentWidth
        => Math.Max(
            DescriptionLines
                .Select(line => line.Length)
                .DefaultIfEmpty(0)
                .Max(),
            RepositoryHelp.Length);

    public static string SeparatorMarkup(int width)
        => $"[bold]{Markup.Escape(new string('─', Math.Clamp(width, 1, MaxSeparatorWidth)))}[/]";

    public static string KeyMarkup(string value)
        => $"[black on white]{Markup.Escape(value)}[/]";

    static string Styled(string style, string value)
        => $"[bold {style}]{Markup.Escape(value)}[/]";

    static string Subdued(string value)
        => $"[grey]{Markup.Escape(value)}[/]";
}
