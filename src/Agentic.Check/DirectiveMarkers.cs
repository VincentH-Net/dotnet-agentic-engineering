namespace Agentic.Check;

static class DirectiveMarkers
{
    const string LegacyPrefix = "dotnet-agentic-engineering:";

    internal static string Start(string name) => $"<!-- {name}:start -->";

    internal static string End(string name) => $"<!-- {name}:end -->";

    internal static bool Contains(string content, string name)
        => content.Contains($"<!-- {name}:", StringComparison.Ordinal)
            || content.Contains($"<!-- {LegacyPrefix}{name}:", StringComparison.Ordinal);

    internal static string Normalize(string content, string name)
        => content.Replace(Start(LegacyPrefix + name), Start(name), StringComparison.Ordinal)
            .Replace(End(LegacyPrefix + name), End(name), StringComparison.Ordinal);

    // Accept either complete pair, but reject duplicates, mixed formats and reversed markers
    // before replacing any user-owned file content.
    internal static Range? FindBlock(string content, string name)
    {
        Range? block = null;
        foreach (string prefix in new[] { string.Empty, LegacyPrefix })
        {
            string start = Start(prefix + name);
            string end = End(prefix + name);
            int from = content.IndexOf(start, StringComparison.Ordinal);
            int to = content.IndexOf(end, StringComparison.Ordinal);
            if (from < 0 && to < 0)
                continue;
            if (from < 0 || to < from || block is not null
                || content.LastIndexOf(start, StringComparison.Ordinal) != from
                || content.LastIndexOf(end, StringComparison.Ordinal) != to)
            {
                throw new DirectiveException($"Directive marker is inconsistent for {name}.");
            }

            block = from..(to + end.Length);
        }

        return block;
    }
}
