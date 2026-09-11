namespace Agentic;

sealed record ParsedPromptLog(string Text, string Description);

static class PromptLogReader
{
    internal static ParsedPromptLog? Parse(string text, bool blockOnly = false)
    {
        string[] lines = PromptBlock.Normalize(text).TrimEnd('\n').Split('\n');
        int[] starts = [.. Enumerable.Range(0, lines.Length).Where(i => lines[i] == PromptBlock.Start)];
        int[] ends = [.. Enumerable.Range(0, lines.Length).Where(i => lines[i] == PromptBlock.End)];
        if (starts.Length == 0 && ends.Length == 0 && !blockOnly)
        {
            return null;
        }

        // These two LegacyPromptLogReader calls are the only historical-format dispatch.
        // Remove them and that class when historical reading is no longer required.
        if (!blockOnly && starts is [0] && ends.Length == 0
            && LegacyPromptLogReader.TryReadStandalone(lines) is { } standalone)
        {
            return standalone;
        }

        if (starts.Length != 1 || ends.Length != 1 || starts[0] >= ends[0]
            || (blockOnly && (starts[0] != 0 || ends[0] != lines.Length - 1)))
        {
            throw new FormatException("Prompt log requires one ordered pair of full-line delimiters. Regenerate malformed blocks with prompt-log wrap.");
        }

        string[] body = lines[(starts[0] + 1)..ends[0]];
        if (body.FirstOrDefault() == PromptBlock.FormatHeader)
        {
            return new(PromptBlock.Read(body), "valid prompt log");
        }

        if (body.FirstOrDefault()?.StartsWith("prompt-log-format:", StringComparison.Ordinal) == true)
        {
            throw new FormatException("Unsupported prompt-log format. Use a tool version that supports the recorded format.");
        }

        return LegacyPromptLogReader.ReadJsonBlock(body);
    }
}
