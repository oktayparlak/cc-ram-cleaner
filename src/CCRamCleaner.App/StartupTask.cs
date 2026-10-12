using System.Diagnostics;
using System.IO;
using System.Text;
using CCRamCleaner.Core.Startup;

namespace CCRamCleaner.App;

/// <summary>
/// "Start with Windows" via Task Scheduler with highest privileges: a Run-key entry would
/// show a UAC prompt on every logon because the app requires administrator rights.
/// The task is created from XML so it is not stopped on battery or after 72 hours.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "CCRamCleaner";

    public static void Apply(bool enabled)
    {
        if (!enabled)
        {
            RunSchtasks($"/Delete /F /TN \"{TaskName}\"", throwOnError: false);
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown.");
        var xmlPath = Path.Combine(Path.GetTempPath(), $"CCRamCleaner-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, ScheduledTaskXml.Build(exe, $"{Environment.UserDomainName}\\{Environment.UserName}"), Encoding.Unicode);
            RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"", throwOnError: true);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static void RunSchtasks(string arguments, bool throwOnError)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        if (throwOnError && process.ExitCode != 0) throw new InvalidOperationException($"schtasks failed: {error.Trim()}");
    }
}
