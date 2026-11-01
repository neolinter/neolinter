using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;
using Neolinter.EditorConfig;

namespace Neolinter.VisualStudio;

/// <summary>Reads and writes the solution .editorconfig; shared by every view model of the window.</summary>
internal sealed class EditorConfigStore(string? path, Action<string> report)
{
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
internal sealed class SettingsViewModel : NotifyPropertyChangedObject
{
    private string status = string.Empty;

    public SettingsViewModel(string? editorConfigPath, string packageBanner)
    {
        PackageBanner = packageBanner;
        var store = new EditorConfigStore(editorConfigPath, message => Status = message);
        Location = editorConfigPath is null ? "No solution open." : $"Settings are stored in {editorConfigPath}";
        var rules = Rule.LoadEmbedded().Where(r => r.Type == "realtime").ToList();
        var values = store.Read(rules.SelectMany(r => r.Options.Select(o => o.Key).Append(r.SeverityKey)));
        foreach (var rule in rules)
        {
            Rules.Add(new RuleViewModel(store, rule, values, message => Status = message));
        }
    }

    [DataMember]
    public string Location { get; }

    /// <summary>Empty when the solution references the Neolinter package (or has no C# project).</summary>
    [DataMember]
    public string PackageBanner { get; }

    [DataMember]
    public ObservableList<RuleViewModel> Rules { get; } = [];

    [DataMember]
    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }
}
