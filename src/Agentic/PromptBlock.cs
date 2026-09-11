namespace Agentic;

// Current format only. Historical JSON and numbered logs live in LegacyPromptLogReader.
static class PromptBlock
{
    internal const string Start = "prompt-log:";
    internal const string End = "prompt-log-end:";
    internal const string FormatHeader = "prompt-log-format: raw-v1";

    internal static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    internal static string Format(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // Git can strip trailing whitespace, so protect lines that would become delimiters too.
        var lines = Normalize(text).Split('\n').Select(line =>
            line.TrimEnd() is Start or End || line.StartsWith('\\') ? "\\" + line : line);
        // One framing LF precedes End, independent of any final LF in the original text.
        return Start + "\n" + FormatHeader + "\n" + string.Join('\n', lines) + "\n" + End + "\n";
    }

    internal static string Read(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2 || lines[0] != FormatHeader)
        {
            throw new FormatException("Raw prompt log requires its format header and content before prompt-log-end:. Regenerate the block with prompt-log wrap.");
        }

        List<string> decoded = [];
        for (int i = 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.StartsWith('\\'))
            {
                string unescaped = line[1..];
                if (unescaped.TrimEnd() is not (Start or End) && !unescaped.StartsWith('\\'))
                {
                    throw new FormatException($"Invalid escape on prompt-log body line {i}. Regenerate the block from raw text with prompt-log wrap; do not escape it by hand.");
                }

                line = unescaped;
            }

            decoded.Add(line);
        }

        // Git cleanup can reduce an originally whitespace-only body to one empty line.
        return string.Join('\n', decoded);
    }
}
