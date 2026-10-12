namespace RamCleaner.Core.Tests.TestSupport;

internal static class TempPaths
{
    /// <summary>A path to a not-yet-existing file inside a fresh, unique temp directory.</summary>
    public static string NewFile(string fileName) =>
        Path.Combine(Path.GetTempPath(), "ramcleaner-tests", Guid.NewGuid().ToString("N"), fileName);
}
