using System.CommandLine;

namespace Agentic;

// Shared by the tools that print help; no other logic lives here.
static class HelpText
{
    // System.CommandLine 2.0.9 prints the executable name in Usage lines and offers no way to rename it,
    // so buffered help and parse-error output has those lines rewritten to the command the user typed.
    internal static string WithLauncherUsage(string helpText, string launcher)
    {
        string usage = "  " + RootCommand.ExecutableName;
        string[] lines = helpText.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line == usage || line.StartsWith(usage + " ", StringComparison.Ordinal))
            {
                lines[i] = "  " + launcher + lines[i][usage.Length..];
            }
        }

        return string.Join('\n', lines);
    }
}
