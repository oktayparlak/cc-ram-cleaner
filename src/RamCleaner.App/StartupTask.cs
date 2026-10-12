using System.Diagnostics;

namespace RamCleaner.App;

/// <summary>
/// "Start with Windows" via Task Scheduler with highest privileges: a Run-key entry would
/// show a UAC prompt on every logon because the app requires administrator rights.
/// </summary>
public static class StartupTask
{
    private const string TaskName = "RamCleaner";

    public static void Apply(bool enabled)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unknown.");
        var arguments = enabled
            ? $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{exe}\\\" --minimized\""
            : $"/Delete /F /TN \"{TaskName}\"";

        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        })!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        if (enabled && process.ExitCode != 0) throw new InvalidOperationException($"schtasks failed: {error.Trim()}");
    }
}
