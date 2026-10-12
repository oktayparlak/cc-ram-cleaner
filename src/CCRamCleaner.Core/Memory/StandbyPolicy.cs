namespace CCRamCleaner.Core.Memory;

public static class StandbyPolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

    public static bool ShouldPurge(MemoryStatus status, int thresholdPercent, DateTime? lastPurge, DateTime now) =>
        status.AvailablePercent < thresholdPercent && (lastPurge is null || now - lastPurge.Value >= MinInterval);
}
