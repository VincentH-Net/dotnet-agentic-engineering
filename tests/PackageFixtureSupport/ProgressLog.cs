using System.Globalization;

namespace Agentic.PackageFixtures;

// One line per phase of a package scenario and a heartbeat while a recorded terminal waits, so a long
// run shows where it is. The full-suite runner prints the newest line while a test project runs, and
// the file stays with the reports of the run.
static class ProgressLog
{
    static readonly Lock Gate = new();

    internal static string Path => System.IO.Path.Combine(FixtureFiles.Reports, "progress.log");

    internal static void Append(string line)
    {
        string entry = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        lock (Gate)
        {
            _ = Directory.CreateDirectory(FixtureFiles.Reports);
            File.AppendAllText(Path, entry);
        }
    }
}
