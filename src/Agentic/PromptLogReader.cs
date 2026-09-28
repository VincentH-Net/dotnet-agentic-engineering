namespace Agentic;

sealed record ParsedPromptLog(string Text, string Description);

static class PromptLogReader
{
    const string Unsupported = "Unsupported prompt-log format. Use a tool version that supports the recorded format.";

    internal static ParsedPromptLog? Parse(string text, bool blockOnly = false)
    {
        string[] lines = PromptBlock.Normalize(text).TrimEnd('\n').Split('\n');
        int[] starts = Find(lines, line => line == PromptBlock.UnversionedStart);
        int[] versionedStarts = Find(lines, PromptBlock.IsVersionedStart);
        int[] ends = Find(lines, line => line == PromptBlock.End);
        if (starts.Length == 0 && versionedStarts.Length == 0 && ends.Length == 0 && !blockOnly)
        {
            return null;
        }

        // Versioned blocks escape unversioned start lines, but historical bodies can contain
        // unescaped versioned ones, so any unversioned start selects the historical formats.
        if (starts.Length == 0 && versionedStarts.Length > 0)
        {
            string[] body = Body(lines, versionedStarts, ends, blockOnly);
            return lines[versionedStarts[0]] == PromptBlock.Start
                ? new(PromptBlock.Read(body), "valid prompt log")
                : throw new FormatException(Unsupported);
        }

        // These LegacyPromptLogReader calls are the only historical-format dispatch.
        // Remove them and that class when historical reading is no longer required.
        if (!blockOnly && starts is [0] && ends.Length == 0
            && LegacyPromptLogReader.TryReadStandalone(lines) is { } standalone)
        {
            return standalone;
        }

        string[] historical = Body(lines, starts, ends, blockOnly);
        if (historical.FirstOrDefault() == LegacyPromptLogReader.RawV1Header)
        {
            return LegacyPromptLogReader.ReadRawV1Block(historical);
        }

        if (historical.FirstOrDefault()?.StartsWith("prompt-log-format:", StringComparison.Ordinal) == true)
        {
            throw new FormatException(Unsupported);
        }

        return LegacyPromptLogReader.ReadJsonBlock(historical);
    }

    static int[] Find(string[] lines, Func<string, bool> match) => [.. Enumerable.Range(0, lines.Length).Where(i => match(lines[i]))];

    static string[] Body(string[] lines, int[] starts, int[] ends, bool blockOnly)
        => starts.Length != 1 || ends.Length != 1 || starts[0] >= ends[0]
            || (blockOnly && (starts[0] != 0 || ends[0] != lines.Length - 1))
            ? throw new FormatException("Prompt log requires one ordered pair of full-line delimiters. Regenerate malformed blocks with prompt-log wrap.")
            : lines[(starts[0] + 1)..ends[0]];
}
