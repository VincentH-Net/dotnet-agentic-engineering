using System.Text.RegularExpressions;

namespace Agentic.Check;

sealed record CodexRule(IReadOnlyList<string> Pattern, string Justification)
{
    // Codex matches rules on literal command tokens, so the pattern identifies a rule.
    public string Key => string.Join('\n', Pattern);

    public string Command => string.Join(' ', Pattern);

    public string Render()
    {
        string tokens = string.Join(", ", Pattern.Select(token => $"\"{token}\""));
        return $"prefix_rule(\n    pattern = [{tokens}],\n    decision = \"allow\",\n    justification = \"{Justification}\",\n)\n";
    }
}

sealed record CodexRulesPlan(string File, bool FileExists, IReadOnlyList<CodexRule> Missing)
{
    public bool IsCurrent => Missing.Count == 0;

    public string Action => FileExists ? "update" : "install";

    public string Status => IsCurrent ? "up to date" : Action;
}

sealed record CodexRulesReport(string File, string Action, IReadOnlyList<string> Rules, bool Success, string? Error);

// Codex runs a command that matches a project allow rule outside its sandbox, so dotnet gets network
// access and its home-directory caches from the first attempt without an approval prompt. For Codex
// targets these rules are offered as a tool item, independent of the dotnet-cli-run directive text,
// and written to a dna-owned file. Rules the user already keeps in any other .codex/rules file count
// as installed and are never rewritten.
static partial class CodexRulesInstaller
{
    internal const string FileName = "dna-dotnet.rules";
    internal static readonly string RelativePath = Path.Combine(".codex", "rules", FileName);
    internal static SkillDependency Identity { get; } = new(string.Empty, "codex-rules");

    const string Header = "# Installed by dna check: Codex runs commands that match these rules outside its sandbox.";

    internal static string FilePath(string targetDirectory)
        => Path.Combine(targetDirectory, RelativePath);

    internal static IReadOnlyList<CodexRule> RequiredRules(StackDetectionResult stack)
    {
        List<CodexRule> rules =
        [
            new(["dotnet"], "dotnet needs network access and writes outside the workspace (NuGet caches), so it must run outside the sandbox from the first attempt."),
            new(["dnx"], "dnx is the SDK shorthand for dotnet tool exec; same needs as dotnet."),
            new(["dna"], "dna launches dotnet tools that fetch directives and skills; same needs as dotnet.")
        ];
        if (stack.Technologies.Contains(TechnologyNames.Uno, StringComparer.OrdinalIgnoreCase))
        {
            rules.Add(new(["pwsh", "./New-View.ps1"], "The Uno C# Markup 2 New-View.ps1 script wraps dotnet new; same needs as dotnet."));
        }

        return rules;
    }

    internal static CodexRulesPlan Plan(string targetDirectory, StackDetectionResult stack)
    {
        string file = FilePath(targetDirectory);
        string directory = Path.GetDirectoryName(file)!;
        HashSet<string> installed = new(StringComparer.Ordinal);
        if (Directory.Exists(directory))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.rules"))
            {
                installed.UnionWith(AllowedPatternKeys(File.ReadAllText(path)));
            }
        }

        CodexRule[] missing = [.. RequiredRules(stack).Where(rule => !installed.Contains(rule.Key))];
        return new(file, File.Exists(file), missing);
    }

    internal static SkillManifestEntry Action(CodexRulesPlan plan)
        => new(string.Empty, Identity.InstallArg, "Codex rules: run dotnet outside the sandbox", string.Empty, [],
            recommendationAction: plan.Action,
            version: $"{RelativePath}: {string.Join(", ", plan.Missing.Select(rule => rule.Command))}");

    internal static async Task<CodexRulesReport> EnsureAsync(CodexRulesPlan plan, bool dryRun, CancellationToken cancellationToken)
    {
        string[] rules = [.. plan.Missing.Select(rule => rule.Command)];
        if (dryRun)
        {
            return new(plan.File, plan.Action, rules, true, null);
        }

        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(plan.File)!);
            string existing = plan.FileExists
                ? await File.ReadAllTextAsync(plan.File, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            await File.WriteAllTextAsync(plan.File, Append(existing, plan.Missing), cancellationToken).ConfigureAwait(false);
            string written = await File.ReadAllTextAsync(plan.File, cancellationToken).ConfigureAwait(false);
            string[] unverified = [.. plan.Missing.Where(rule => !Contains(written, rule)).Select(rule => rule.Command)];
            return unverified.Length == 0
                ? new(plan.File, plan.Action, rules, true, null)
                : new(plan.File, plan.Action, rules, false, $"Validation failed: {plan.File} does not allow {string.Join(", ", unverified)}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(plan.File, plan.Action, rules, false, exception.Message);
        }
    }

    internal static string Append(string existingContent, IReadOnlyList<CodexRule> rules)
    {
        string content = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        if (content.Length == 0)
        {
            content = Header;
        }

        foreach (var rule in rules)
        {
            content += "\n\n" + rule.Render().TrimEnd('\n');
        }

        return content + "\n";
    }

    internal static bool Contains(string content, CodexRule rule)
        => AllowedPatternKeys(content).Contains(rule.Key, StringComparer.Ordinal);

    // Reads every prefix_rule whose decision is allow (the default) and returns its pattern key.
    internal static IReadOnlyList<string> AllowedPatternKeys(string content)
    {
        string code = StripComments(content);
        List<string> keys = [];
        int index = 0;
        while ((index = code.IndexOf("prefix_rule", index, StringComparison.Ordinal)) >= 0)
        {
            int open = code.IndexOf('(', index);
            int close = open < 0 ? -1 : FindClosing(code, open, '(', ')');
            if (close < 0)
            {
                break;
            }

            string body = code[(open + 1)..close];
            index = close + 1;
            var decision = DecisionRegex().Match(body);
            if (decision.Success && decision.Groups[1].Value != "allow")
            {
                continue;
            }

            if (PatternTokens(body) is { } tokens)
            {
                keys.Add(string.Join('\n', tokens));
            }
        }

        return keys;
    }

    static List<string>? PatternTokens(string body)
    {
        var pattern = PatternRegex().Match(body);
        if (!pattern.Success)
        {
            return null;
        }

        int open = pattern.Index + pattern.Length - 1;
        int close = FindClosing(body, open, '[', ']');
        if (close < 0)
        {
            return null;
        }

        List<string> tokens = [];
        int position = open + 1;
        while (position < close)
        {
            char current = body[position];
            if (char.IsWhiteSpace(current) || current == ',')
            {
                position++;
            }
            else if (current is '"' or '\'')
            {
                int end = FindStringEnd(body, position);
                tokens.Add(body[(position + 1)..end]);
                position = end + 1;
            }
            else if (current == '[')
            {
                // A nested alternatives list never equals a plain token, so keep it as is.
                int end = FindClosing(body, position, '[', ']');
                tokens.Add(body[position..(end + 1)]);
                position = end + 1;
            }
            else
            {
                int next = body.IndexOf(',', position, close - position);
                int end = next < 0 ? close : next;
                tokens.Add(body[position..end].Trim());
                position = end;
            }
        }

        return tokens;
    }

    static string StripComments(string content)
    {
        System.Text.StringBuilder result = new(content.Length);
        int position = 0;
        while (position < content.Length)
        {
            char current = content[position];
            if (current is '"' or '\'')
            {
                int end = FindStringEnd(content, position);
                _ = result.Append(content, position, end - position + 1);
                position = end + 1;
            }
            else if (current == '#')
            {
                int end = content.IndexOf('\n', position);
                position = end < 0 ? content.Length : end;
            }
            else
            {
                _ = result.Append(current);
                position++;
            }
        }

        return result.ToString();
    }

    static int FindClosing(string text, int open, char opening, char closing)
    {
        int depth = 0;
        for (int position = open; position < text.Length; position++)
        {
            char current = text[position];
            if (current is '"' or '\'')
            {
                position = FindStringEnd(text, position);
            }
            else if (current == opening)
            {
                depth++;
            }
            else if (current == closing && --depth == 0)
            {
                return position;
            }
        }

        return -1;
    }

    // Returns the index of the closing quote, or the last index when the string is unterminated.
    static int FindStringEnd(string text, int open)
    {
        char quote = text[open];
        for (int position = open + 1; position < text.Length; position++)
        {
            if (text[position] == '\\')
            {
                position++;
            }
            else if (text[position] == quote)
            {
                return position;
            }
        }

        return text.Length - 1;
    }

    [GeneratedRegex(@"\bdecision\s*=\s*""([a-z_]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionRegex();

    [GeneratedRegex(@"\bpattern\s*=\s*\[", RegexOptions.CultureInvariant)]
    private static partial Regex PatternRegex();
}
