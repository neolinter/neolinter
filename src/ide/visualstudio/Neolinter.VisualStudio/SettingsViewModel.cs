using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;
using Neolinter.EditorConfig;

namespace Neolinter.VisualStudio;

/// <summary>Reads and writes the solution .editorconfig; shared by every view model of the window.</summary>
internal sealed class EditorConfigStore(string? path, Action<string> report)
{
    /// <summary>UTC ticks of the last write made by the window, so the file watcher can ignore it.</summary>
    public static long LastOwnWriteTicks;

    public string? Path { get; } = path;

    public IReadOnlyDictionary<string, string> Read(IEnumerable<string> keys) =>
        EditorConfigFile.ReadValues(Path is not null && File.Exists(Path) ? File.ReadAllText(Path) : string.Empty, keys);

    public void Set(string key, string value)
    {
        if (Path is null)
        {
            report("Open a solution to edit its .editorconfig.");
            return;
        }

        Interlocked.Exchange(ref LastOwnWriteTicks, DateTime.UtcNow.Ticks);
        File.WriteAllText(Path, EditorConfigFile.SetValue(File.Exists(Path) ? File.ReadAllText(Path) : string.Empty, key, value));
        report($"Saved {key} = {value}");
    }
}

[DataContract]
internal sealed class RuleOptionViewModel : NotifyPropertyChangedObject
{
    private readonly EditorConfigStore store;
    private readonly RuleOption option;
    private string value;

    public RuleOptionViewModel(EditorConfigStore store, RuleOption option, string value, Action<string> report)
    {
        this.store = store;
        this.option = option;
        this.value = value;
        Choices = option.Type == "boolean" ? ["true", "false"] : option.Values;
        Hint = $"{option.Description} ({option.Key}, default {option.Default})";
        this.report = report;
    }

    private readonly Action<string> report;

    [DataMember]
    public string Title => option.Title;

    [DataMember]
    public string Hint { get; }

    [DataMember]
    public List<string> Choices { get; }

    [DataMember]
    public string Value
    {
        get => value;
        set
        {
            if (option.Type == "integer" && (!int.TryParse(value, out var number) || number < (option.Minimum ?? int.MinValue)))
            {
                report($"{option.Key} must be an integer of at least {option.Minimum}.");
                return;
            }

            if (SetProperty(ref this.value, value))
            {
                store.Set(option.Key, value);
            }
        }
    }
}

[DataContract]
internal sealed class RuleViewModel : NotifyPropertyChangedObject
{
    private readonly EditorConfigStore store;
    private readonly Rule rule;
    private string severity;

    public RuleViewModel(EditorConfigStore store, Rule rule, IReadOnlyDictionary<string, string> values, Action<string> report)
    {
        this.store = store;
        this.rule = rule;
        severity = values.TryGetValue(rule.SeverityKey, out var s) ? s : rule.DefaultSeverity;
        foreach (var option in rule.Options)
        {
            var view = new RuleOptionViewModel(store, option, values.TryGetValue(option.Key, out var v) ? v : option.Default, report);
            (view.Choices.Count > 0 ? ChoiceOptions : TextOptions).Add(view);
        }
    }

    [DataMember]
    public string Title => $"{rule.Id} — {rule.Title}";

    [DataMember]
    public string Description => rule.Description;

    [DataMember]
    public List<string> Severities => [.. Rule.Severities];

    [DataMember]
    public ObservableList<RuleOptionViewModel> TextOptions { get; } = [];

    [DataMember]
    public ObservableList<RuleOptionViewModel> ChoiceOptions { get; } = [];

    [DataMember]
    public bool Enabled
    {
        get => severity != "none";
        set => Severity = value ? (rule.DefaultSeverity == "none" ? "suggestion" : rule.DefaultSeverity) : "none";
    }

    [DataMember]
    public string Severity
    {
        get => severity;
        set
        {
            if (SetProperty(ref severity, value))
            {
                store.Set(rule.SeverityKey, value);
                RaiseNotifyPropertyChangedEvent(nameof(Enabled));
            }
        }
    }
}

[DataContract]
internal sealed class SettingsViewModel : NotifyPropertyChangedObject, IDisposable
{
    private readonly Func<CancellationToken, Task<(string? EditorConfigPath, string PackageBanner)>> resolve;
    private string status = string.Empty;
    private string location = string.Empty;
    private string packageBanner = string.Empty;
    private string? currentPath;
    private FileSystemWatcher? watcher;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim reloading = new(1, 1);

    public SettingsViewModel(Func<CancellationToken, Task<(string? EditorConfigPath, string PackageBanner)>> resolve)
    {
        this.resolve = resolve;
        ReloadCommand = new AsyncCommand(async (_, _, cancellationToken) => await ReloadAsync(cancellationToken));
    }

    /// <summary>
    /// Keeps the window in sync without a manual reload: polls the open solution every few seconds (there is no
    /// solution-changed event we could rely on) and a file watcher reloads when the .editorconfig is edited elsewhere.
    /// </summary>
    public void StartAutoReload()
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            try
            {
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    var (path, _) = await resolve(stop.Token);
                    if (path != currentPath)
                    {
                        await ReloadAsync(stop.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void Watch(string? editorConfigPath)
    {
        watcher?.Dispose();
        watcher = null;
        if (editorConfigPath is null)
        {
            return;
        }

        watcher = new FileSystemWatcher(Path.GetDirectoryName(editorConfigPath)!, Path.GetFileName(editorConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler changed = (_, _) =>
        {
            // The window's own saves must not rebuild the cards while the user is typing.
            if (DateTime.UtcNow.Ticks - Interlocked.Read(ref EditorConfigStore.LastOwnWriteTicks) > TimeSpan.FromSeconds(1).Ticks)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    await ReloadAsync(stop.Token);
                });
            }
        };
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += (sender, _) => changed(sender, null!);
    }

    public void Dispose()
    {
        stop.Cancel();
        watcher?.Dispose();
        stop.Dispose();
        reloading.Dispose();
    }

    /// <summary>Re-reads the open solution and its .editorconfig (also picks up edits made outside the window).</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await reloading.WaitAsync(cancellationToken);
        try
        {
            await ReloadCoreAsync(cancellationToken);
        }
        finally
        {
            reloading.Release();
        }
    }

    private async Task ReloadCoreAsync(CancellationToken cancellationToken)
    {
        var (editorConfigPath, banner) = await resolve(cancellationToken);
        currentPath = editorConfigPath;
        Watch(editorConfigPath);
        var store = new EditorConfigStore(editorConfigPath, message => Status = message);
        Location = editorConfigPath is null ? "No solution open." : $"Settings are stored in {editorConfigPath}";
        PackageBanner = banner;
        var rules = Rule.LoadEmbedded().Where(r => r.Type == "realtime").ToList();
        var values = store.Read(rules.SelectMany(r => r.Options.Select(o => o.Key).Append(r.SeverityKey)));
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(new RuleViewModel(store, rule, values, message => Status = message));
        }

        Status = string.Empty;
    }

    [DataMember]
    public IAsyncCommand ReloadCommand { get; }

    [DataMember]
    public string Location
    {
        get => location;
        private set => SetProperty(ref location, value);
    }

    /// <summary>Empty when the solution references the Neolinter package (or has no C# project).</summary>
    [DataMember]
    public string PackageBanner
    {
        get => packageBanner;
        private set => SetProperty(ref packageBanner, value);
    }

    [DataMember]
    public ObservableList<RuleViewModel> Rules { get; } = [];

    [DataMember]
    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }
}
