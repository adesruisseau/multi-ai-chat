using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgentGroupChat;

public abstract class ObservableEntity : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AgentProfile : ObservableEntity
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _provider = "Groq";
    private string _systemPrompt = string.Empty;
    private string _memoryNotes = string.Empty;
    private string _memoryFilePath = string.Empty;
    private string _shortTermMemory = string.Empty;
    private string _shortTermMemoryFilePath = string.Empty;
    private bool _isEnabled = true;
    private int? _maxTokensOverride;
    private int _compactionBudget = 420;
    private string _accentHex = "#C56A54";
    private string _backgroundHex = "#F9E5DE";
    private string _textToSpeechVoice = string.Empty;
    private bool _isSelectedForEdit;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Provider
    {
        get => _provider;
        set => SetProperty(ref _provider, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    public string MemoryNotes
    {
        get => _memoryNotes;
        set => SetProperty(ref _memoryNotes, value);
    }

    public string MemoryFilePath
    {
        get => _memoryFilePath;
        set => SetProperty(ref _memoryFilePath, value);
    }

    public string ShortTermMemory
    {
        get => _shortTermMemory;
        set => SetProperty(ref _shortTermMemory, value);
    }

    public string ShortTermMemoryFilePath
    {
        get => _shortTermMemoryFilePath;
        set => SetProperty(ref _shortTermMemoryFilePath, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public int? MaxTokensOverride
    {
        get => _maxTokensOverride;
        set => SetProperty(ref _maxTokensOverride, value);
    }

    public int CompactionBudget
    {
        get => _compactionBudget;
        set => SetProperty(ref _compactionBudget, value);
    }

    public string AccentHex
    {
        get => _accentHex;
        set => SetProperty(ref _accentHex, value);
    }

    public string BackgroundHex
    {
        get => _backgroundHex;
        set => SetProperty(ref _backgroundHex, value);
    }

    public string TextToSpeechVoice
    {
        get => _textToSpeechVoice;
        set => SetProperty(ref _textToSpeechVoice, value);
    }

    public bool IsSelectedForEdit
    {
        get => _isSelectedForEdit;
        set => SetProperty(ref _isSelectedForEdit, value);
    }
}

public sealed class ThemeProfile : ObservableEntity
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _topic = string.Empty;
    private bool _waitForUserReply = true;
    private int _agentDelaySeconds = 5;
    private int _maxTokens = 300;
    private int _recentTurnsWindow = 6;
    private int _userCompactionBudget = 3200;
    private string _sceneSummarizationLevel = "Moderate";
    private int _sceneSummarizerMaxTokens = 500;
    private int _sceneSummarizerMaxLines = 28;
    private int _sceneSummarizerMaxCharacters = 5600;
    private int _sceneSummarizerBroaderTranscriptTurns = 6;
    private string _sceneSummarizerPromptOverride = string.Empty;
    private string _summarizerProvider = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Topic
    {
        get => _topic;
        set => SetProperty(ref _topic, value);
    }

    public bool WaitForUserReply
    {
        get => _waitForUserReply;
        set => SetProperty(ref _waitForUserReply, value);
    }

    public int AgentDelaySeconds
    {
        get => _agentDelaySeconds;
        set => SetProperty(ref _agentDelaySeconds, value);
    }

    public int MaxTokens
    {
        get => _maxTokens;
        set => SetProperty(ref _maxTokens, value);
    }

    public int RecentTurnsWindow
    {
        get => _recentTurnsWindow;
        set => SetProperty(ref _recentTurnsWindow, value);
    }

    public int UserCompactionBudget
    {
        get => _userCompactionBudget;
        set => SetProperty(ref _userCompactionBudget, value);
    }

    public string SceneSummarizationLevel
    {
        get => _sceneSummarizationLevel;
        set => SetProperty(ref _sceneSummarizationLevel, value);
    }

    public int SceneSummarizerMaxTokens
    {
        get => _sceneSummarizerMaxTokens;
        set => SetProperty(ref _sceneSummarizerMaxTokens, value);
    }

    public int SceneSummarizerMaxLines
    {
        get => _sceneSummarizerMaxLines;
        set => SetProperty(ref _sceneSummarizerMaxLines, value);
    }

    public int SceneSummarizerMaxCharacters
    {
        get => _sceneSummarizerMaxCharacters;
        set => SetProperty(ref _sceneSummarizerMaxCharacters, value);
    }

    public int SceneSummarizerBroaderTranscriptTurns
    {
        get => _sceneSummarizerBroaderTranscriptTurns;
        set => SetProperty(ref _sceneSummarizerBroaderTranscriptTurns, value);
    }

    public string SceneSummarizerPromptOverride
    {
        get => _sceneSummarizerPromptOverride;
        set => SetProperty(ref _sceneSummarizerPromptOverride, value);
    }

    public string SummarizerProvider
    {
        get => _summarizerProvider;
        set => SetProperty(ref _summarizerProvider, value);
    }

    public ObservableCollection<AgentProfile> Agents { get; set; } = new();
}

public sealed class ThemeLibrary
{
    public string? SelectedThemeId { get; set; }

    public ObservableCollection<ThemeProfile> Themes { get; set; } = new();
}