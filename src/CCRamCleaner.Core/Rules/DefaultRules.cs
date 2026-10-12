using System.Text.Json;

namespace CCRamCleaner.Core.Rules;

public static class DefaultRules
{
    private const string ResourceName = "CCRamCleaner.Core.Rules.rules.default.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static RuleSet Load()
    {
        using var stream = typeof(DefaultRules).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize<RuleSet>(stream, Options)
            ?? throw new InvalidOperationException("rules.default.json is empty.");
    }
}
