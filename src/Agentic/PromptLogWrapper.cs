using System.Text;

namespace Agentic;

static class PromptLogWrapper
{
    static readonly UTF8Encoding Utf8 = new(false, true);

    internal static async Task WrapAsync(string input, string output, TextReader stdin, CancellationToken cancellationToken, Action? beforeReplace = null, TextWriter? stdout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? destination = output == "-" ? null : ResolvePath(output);
        if (input != "-" && destination is not null
            && ResolvePath(input).Equals(destination, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException("Input and prompt-log output must be different files.");
        }

        string text = input == "-"
            ? await stdin.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
            : await ReadInputAsync(input, destination, cancellationToken).ConfigureAwait(false);
        string block = PromptBlock.Format(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (destination is null)
        {
            await (stdout ?? Console.Out).WriteAsync(block.AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Keep the sidecar stable across atomic replacements; unlinking it could let two
        // writers acquire different lock inodes. Concurrent access fails instead of racing.
        using FileStream gate = new(destination + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            // Empty input deliberately clears a named output, so an old block cannot be reused.
            await File.WriteAllTextAsync(temporary, block, Utf8, cancellationToken).ConfigureAwait(false);
            beforeReplace?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    static async Task<string> ReadInputAsync(string input, string? destination, CancellationToken cancellationToken)
    {
        // Holding both handles exclusively also rejects hard-link aliases of the same file.
        using FileStream source = new(input, FileMode.Open, FileAccess.Read, FileShare.None);
        using FileStream? existing = destination is not null && File.Exists(destination)
            ? new(destination, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        using StreamReader reader = new(source, Utf8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    static string ResolvePath(string path)
    {
        string full = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(full);
        if (parent is not null)
        {
            full = Path.Combine(ResolvePath(parent), Path.GetFileName(full));
        }

        FileSystemInfo entry = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        return entry.Exists ? entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full : full;
    }
}
