using CommunityToolkit.Mvvm.ComponentModel;
using CCRamCleaner.Core.Config;

namespace CCRamCleaner.App;

public sealed record ProfileOption(GuardProfile Value, string Label);

public sealed record PreviewRow(string Name, string Kind, string Memory);

public sealed record SuggestionRow(string Name, int Count, string Memory);

public sealed partial class RuleRow(string key, string name, string description, bool isEnabled, Action<RuleRow> onChanged) : ObservableObject
{
    public string Key => key;
    public string Name => name;
    public string Description => description;

    [ObservableProperty]
    private bool _isEnabled = isEnabled;

    partial void OnIsEnabledChanged(bool value) => onChanged(this);
}
