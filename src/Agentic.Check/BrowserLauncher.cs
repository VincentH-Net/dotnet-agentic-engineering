using System.Diagnostics;

namespace Agentic.Check;

static class BrowserLauncher
{
    internal static bool Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                _ = Process.Start("open", [url]);
                return true;
            }

            _ = Process.Start("xdg-open", [url]);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
