using System.ComponentModel;
using System.Diagnostics;

namespace Agentic;

// Shared source for the two launchers; no setup or package-resolution policy lives here.
static class ToolLauncher
{
    internal static async Task<int> CheckAsync(string[] arguments, string directory, TextWriter error, CancellationToken cancellationToken = default)
    {
        try
        {
            using Process sdk = new() { StartInfo = StartInfo(["--version"], directory) };
            sdk.StartInfo.RedirectStandardOutput = true;
            sdk.StartInfo.RedirectStandardError = true;
            _ = sdk.Start();
            try
            {
                var output = sdk.StandardOutput.ReadToEndAsync(cancellationToken);
                var errors = sdk.StandardError.ReadToEndAsync(cancellationToken);
                await sdk.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                string version = (await output.ConfigureAwait(false)).Trim();
                string diagnostics = await errors.ConfigureAwait(false);
                if (sdk.ExitCode != 0 || !Version.TryParse(version.Split('-')[0], out var selected) || selected.Major < 10)
                {
                    await error.WriteLineAsync("Agentic.Check requires .NET SDK 10 or later selected for this directory. Check the installed SDKs and global.json.").ConfigureAwait(false);
                    if (diagnostics.Length > 0)
                        await error.WriteAsync(diagnostics).ConfigureAwait(false);
                    return sdk.ExitCode == 0 ? 1 : sdk.ExitCode;
                }
            }
            finally
            {
                if (!sdk.HasExited)
                    sdk.Kill(entireProcessTree: true);
            }
        }
        catch (Win32Exception exception)
        {
            await error.WriteLineAsync($"Cannot start dotnet. Agentic.Check requires .NET SDK 10 or later: {exception.Message}").ConfigureAwait(false);
            return 127;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }

        return await RunAsync(["tool", "exec", "Agentic.Check", "--", .. arguments], directory, error, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> RunAsync(string[] arguments, string directory, TextWriter error, CancellationToken cancellationToken = default)
    {
        using Process process = new() { StartInfo = StartInfo(arguments, directory) };
        // The foreground child receives Ctrl+C too. Let it terminate and return its exit code.
        static void Cancel(object? _, ConsoleCancelEventArgs eventArgs) => eventArgs.Cancel = true;
        bool started = false;
        Console.CancelKeyPress += Cancel;
        try
        {
            _ = process.Start();
            started = true;
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            await error.WriteLineAsync($"Cannot start dotnet: {exception.Message}").ConfigureAwait(false);
            return 127;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            if (started && !process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    static ProcessStartInfo StartInfo(string[] arguments, string directory)
    {
        ProcessStartInfo info = new("dotnet") { WorkingDirectory = directory, UseShellExecute = false };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }
}
