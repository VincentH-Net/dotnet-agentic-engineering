using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Dna;

static class LocalCompanion
{
    internal static async Task<bool> IsMissingAsync(string directory)
    {
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "tool", "list", "--local", "--format", "json" })
            start.ArgumentList.Add(argument);
        try
        {
            using Process process = new() { StartInfo = start };
            _ = process.Start();
            try
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().ConfigureAwait(false);
                string listing = await output.ConfigureAwait(false);
                _ = await error.ConfigureAwait(false);
                if (process.ExitCode != 0)
                    return false;
                using var document = JsonDocument.Parse(listing);
                return !document.RootElement.GetProperty("data").EnumerateArray().Any(tool =>
                    string.Equals(tool.GetProperty("packageId").GetString(), "InnoWvate.Agentic", StringComparison.OrdinalIgnoreCase)
                    && tool.GetProperty("commands").EnumerateArray().Any(command => command.GetString() == "agentic"));
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            // If discovery is unavailable, let the normal invocation report the SDK/manifest
            // error. Only a successful listing can establish that the local tool is absent.
            return false;
        }
    }
}
