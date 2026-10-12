using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RamCleaner.Core.Classification;
using RamCleaner.Core.Config;
using RamCleaner.Core.Enforcement;
using RamCleaner.Core.Memory;
using RamCleaner.Core.Processes;
using RamCleaner.Core.Rules;

namespace RamCleaner.App;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(60);

    private readonly Guard _guard;
    private readonly IProcessControl _processes;
    private readonly ConfigStore _store;
    private readonly ActivityLog _log;
    private readonly Dispatcher _ui;
    private readonly GuardRunner _runner;
    private readonly DispatcherTimer _metricsTimer;
    private readonly RuleSet _defaults = DefaultRules.Load();
    private readonly Dictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private UserConfig _config;
    private bool _refreshingMetrics;
    private bool _shutDown;

    public MainViewModel(Guard guard, IProcessControl processes, ConfigStore store, ActivityLog log, Dispatcher ui)
    {
        _guard = guard;
        _processes = processes;
        _store = store;
        _log = log;
        _ui = ui;
        _config = store.Load();
        _runner = new GuardRunner(guard, processes, log, () => _config);

        Profiles = [new(GuardProfile.Balanced, L.Balanced), new(GuardProfile.Aggressive, L.Aggressive)];
        SelectedProfile = Profiles.First(p => p.Value == _config.Profile);
        LoadRuleRows();
        SyncUserLists();
        LoadSettings();

        foreach (var entry in log.Snapshot().Reverse()) Log.Add(entry);
        log.Added += entry => _ui.InvokeAsync(() =>
        {
            Log.Insert(0, entry);
            if (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
        });
        guard.ProcessBlocked += OnProcessBlocked;

        _metricsTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await RefreshMetricsAsync(), ui);
        _ = RefreshMetricsAsync();
    }

    /// <summary>Raised on the UI thread, at most once per name per minute.</summary>
    public event Action<string>? BlockedNotification;

    public IReadOnlyList<ProfileOption> Profiles { get; }
    public IReadOnlyList<string> DevTools => _defaults.DevAllowedProcesses;
    public ObservableCollection<LogEntry> Log { get; } = [];
    public ObservableCollection<PreviewRow> PreviewRows { get; } = [];
    public ObservableCollection<RuleRow> ServiceRules { get; } = [];
    public ObservableCollection<RuleRow> ProcessRules { get; } = [];
    public ObservableCollection<string> UserAllowed { get; } = [];
    public ObservableCollection<string> UserBlocked { get; } = [];
    public ObservableCollection<string> UserBlockedServices { get; } = [];
    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];

    [ObservableProperty] private bool _isActive;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ToggleCommand))] private bool _isBusy;
    [ObservableProperty] private ProfileOption? _selectedProfile;
    [ObservableProperty] private string _totalRam = "";
    [ObservableProperty] private string _availableRam = "";
    [ObservableProperty] private double _usedPercent;
    [ObservableProperty] private string _agentSessions = "—";
    [ObservableProperty] private string _reclaimed = "0 MB";
    [ObservableProperty] private string _previewSummary = "";
    [ObservableProperty] private string _newPattern = "";
    [ObservableProperty] private bool _trimEnabled;
    [ObservableProperty] private int _trimIntervalSeconds;
    [ObservableProperty] private bool _autoStandbyPurge;
    [ObservableProperty] private int _standbyPurgeThresholdPercent;
    [ObservableProperty] private bool _startWithWindows;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            if (_guard.IsActive)
            {
                _runner.Stop();
                await Task.Run(() => _guard.Deactivate());
            }
            else
            {
                var rules = BuildRules();
                await Task.Run(() => _guard.Activate(rules));
                if (_guard.IsActive) _runner.Start(); // a racing deactivate must not leave the runner going
            }
        }
        catch (Exception ex)
        {
            _log.Write(LogKind.Warning, $"{L.ActivationFailed}: {ex.Message}");
        }
        finally
        {
            IsActive = _guard.IsActive;
            IsBusy = false;
        }
    }

    private bool CanToggle() => !IsBusy;

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var rules = BuildRules();
        var result = await Task.Run(() => _guard.Preview(rules));
        PreviewRows.Clear();
        foreach (var group in result.Processes
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(p => p.WorkingSetBytes)))
        {
            var label = group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key;
            PreviewRows.Add(new PreviewRow(label, L.KindProcess, Mb(group.Sum(p => p.WorkingSetBytes))));
        }
        foreach (var service in result.Services)
            PreviewRows.Add(new PreviewRow(service.Name, L.KindService, service.IsRunning ? L.Running : "—"));
        PreviewSummary = L.EstimatedGain(Mb(result.EstimatedBytes));
    }

    [RelayCommand]
    private Task PurgeStandbyAsync() => Task.Run(_runner.PurgeStandby);

    [RelayCommand]
    private void BlockAppFromInput()
    {
        BlockProcess(NewPattern);
        NewPattern = "";
    }

    [RelayCommand]
    private void AllowAppFromInput()
    {
        AllowProcess(NewPattern);
        NewPattern = "";
    }

    [RelayCommand]
    private void BlockServiceFromInput()
    {
        var name = NewPattern.Trim();
        NewPattern = "";
        if (name.Length == 0 || _config.BlockedServices.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        _config.BlockedServices.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    [RelayCommand]
    private void RemoveUserBlocked(string name) => RemoveFrom(_config.BlockedProcesses, name);

    [RelayCommand]
    private void RemoveUserAllowed(string name) => RemoveFrom(_config.AllowedProcesses, name);

    [RelayCommand]
    private void RemoveUserBlockedService(string name) => RemoveFrom(_config.BlockedServices, name);

    [RelayCommand]
    private void BlockSuggestion(SuggestionRow row)
    {
        BlockProcess(row.Name);
        Suggestions.Remove(row);
    }

    [RelayCommand]
    private void AllowSuggestion(SuggestionRow row)
    {
        AllowProcess(row.Name);
        Suggestions.Remove(row);
    }

    [RelayCommand]
    private async Task RefreshSuggestionsAsync()
    {
        var classifier = new ProcessClassifier(BuildRules(), new HashSet<int>());
        var snapshot = await Task.Run(() => _processes.Snapshot());
        Suggestions.Clear();
        foreach (var group in snapshot
                     .Where(p => p.SessionId != 0 && classifier.Classify(p) == Verdict.Unknown)
                     .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Sum(p => p.WorkingSetBytes)))
        {
            Suggestions.Add(new SuggestionRow(group.Key, group.Count(), Mb(group.Sum(p => p.WorkingSetBytes))));
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _config.TrimEnabled = TrimEnabled;
        _config.TrimIntervalSeconds = TrimIntervalSeconds;
        _config.AutoStandbyPurge = AutoStandbyPurge;
        _config.StandbyPurgeThresholdPercent = StandbyPurgeThresholdPercent;
        if (StartWithWindows != _config.StartWithWindows)
        {
            try
            {
                StartupTask.Apply(StartWithWindows);
                _config.StartWithWindows = StartWithWindows;
            }
            catch (Exception ex)
            {
                _log.Write(LogKind.Warning, ex.Message);
            }
        }
        _store.Save(_config);
        _config = _store.Load(); // re-applies clamping
        LoadSettings();
        _log.Write(LogKind.Info, L.Saved);
    }

    public void AllowProcess(string name)
    {
        name = NamePattern.Normalize(name);
        if (name.Length == 0) return;
        _config.BlockedProcesses.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (!_config.AllowedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase)) _config.AllowedProcesses.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    /// <summary>
    /// Logoff/shutdown: put start types back quickly without restarting services (Automatic ones start at next boot).
    /// Unlike <see cref="Shutdown"/> the view-model stays usable, because the user may cancel the logoff.
    /// </summary>
    public void OnSessionEnding()
    {
        _runner.Stop();
        try { _guard.Deactivate(restartServices: false); }
        catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
        IsActive = _guard.IsActive;
    }

    /// <summary>Stops the guard and restores services. Safe to call more than once.</summary>
    public void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _metricsTimer.Stop();
        _runner.Dispose();
        try { _guard.Deactivate(); }
        catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        if (value is null || value.Value == _config.Profile) return;
        _config.Profile = value.Value;
        SaveAndApply();
    }

    private void BlockProcess(string name)
    {
        name = NamePattern.Normalize(name);
        if (!NamePattern.IsSafeBlockPattern(name))
        {
            _log.Write(LogKind.Warning, L.UnsafePattern(name));
            return;
        }
        _config.AllowedProcesses.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (!_config.BlockedProcesses.Contains(name, StringComparer.OrdinalIgnoreCase)) _config.BlockedProcesses.Add(name);
        SyncUserLists();
        SaveAndApply();
    }

    private void RemoveFrom(List<string> list, string name)
    {
        list.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        SyncUserLists();
        SaveAndApply();
    }

    private EffectiveRules BuildRules() => EffectiveRules.Build(_defaults, _config);

    private void SaveAndApply()
    {
        _store.Save(_config);
        if (!_guard.IsActive) return;
        var rules = BuildRules();
        _ = Task.Run(() =>
        {
            try { _guard.UpdateRules(rules); }
            catch (Exception ex) { _log.Write(LogKind.Warning, ex.Message); }
        });
    }

    private void OnProcessBlocked(BlockedProcessEvent e) => _ui.InvokeAsync(() =>
    {
        var now = DateTime.Now;
        if (_lastNotified.TryGetValue(e.Name, out var last) && now - last < NotifyCooldown) return;
        _lastNotified[e.Name] = now;
        BlockedNotification?.Invoke(e.Name);
    });

    private void LoadRuleRows()
    {
        foreach (var rule in _defaults.BlockedServices)
        {
            var key = UserConfig.ServiceKey(rule.Name);
            ServiceRules.Add(new RuleRow(key, rule.Name, rule.Description, IsRuleEnabled(key, rule.EnabledByDefault), OnRuleToggled));
        }
        foreach (var rule in _defaults.BlockedProcesses)
        {
            var key = UserConfig.ProcessKey(rule.Name);
            ProcessRules.Add(new RuleRow(key, rule.Name, rule.Description, IsRuleEnabled(key, rule.EnabledByDefault), OnRuleToggled));
        }
    }

    private bool IsRuleEnabled(string key, bool byDefault) =>
        _config.RuleToggles.TryGetValue(key, out var value) ? value : byDefault;

    private void OnRuleToggled(RuleRow row)
    {
        _config.RuleToggles[row.Key] = row.IsEnabled;
        SaveAndApply();
    }

    private void SyncUserLists()
    {
        Replace(UserAllowed, _config.AllowedProcesses);
        Replace(UserBlocked, _config.BlockedProcesses);
        Replace(UserBlockedServices, _config.BlockedServices);
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void LoadSettings()
    {
        TrimEnabled = _config.TrimEnabled;
        TrimIntervalSeconds = _config.TrimIntervalSeconds;
        AutoStandbyPurge = _config.AutoStandbyPurge;
        StandbyPurgeThresholdPercent = _config.StandbyPurgeThresholdPercent;
        StartWithWindows = _config.StartWithWindows;
    }

    private async Task RefreshMetricsAsync()
    {
        if (_refreshingMetrics) return;
        _refreshingMetrics = true;
        try
        {
            var memory = SystemMemory.Read();
            TotalRam = Gb(memory.TotalBytes);
            AvailableRam = $"{Gb(memory.AvailableBytes)} ({memory.AvailablePercent:0}%)";
            UsedPercent = memory.UsedPercent;
            Reclaimed = Mb(_guard.ReclaimedBytes);
            var snapshot = await Task.Run(() => _processes.Snapshot());
            var agents = AgentSessionCounter.Count(snapshot);
            // Only agents that are running are listed, one per line, e.g. "Claude Code  6".
            AgentSessions = agents.Count == 0 ? "—" : string.Join(Environment.NewLine, agents.Select(a => $"{a.Agent}  {a.Sessions}"));
        }
        catch (Exception ex)
        {
            _log.Write(LogKind.Warning, ex.Message);
        }
        finally
        {
            _refreshingMetrics = false;
        }
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:N0} MB";
    private static string Gb(ulong bytes) => $"{bytes / 1073741824.0:0.0} GB";
}
