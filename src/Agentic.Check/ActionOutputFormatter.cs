namespace Agentic.Check;

static class ActionOutputFormatter
{
    internal const string ProgressIndent = "  ";

    const int ActionColumnWidth = 24;

    internal static string FormatSelectionSummary(int actionCount)
        => actionCount switch
        {
            0 => "No actions selected",
            1 => "1 action selected",
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{actionCount} actions selected")
        };

    internal static string FormatHeader()
        => FormatLine("Action", "Name");

    internal static string FormatLine(string action, string name)
    {
        string paddedAction = action.PadRight(ActionColumnWidth);
        return $"  {paddedAction}  {name}";
    }
}
