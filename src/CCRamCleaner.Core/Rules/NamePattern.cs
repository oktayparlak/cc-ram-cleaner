using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CCRamCleaner.Core.Rules;

/// <summary>Case-insensitive, culture-invariant process/service name matching with '*' wildcards.</summary>
public static class NamePattern
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static string Normalize(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    public static bool Matches(string pattern, string name)
    {
        var p = Normalize(pattern);
        var n = Normalize(name);
        if (!p.Contains('*')) return string.Equals(p, n, StringComparison.OrdinalIgnoreCase);
        var regex = Cache.GetOrAdd(p, key => new Regex(
            "^" + Regex.Escape(key).Replace(@"\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        return regex.IsMatch(n);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string name) =>
        patterns.Any(p => Matches(p, name));

    /// <summary>Rejects block patterns too broad to apply safely ("*", "a*", empty).</summary>
    public static bool IsSafeBlockPattern(string pattern) =>
        Normalize(pattern).Replace("*", "").Length >= 3;
}
