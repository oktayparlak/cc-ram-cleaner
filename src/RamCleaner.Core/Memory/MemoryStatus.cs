namespace RamCleaner.Core.Memory;

public sealed record MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
{
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - AvailableBytes) / TotalBytes;
    public double AvailablePercent => 100.0 - UsedPercent;
}
