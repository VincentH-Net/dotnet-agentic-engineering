namespace Agentic;

// Current format only. Historical formats live in LegacyPromptLogReader.
static class PromptBlock
{
    // The start delimiter carries the format version, so a block needs no separate format line.
    internal const string Start = "prompt-log-v2:";
    internal const string End = "prompt-log-end:";
    // The historical formats start with this unversioned delimiter.
    internal const string UnversionedStart = "prompt-log:";
    const string VersionedStartPrefix = "prompt-log-v";

    internal static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    internal static bool IsVersionedStart(string line)
        => line.Length > VersionedStartPrefix.Length + 1 && line.StartsWith(VersionedStartPrefix, StringComparison.Ordinal)
            && line.EndsWith(':') && line[VersionedStartPrefix.Length..^1].All(char.IsAsciiDigit);

    // Git can strip trailing whitespace, so protect lines that would become delimiters too.
    // Escaping every start delimiter, versioned or not, keeps the block's format unambiguous.
    static bool NeedsEscape(string line)
        => line.TrimEnd() is UnversionedStart or End || IsVersionedStart(line.TrimEnd()) || line.StartsWith('\\');

    internal static string Format(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var lines = Normalize(text).Split('\n').Select(line => NeedsEscape(line) ? "\\" + line : line);
        // One framing LF precedes End, independent of any final LF in the original text.
        return Start + "\n" + string.Join('\n', lines) + "\n" + End + "\n";
    }

    internal static string Read(IReadOnlyList<string> lines)
        => lines.Count == 0
            ? throw new FormatException($"Prompt log requires content before {End}. Regenerate the block with prompt-log wrap.")
            : Unescape(lines, NeedsEscape);

    internal static string Unescape(IReadOnlyList<string> lines, Func<string, bool> needsEscape)
    {
        List<string> decoded = [];
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.StartsWith('\\'))
            {
                line = line[1..];
                if (!needsEscape(line))
                {
                    throw new FormatException($"Invalid escape on prompt-log body line {i + 1}. Regenerate the block from raw text with prompt-log wrap; do not escape it by hand.");
                }
            }

            decoded.Add(line);
        }

        // Git cleanup can reduce an originally whitespace-only body to one empty line.
        return string.Join('\n', decoded);
    }
}
