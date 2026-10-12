using RamCleaner.Core.Config;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests.TestSupport;

internal static class TestRules
{
    public static EffectiveRules Make(
        GuardProfile profile = GuardProfile.Balanced,
        string[]? services = null,
        string[]? blocked = null,
        string[]? dev = null,
        string[]? userAllowed = null,
        string[]? userBlocked = null) => new()
    {
        Profile = profile,
        BlockedServices = services ?? [],
        DefaultBlockedProcesses = blocked ?? ["Spotify"],
        DevAllowedProcesses = dev ?? ["claude", "node", "chrome", "com.docker.*"],
        UserAllowedProcesses = userAllowed ?? [],
        UserBlockedProcesses = userBlocked ?? [],
    };
}
