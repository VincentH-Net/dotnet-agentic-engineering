using System.Text.Json;

namespace Agentic;

// Read-only compatibility for the original Perl JSON-line blocks and numbered standalone
// commits. No current-format wrapping, file handling, or Git operations belong here.
static class LegacyPromptLogReader
{
    internal static ParsedPromptLog? TryReadStandalone(IReadOnlyList<string> lines)
        => lines.Skip(1).FirstOrDefault(line => line.Length > 0)?.StartsWith("1. ", StringComparison.Ordinal) == true
            ? new(string.Join('\n', lines.Skip(1)).TrimStart('\n'), "legacy prompt log (raw text)")
            : null;

    internal static ParsedPromptLog ReadJsonBlock(IReadOnlyList<string> lines)
    {
        List<string> entries = [];
        List<string> entryLines = [];
        try
        {
            foreach (string line in lines)
            {
                if (line.Length == 0)
                {
                    if (entryLines.Count == 0)
                    {
                        throw new FormatException("Historical JSON prompt log has an empty entry or repeated separator.");
                    }

                    entries.Add(string.Join('\n', entryLines));
                    entryLines.Clear();
                    continue;
                }

                using var json = JsonDocument.Parse(line);
                if (json.RootElement.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("Every historical prompt-log line must be a JSON string.");
                }

                entryLines.Add(json.RootElement.GetString()!);
            }
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid historical JSON prompt log: " + exception.Message, exception);
        }

        if (entryLines.Count > 0)
        {
            entries.Add(string.Join('\n', entryLines));
        }
        else if (entries.Count == 0)
        {
            throw new FormatException("Historical JSON prompt log must contain an entry before its closing marker.");
        }

        // The Perl helper emitted a trailing separator; the first companion writer did not.
        return new(string.Join("\n\n", entries), "legacy prompt log (JSON)");
    }
}
