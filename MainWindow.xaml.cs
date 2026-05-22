using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using LocalSpeechSynthesizer = System.Speech.Synthesis.SpeechSynthesizer;
using LocalSpeakCompletedEventArgs = System.Speech.Synthesis.SpeakCompletedEventArgs;

namespace AgentGroupChat;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int MaximumInMemoryLogEntries = 50;
    private const int DurableMemoryPromotionInterval = 3;
    private const int PiperMaximumSpeechChunkCharacters = 260;
    private const int PiperMaximumSpeechChunkUnits = 3;
    private static readonly TimeSpan KokoroSegmentEdgeFadeDuration = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PiperInitialPlaybackBufferDuration = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan PiperMaximumBufferedAudioDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PiperBufferPollingInterval = TimeSpan.FromMilliseconds(25);
    private sealed record SceneSummarizerProfile(
        int MaxCompletionTokens,
        int MaxLines,
        int MaxCharacters,
        int BroaderTranscriptTurns,
        string CompressionInstruction,
        string SectionBudgetInstruction);

    private readonly HttpClient _httpClient = new();
    private readonly LlmClient _llmClient;
    private readonly object _speechLock = new();
    private readonly ConnectionSettingsStore _connectionSettingsStore = new();
    private readonly SessionLogStore _sessionLogStore = new();
    private readonly ThemeStore _themeStore = new();
    private readonly Dictionary<string, string> _aiModelNameSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly CollectionViewSource _filteredAiModelsSource = new();
    private readonly List<SessionTurn> _sessionTurns = new();
    private Task _speechQueue = Task.CompletedTask;
    private ThemeLibrary _themeLibrary = new();
    private ChatMessage? _activeSpeechMessage;
    private string _defaultTextToSpeechVoice = string.Empty;
    private WaveOutEvent? _kokoroWaveOut;
    private BufferedWaveProvider? _kokoroBufferedWaveProvider;
    private WaveFormat? _kokoroWaveFormat;
    private Process? _piperProcess;
    private WaveOutEvent? _piperWaveOut;
    private string _textToSpeechProviderSelection = "Local";
    private bool _isRunning;
    private bool _isInitializing;
    private bool _isSpeechActive;
    private bool _isSpeechPaused;
    private Task? _activeRunTask;
    private CancellationTokenSource? _runCancellationTokenSource;
    private CancellationTokenSource? _transcriptReplayCancellationTokenSource;
    private LocalSpeechSynthesizer? _speechSynthesizer;
    private double _chatFontSize = 17;
    private bool _appearanceSyncSubscribed;
    private int _completedConversationRounds;
    private string _connectionSettingsPath = string.Empty;
    private string _connectionSettingsSummary = string.Empty;
    private string _sharedRoomMemory = string.Empty;
    private string _uiAccentSelection = "Terracotta";
    private string _uiThemeSelection = "System";
    private string _selectedLogSource = "All sources";
    private Visibility _setupGuideVisibility = Visibility.Visible;
    private bool _showSetupGuideOnDemand;
    private bool _isSetupGuideModelsCompleted;
    private bool _isSetupGuideRoomsCompleted;
    private string _setupGuideTtsStatus = "Pending";
    private string _summarizerProviderSelection = "Gemini";
    private string _themeDurableMemory = string.Empty;
    private bool _textToSpeechEnabled;
    private int _textToSpeechRate;
    private string _runButtonText = "Run";
    private string _sessionLogPath = string.Empty;
    private ConnectionSettings _loadedConnectionSettings = new();
    private bool _isApplyingSetupGuideState;
    private bool _isApplyingSettings;
    private ThemeProfile? _selectedTheme;
    private AgentProfile? _selectedAgent;
    private AiConnectionProfile? _selectedAiConnection;
    private AiModelCatalogEntry? _selectedAiModel;
    private bool _showMemoryLogs = true;
    private bool _showSpeechLogs = false;
    private bool _showRequestLogs = true;
    private bool _showResponseLogs = true;
    private bool _showSystemLogs = true;
    private bool _showTimingLogs = true;

    public MainWindow()
    {
        InitializeComponent();
        _llmClient = new LlmClient(_httpClient);
        Messages = new ObservableCollection<ChatMessage>();
        Logs = new ObservableCollection<LogEntry>();
        LogSources = new ObservableCollection<string> { "All sources" };
        AvailableTextToSpeechVoices = new ObservableCollection<string>();
        FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
        FilteredLogs.Filter = FilterLogEntry;
        Themes = new ObservableCollection<ThemeProfile>();
        AiConnectionProfiles = new ObservableCollection<AiConnectionProfile>();
        AiModels = new ObservableCollection<AiModelCatalogEntry>();
        _filteredAiModelsSource.Source = AiModels;
        FilteredAiModels = _filteredAiModelsSource.View;
        FilteredAiModels.Filter = FilterAiModelEntry;
        AiConnectionProfiles.CollectionChanged += AiConnectionProfiles_CollectionChanged;
        AiModels.CollectionChanged += AiModels_CollectionChanged;
        DataContext = this;
        AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(HandleLoadedElementForAppearance), true);
        SubscribeToAppearanceSync();

        LoadTextToSpeechOptions();
        ConnectionSettingsPath = _connectionSettingsStore.SettingsPath;
        SessionLogPath = _sessionLogStore.SessionLogPath;
        RefreshConnectionSettingsSummary();
        LoadThemes();
        AddSystemMessage($"Ready. Pick a theme, configure the agent order, and run the room. Session log: {SessionLogPath}");
    }

    public ObservableCollection<ChatMessage> Messages { get; }

    public ObservableCollection<LogEntry> Logs { get; }

    public ObservableCollection<string> LogSources { get; }

    public ObservableCollection<string> AvailableTextToSpeechVoices { get; }

    public ICollectionView FilteredLogs { get; }

    public ObservableCollection<ThemeProfile> Themes { get; }

    public ObservableCollection<AiConnectionProfile> AiConnectionProfiles { get; }

    public ObservableCollection<AiModelCatalogEntry> AiModels { get; }

    public ICollectionView FilteredAiModels { get; }

    public IReadOnlyList<string> AvailableAiConnectionTransports { get; } = ConnectionSettingsStore.AvailableTransportNames;

    public IReadOnlyList<string> AvailableTextToSpeechProviders { get; } = new[] { "Local", "Piper", "Kokoro" };

    public IReadOnlyList<string> AvailableUiThemes { get; } = UiThemeManager.AvailableThemes;

    public IReadOnlyList<string> AvailableUiAccents { get; } = UiThemeManager.AvailableAccents;

    public IReadOnlyList<string> AvailableSceneSummarizationLevels { get; } = new[]
    {
        "Aggressive",
        "Semi-Aggressive",
        "Moderate",
        "Semi-Relaxed",
        "Relaxed",
        "Custom",
    };

    public double ChatFontSize
    {
        get => _chatFontSize;
        set => SetProperty(ref _chatFontSize, value);
    }

    public bool HasTextToSpeechVoices => AvailableTextToSpeechVoices.Count > 0;

    public string ConnectionSettingsPath
    {
        get => _connectionSettingsPath;
        private set => SetProperty(ref _connectionSettingsPath, value);
    }

    public string ConnectionSettingsSummary
    {
        get => _connectionSettingsSummary;
        private set => SetProperty(ref _connectionSettingsSummary, value);
    }

    public string SessionLogPath
    {
        get => _sessionLogPath;
        private set => SetProperty(ref _sessionLogPath, value);
    }

    public Visibility SetupGuideVisibility
    {
        get => _setupGuideVisibility;
        private set => SetProperty(ref _setupGuideVisibility, value);
    }

    public bool IsSetupGuideModelsCompleted
    {
        get => _isSetupGuideModelsCompleted;
        private set => SetProperty(ref _isSetupGuideModelsCompleted, value);
    }

    public bool IsSetupGuideRoomsCompleted
    {
        get => _isSetupGuideRoomsCompleted;
        private set => SetProperty(ref _isSetupGuideRoomsCompleted, value);
    }

    public string SetupGuideTtsStatus
    {
        get => _setupGuideTtsStatus;
        private set => SetProperty(ref _setupGuideTtsStatus, value);
    }

    public bool IsSetupGuideTtsCompleted => SetupGuideTtsStatus is "Configured" or "Skipped";

    public bool IsSetupGuideComplete => IsSetupGuideModelsCompleted && IsSetupGuideRoomsCompleted && IsSetupGuideTtsCompleted;

    public int SetupGuideCompletedStepCount =>
        (IsSetupGuideModelsCompleted ? 1 : 0)
        + (IsSetupGuideRoomsCompleted ? 1 : 0)
        + (IsSetupGuideTtsCompleted ? 1 : 0);

    public string SetupGuideProgressText => $"{SetupGuideCompletedStepCount} of 3 setup steps complete";

    public string SetupGuideModelsChecklistText => IsSetupGuideModelsCompleted
        ? "[x] AI models configured"
        : "[ ] AI models not confirmed yet";

    public string SetupGuideRoomsChecklistText => IsSetupGuideRoomsCompleted
        ? "[x] Room setup complete"
        : "[ ] Room setup not confirmed yet";

    public string SetupGuideTtsChecklistText => SetupGuideTtsStatus switch
    {
        "Configured" => "[x] TTS configured",
        "Skipped" => "[x] TTS skipped for now",
        _ => "[ ] TTS still pending (optional)",
    };

    public string SetupGuideCompletionMessage => IsSetupGuideComplete
        ? "Setup is complete. The guide will hide itself from now on, but you can reopen it with the ? button in the header."
        : "The guide stays visible until models, rooms, and the optional TTS decision have each been completed once.";

    public bool CanSkipSetupGuideTts => !IsSetupGuideTtsCompleted;

    public bool TextToSpeechEnabled
    {
        get => _textToSpeechEnabled;
        set
        {
            if (!SetProperty(ref _textToSpeechEnabled, value))
            {
                return;
            }

            PersistTextToSpeechSettings();
            if (!value)
            {
                StopSpeaking();
            }
        }
    }

    public string TextToSpeechProviderSelection
    {
        get => _textToSpeechProviderSelection;
        set
        {
            var normalized = NormalizeTextToSpeechProvider(value);
            if (!SetProperty(ref _textToSpeechProviderSelection, normalized))
            {
                return;
            }

            StopSpeaking();
            PersistTextToSpeechSettings();
            LoadTextToSpeechOptions();
        }
    }

    public int TextToSpeechRate
    {
        get => _textToSpeechRate;
        set
        {
            var clamped = Math.Clamp(value, -5, 5);
            if (!SetProperty(ref _textToSpeechRate, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(TextToSpeechRateLabel));
            PersistTextToSpeechSettings();
        }
    }

    public string TextToSpeechRateLabel => TextToSpeechRate >= 0
        ? $"+{TextToSpeechRate}"
        : TextToSpeechRate.ToString();

    public string SummarizerProviderSelection
    {
        get => _summarizerProviderSelection;
        set
        {
            if (_isApplyingSettings)
            {
                return;
            }

            var currentSettings = CreateCurrentModelCatalogSnapshot();
            var normalized = NormalizeModelSelection(value, currentSettings);
            if (!SetProperty(ref _summarizerProviderSelection, normalized))
            {
                return;
            }

            if (TryPersistAiSettings(out var errorMessage))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                ConnectionSettingsSummary = errorMessage;
            }
        }
    }

    public AiConnectionProfile? SelectedAiConnection
    {
        get => _selectedAiConnection;
        set
        {
            if (!SetProperty(ref _selectedAiConnection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedAiConnectionDisplayName));
            RefreshFilteredAiModels();
        }
    }

    public AiModelCatalogEntry? SelectedAiModel
    {
        get => _selectedAiModel;
        set => SetProperty(ref _selectedAiModel, value);
    }

    public string SelectedAiConnectionDisplayName => SelectedAiConnection?.Name ?? "All services";

    public string SharedRoomMemoryDisplay => string.IsNullOrWhiteSpace(_sharedRoomMemory)
        ? "(No shared room memory yet.)"
        : _sharedRoomMemory;

    public string ThemeLibraryPath => _themeStore.ThemesPath;

    public string SelectedThemeSharedRoomMemoryPath => SelectedTheme is null
        ? string.Empty
        : _themeStore.GetThemeSharedRoomMemoryPath(SelectedTheme);

    public string SelectedThemeConversationPath => SelectedTheme is null
        ? string.Empty
        : _themeStore.GetThemeConversationPath(SelectedTheme);

    public string SelectedLogSource
    {
        get => _selectedLogSource;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "All sources" : value;
            if (SetProperty(ref _selectedLogSource, normalized))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowSystemLogs
    {
        get => _showSystemLogs;
        set
        {
            if (SetProperty(ref _showSystemLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowSpeechLogs
    {
        get => _showSpeechLogs;
        set
        {
            if (SetProperty(ref _showSpeechLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowTimingLogs
    {
        get => _showTimingLogs;
        set
        {
            if (SetProperty(ref _showTimingLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowRequestLogs
    {
        get => _showRequestLogs;
        set
        {
            if (SetProperty(ref _showRequestLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowResponseLogs
    {
        get => _showResponseLogs;
        set
        {
            if (SetProperty(ref _showResponseLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public bool ShowMemoryLogs
    {
        get => _showMemoryLogs;
        set
        {
            if (SetProperty(ref _showMemoryLogs, value))
            {
                RefreshLogFilters();
            }
        }
    }

    public string ThemeDurableMemoryDisplay => string.IsNullOrWhiteSpace(_themeDurableMemory)
        ? "(No durable memory yet.)"
        : _themeDurableMemory;

    public string SelectedThemeDurableMemoryPath => SelectedTheme is null
        ? string.Empty
        : _themeStore.GetThemeDurableMemoryPath(SelectedTheme);

    public string RunButtonText
    {
        get => _runButtonText;
        private set => SetProperty(ref _runButtonText, value);
    }

    public string PauseButtonText => _isSpeechPaused ? "RESUME" : "PAUSE";

    public bool CanPauseSpeech => _isSpeechActive || _isSpeechPaused;

    public string SelectedThemeSummarizerProvider
    {
        get => SelectedTheme?.SummarizerProvider ?? string.Empty;
        set
        {
            if (SelectedTheme is null || _isApplyingSettings)
            {
                return;
            }

            var currentSettings = CreateCurrentModelCatalogSnapshot();
            var normalized = NormalizeModelSelection(value, currentSettings);
            if (string.Equals(SelectedTheme.SummarizerProvider, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SelectedTheme.SummarizerProvider = normalized;
            PersistCurrentConfiguration();
        }
    }

    public string SelectedThemeSceneSummarizationLevel
    {
        get => SelectedTheme?.SceneSummarizationLevel ?? "Moderate";
        set
        {
            if (SelectedTheme is null)
            {
                return;
            }

            var normalized = NormalizeSceneSummarizationLevel(value);
            if (string.Equals(SelectedTheme.SceneSummarizationLevel, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SelectedTheme.SceneSummarizationLevel = normalized;
            RefreshSelectedThemeSummarizerEditor();
        }
    }

    public bool CanEditSelectedThemeSummarizerSettings => SelectedTheme is not null
        && string.Equals(SelectedTheme.SceneSummarizationLevel, "Custom", StringComparison.Ordinal);

    public int SelectedThemeSceneSummarizerMaxTokens
    {
        get
        {
            if (SelectedTheme is null)
            {
                return 0;
            }

            if (CanEditSelectedThemeSummarizerSettings)
            {
                return SelectedTheme.SceneSummarizerMaxTokens;
            }

            return GetSceneSummarizerProfile(SelectedTheme, SelectedTheme.RecentTurnsWindow).MaxCompletionTokens;
        }
        set
        {
            if (SelectedTheme is null || !CanEditSelectedThemeSummarizerSettings)
            {
                return;
            }

            SelectedTheme.SceneSummarizerMaxTokens = value;
        }
    }

    public int SelectedThemeSceneSummarizerMaxLines
    {
        get
        {
            if (SelectedTheme is null)
            {
                return 0;
            }

            if (CanEditSelectedThemeSummarizerSettings)
            {
                return SelectedTheme.SceneSummarizerMaxLines;
            }

            return GetSceneSummarizerProfile(SelectedTheme, SelectedTheme.RecentTurnsWindow).MaxLines;
        }
        set
        {
            if (SelectedTheme is null || !CanEditSelectedThemeSummarizerSettings)
            {
                return;
            }

            SelectedTheme.SceneSummarizerMaxLines = value;
        }
    }

    public int SelectedThemeSceneSummarizerBroaderTranscriptTurns
    {
        get
        {
            if (SelectedTheme is null)
            {
                return 0;
            }

            if (CanEditSelectedThemeSummarizerSettings)
            {
                return SelectedTheme.SceneSummarizerBroaderTranscriptTurns;
            }

            return GetSceneSummarizerProfile(SelectedTheme, SelectedTheme.RecentTurnsWindow).BroaderTranscriptTurns;
        }
        set
        {
            if (SelectedTheme is null || !CanEditSelectedThemeSummarizerSettings)
            {
                return;
            }

            SelectedTheme.SceneSummarizerBroaderTranscriptTurns = value;
        }
    }

    public int SelectedThemeSceneSummarizerMaxCharacters
    {
        get
        {
            if (SelectedTheme is null)
            {
                return 0;
            }

            if (CanEditSelectedThemeSummarizerSettings)
            {
                return SelectedTheme.SceneSummarizerMaxCharacters;
            }

            return GetSceneSummarizerProfile(SelectedTheme, SelectedTheme.RecentTurnsWindow).MaxCharacters;
        }
        set
        {
            if (SelectedTheme is null || !CanEditSelectedThemeSummarizerSettings)
            {
                return;
            }

            SelectedTheme.SceneSummarizerMaxCharacters = value;
        }
    }

    public string SelectedThemeSummarizerAgentPrompt
    {
        get
        {
            if (SelectedTheme is null)
            {
                return string.Empty;
            }

            var profile = GetSceneSummarizerProfile(SelectedTheme, SelectedTheme.RecentTurnsWindow);
            return BuildSharedRoomMemorySummarizerSystemPrompt(SelectedTheme, profile);
        }
        set
        {
            if (SelectedTheme is null || !CanEditSelectedThemeSummarizerSettings)
            {
                return;
            }

            SelectedTheme.SceneSummarizerPromptOverride = value;
        }
    }

    public string SelectedThemeSummarizerEditorHelpText => CanEditSelectedThemeSummarizerSettings
        ? "Custom mode unlocks the numeric limits and the full summarizer system prompt. Keep the <shared_room_memory> tags so the parser can still extract the result."
        : "Preset levels use the built-in summarizer prompt and preset limits. Switch to Custom if you want to edit the numeric limits or the full summarizer system prompt.";

    public ThemeProfile? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var previousTheme = _selectedTheme;
            var previousThemeId = _selectedTheme?.Id;
            if (SetProperty(ref _selectedTheme, value))
            {
                if (previousTheme is not null)
                {
                    previousTheme.PropertyChanged -= SelectedTheme_PropertyChanged;
                }

                if (_selectedTheme is not null)
                {
                    _selectedTheme.PropertyChanged += SelectedTheme_PropertyChanged;
                }

                OnPropertyChanged(nameof(SelectedThemeConversationPath));
                OnPropertyChanged(nameof(SelectedThemeSharedRoomMemoryPath));
                OnPropertyChanged(nameof(SelectedThemeDurableMemoryPath));

                if (!_isInitializing)
                {
                    if (!string.Equals(previousThemeId, _selectedTheme?.Id, StringComparison.Ordinal))
                    {
                        PersistConversationSession(previousTheme);
                        ResetConversationSession();
                    }

                    PersistCurrentConfiguration();
                }

                if (_selectedTheme is not null)
                {
                    EnsureThemePrepared(_selectedTheme);
                    SelectedAgent = _selectedTheme.Agents.FirstOrDefault();
                    _themeLibrary.SelectedThemeId = _selectedTheme.Id;
                    LoadConversationSession(_selectedTheme);
                    RefreshSelectedThemeSummarizerEditor();
                }
                else
                {
                    _sharedRoomMemory = string.Empty;
                    _themeDurableMemory = string.Empty;
                    OnPropertyChanged(nameof(SharedRoomMemoryDisplay));
                    OnPropertyChanged(nameof(ThemeDurableMemoryDisplay));
                    RefreshSelectedThemeSummarizerEditor();
                    SelectedAgent = null;
                }
            }
        }
    }

    private void SelectedTheme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ThemeProfile.Name), StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedThemeConversationPath));
        OnPropertyChanged(nameof(SelectedThemeSharedRoomMemoryPath));
        OnPropertyChanged(nameof(SelectedThemeDurableMemoryPath));

        if (SelectedTheme is null)
        {
            return;
        }

        var sharedRoomMemoryPath = _themeStore.GetThemeSharedRoomMemoryPath(SelectedTheme);
        foreach (var agent in SelectedTheme.Agents)
        {
            agent.ShortTermMemoryFilePath = sharedRoomMemoryPath;
        }
    }

    public AgentProfile? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (ReferenceEquals(_selectedAgent, value))
            {
                return;
            }

            if (!_isInitializing)
            {
                PersistCurrentAgentMemory();
            }

            if (_selectedAgent is not null)
            {
                _selectedAgent.IsSelectedForEdit = false;
            }

            _selectedAgent = value;
            if (_selectedAgent is not null && SelectedTheme is not null)
            {
                _selectedAgent.IsSelectedForEdit = true;
                _selectedAgent.ShortTermMemory = _themeStore.LoadThemeSharedRoomMemory(SelectedTheme);
                _selectedAgent.ShortTermMemoryFilePath = _themeStore.GetThemeSharedRoomMemoryPath(SelectedTheme);
            }

            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void RunButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelTranscriptReplay();
        _activeRunTask = RunWorkflowAsync();

        try
        {
            await _activeRunTask;
        }
        finally
        {
            _activeRunTask = null;
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelTranscriptReplay();
        _runCancellationTokenSource?.Cancel();
        StopSpeaking();
        StatusTextBlock.Text = "Stopping";
    }

    private void PauseSpeechButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryToggleSpeechPause(out var isPaused, out var feedbackMessage))
        {
            if (!string.IsNullOrWhiteSpace(feedbackMessage))
            {
                AddSystemMessage(feedbackMessage);
            }

            return;
        }

        StatusTextBlock.Text = isPaused ? "Speech paused" : StatusTextBlock.Text;
    }

    private async void ReplayChatMessageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessage message })
        {
            return;
        }

        await ReplayTranscriptFromMessageAsync(message);
    }

    private void CopyChatMessageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage message })
        {
            Clipboard.SetText(BuildCopiedChatMessageText(message));
        }
    }

    private void CancelTranscriptReplay()
    {
        _transcriptReplayCancellationTokenSource?.Cancel();
        _transcriptReplayCancellationTokenSource?.Dispose();
        _transcriptReplayCancellationTokenSource = null;
    }

    private void CopyLogEntryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LogEntry entry })
        {
            Clipboard.SetText(BuildCopiedLogEntryText(entry));
        }
    }

    private void OpenPathInExplorerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaggedPath(sender, out var path))
        {
            AddSystemMessage("No file path is available yet.");
            return;
        }

        try
        {
            OpenPathInExplorer(path);
        }
        catch (Exception exception)
        {
            AddSystemMessage($"Unable to open the path in Explorer: {exception.Message}");
        }
    }

    private void CopyPathButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaggedPath(sender, out var path))
        {
            AddSystemMessage("No file path is available yet.");
            return;
        }

        try
        {
            Clipboard.SetText(path);
        }
        catch (Exception exception)
        {
            AddSystemMessage($"Unable to copy the path: {exception.Message}");
        }
    }

    private static bool TryGetTaggedPath(object sender, out string path)
    {
        path = (sender as FrameworkElement)?.Tag?.ToString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static void OpenPathInExplorer(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        ProcessStartInfo startInfo;

        if (File.Exists(normalizedPath))
        {
            startInfo = new ProcessStartInfo("explorer.exe", $"/select,\"{normalizedPath}\"");
        }
        else if (Directory.Exists(normalizedPath))
        {
            startInfo = new ProcessStartInfo("explorer.exe", $"\"{normalizedPath}\"");
        }
        else
        {
            var directoryPath = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"No existing file or folder was found for '{normalizedPath}'.");
            }

            startInfo = new ProcessStartInfo("explorer.exe", $"\"{directoryPath}\"");
        }

        startInfo.UseShellExecute = true;
        Process.Start(startInfo);
    }

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectTabByHeader("Logs");

        ScrollLogsToEnd();
    }

    private void ShowSetupGuideButton_OnClick(object sender, RoutedEventArgs e)
    {
        _showSetupGuideOnDemand = true;
        UpdateSetupGuideVisibility();
        SelectTabByHeader("Setup Guide");
    }

    private void SelectThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ThemeProfile theme })
        {
            SelectedTheme = theme;
        }
    }

    private void SelectTabByHeader(string tabHeader)
    {
        if (MainTabControl.Items.OfType<TabItem>().FirstOrDefault(item => string.Equals(item.Header?.ToString(), tabHeader, StringComparison.Ordinal)) is { } tab)
        {
            MainTabControl.SelectedItem = tab;
        }
    }

    private void SelectAgentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AgentProfile agent })
        {
            SelectedAgent = agent;
        }
    }

    private void AgentExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        if (sender is FrameworkElement { Tag: AgentProfile agent } && !ReferenceEquals(SelectedAgent, agent))
        {
            SelectedAgent = agent;
        }
    }

    private void AgentExpander_OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        if (sender is FrameworkElement { Tag: AgentProfile agent } && ReferenceEquals(SelectedAgent, agent))
        {
            SelectedAgent = null;
        }
    }

    private async void PromptTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await RunWorkflowAsync();
        }
    }

    private void NewThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistCurrentConfiguration();

        var theme = new ThemeProfile
        {
            Name = "New Theme",
            Topic = "Describe the purpose of this room.",
            Agents = new ObservableCollection<AgentProfile>
            {
                CreateAgent("Agent 1", "Define this agent's behavior here.", 0),
            },
        };

        Themes.Add(theme);
        _themeLibrary.SelectedThemeId = theme.Id;
        EnsureThemePrepared(theme);
        SelectedTheme = theme;
        PersistCurrentConfiguration();
        MarkSetupGuideRoomsCompleted();
        AddSystemMessage($"Created theme '{theme.Name}'.");
    }

    private void DeleteThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null)
        {
            return;
        }

        if (Themes.Count == 1)
        {
            MessageBox.Show(this, "Keep at least one theme in the library.", "Cannot delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var themeToRemove = SelectedTheme;
        var selectedIndex = Themes.IndexOf(themeToRemove);
        Themes.Remove(themeToRemove);
        SelectedTheme = Themes[Math.Max(0, selectedIndex - 1)];
        PersistCurrentConfiguration();
        AddSystemMessage($"Deleted theme '{themeToRemove.Name}'.");
    }

    private void AddAgentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null)
        {
            return;
        }

        if (SelectedTheme.Agents.Count >= 4)
        {
            MessageBox.Show(this, "A theme can have at most 4 agents.", "Agent limit reached", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var agent = CreateAgent($"Agent {SelectedTheme.Agents.Count + 1}", "Define this agent's behavior here.", SelectedTheme.Agents.Count);
        SelectedTheme.Agents.Add(agent);
        EnsureThemePrepared(SelectedTheme);
        SelectedAgent = agent;
        PersistCurrentConfiguration();
        MarkSetupGuideRoomsCompleted();
        AddSystemMessage($"Added {agent.Name} to {SelectedTheme.Name}.");
    }

    private void RemoveAgentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null || SelectedAgent is null)
        {
            return;
        }

        if (SelectedTheme.Agents.Count == 1)
        {
            MessageBox.Show(this, "Keep at least one agent in the theme.", "Cannot remove", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = SelectedTheme.Agents.IndexOf(SelectedAgent);
        var removedName = SelectedAgent.Name;
        SelectedTheme.Agents.Remove(SelectedAgent);
        EnsureThemePrepared(SelectedTheme);
        SelectedAgent = SelectedTheme.Agents[Math.Max(0, index - 1)];
        PersistCurrentConfiguration();
        AddSystemMessage($"Removed {removedName} from {SelectedTheme.Name}.");
    }

    private void MoveAgentUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        MoveSelectedAgent(-1);
    }

    private void MoveAgentDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        MoveSelectedAgent(1);
    }

    private void AddAiConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var (name, transport, endpoint) = GetNextConnectionTemplate();
        var connection = new AiConnectionProfile
        {
            Name = name,
            Transport = transport,
            Endpoint = endpoint,
        };

        AiConnectionProfiles.Add(connection);
        SelectedAiConnection = connection;
        RefreshAiModelConnectionNames();
    }

    private void RemoveAiConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAiConnection is null)
        {
            return;
        }

        if (AiConnectionProfiles.Count == 1)
        {
            MessageBox.Show(this, "Keep at least one AI connection in the catalog.", "Cannot remove connection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dependentModels = AiModels
            .Where(model => string.Equals(model.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase))
            .Select(model => model.Name)
            .ToList();

        if (dependentModels.Count > 0)
        {
            MessageBox.Show(
                this,
                $"Move or remove these models first: {string.Join(", ", dependentModels)}",
                "Connection still in use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var removedId = SelectedAiConnection.Id;
        AiConnectionProfiles.Remove(SelectedAiConnection);
        SelectedAiConnection = AiConnectionProfiles.FirstOrDefault();

        if (SelectedAiModel is not null && string.Equals(SelectedAiModel.ConnectionId, removedId, StringComparison.OrdinalIgnoreCase))
        {
            SelectedAiModel = AiModels.FirstOrDefault();
        }

        RefreshAiModelConnectionNames();
    }

    private void AddAiModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var connectionId = SelectedAiConnection?.Id ?? AiConnectionProfiles.FirstOrDefault()?.Id ?? string.Empty;
        var model = new AiModelCatalogEntry
        {
            Name = GetNextModelName(),
            ConnectionId = connectionId,
            ModelId = string.Empty,
        };

        AiModels.Add(model);
        RefreshAiModelConnectionNames();
        SelectedAiModel = model;
        RefreshSetupGuideProgress(autoFinalize: false);
    }

    private void RemoveAiModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAiModel is null)
        {
            return;
        }

        if (AiModels.Count == 1)
        {
            MessageBox.Show(this, "Keep at least one AI model in the catalog.", "Cannot remove model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (TryDescribeModelUsage(SelectedAiModel.Name, out var usageDescription))
        {
            MessageBox.Show(
                this,
                $"Reassign the model before deleting it. Current usage: {usageDescription}",
                "Model still in use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        AiModels.Remove(SelectedAiModel);
        SelectedAiModel = AiModels.FirstOrDefault();
    }

    private void SaveAiSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryPersistAiSettings(out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "AI settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MarkSetupGuideModelsCompleted();
        AddSystemMessage("AI model catalog saved.");
    }

    private async void TestAiModelConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAiModel is null)
        {
            MessageBox.Show(this, "Select a model to test.", "No model selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryPersistAiSettings(out var errorMessage))
        {
            MessageBox.Show(this, errorMessage, "AI settings not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousStatus = StatusTextBlock.Text;
        StatusTextBlock.Text = $"Testing {SelectedAiModel.Name}";

        try
        {
            var settings = _connectionSettingsStore.Load();
            ApplyLoadedConnectionSettings(settings);

            var requestSettings = BuildRequestSettings(settings, SelectedAiModel.Name, 80);
            var response = await CompleteWithLoggingAsync(
                "ModelTest",
                requestSettings,
                new[]
                {
                    new LlmChatMessage("user", "Reply with exactly: CONNECTION OK"),
                },
                CancellationToken.None);

            var preview = response.Length > 180 ? response[..180] + "..." : response;
            AddSystemMessage($"Model test succeeded for {SelectedAiModel.Name}. Response: {preview}");
            MessageBox.Show(
                this,
                $"Connection succeeded for '{SelectedAiModel.Name}'.\n\nResponse: {preview}",
                "Model test passed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            MarkSetupGuideModelsCompleted();
        }
        catch (Exception exception)
        {
            AddSystemMessage($"Model test failed for {SelectedAiModel.Name}: {exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "Model test failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            StatusTextBlock.Text = previousStatus;
        }
    }

    private void SaveThemeButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistCurrentConfiguration();
        MarkSetupGuideRoomsCompleted();
        AddSystemMessage("Theme library saved.");
    }

    private async void ClearChatButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelTranscriptReplay();
        _runCancellationTokenSource?.Cancel();
        StopSpeaking();

        if (_activeRunTask is not null)
        {
            try
            {
                await _activeRunTask;
            }
            catch
            {
            }
        }

        ResetConversationSession();
        PersistConversationSession(SelectedTheme);
        StatusTextBlock.Text = "Ready";
        AddSystemMessage("Chat cleared.");
    }

    private void ClearAllAgentMemoriesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null)
        {
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Clear shared room and durable memory for '{SelectedTheme.Name}'?",
            "Clear shared room memory",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var agent in SelectedTheme.Agents)
        {
            agent.ShortTermMemory = string.Empty;
        }

        _themeStore.SaveThemeSharedRoomMemory(SelectedTheme, string.Empty);
        _themeStore.SaveThemeDurableMemory(SelectedTheme, string.Empty);
        LoadAutomaticThemeMemory(SelectedTheme);

        if (SelectedAgent is not null)
        {
            SelectedAgent.ShortTermMemory = string.Empty;
        }

        AddSystemMessage($"Cleared shared room memory for {SelectedTheme.Name}.");
    }

    private void SanitizeShortTermMemoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null || SelectedAgent is null)
        {
            return;
        }

        ApplySharedRoomMemory(SelectedTheme, SanitizeStructuredMemoryBlock(SelectedAgent.ShortTermMemory, 28, 6200));
        AddSystemMessage($"Sanitized shared room memory for {SelectedTheme.Name}.");
    }

    private void ClearShortTermMemoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedTheme is null || SelectedAgent is null)
        {
            return;
        }

        ApplySharedRoomMemory(SelectedTheme, string.Empty);
        AddSystemMessage($"Cleared shared room memory for {SelectedTheme.Name}.");
    }

    private void LoadThemes()
    {
        _isInitializing = true;
        _themeLibrary = _themeStore.Load();
        Themes.Clear();

        foreach (var theme in _themeLibrary.Themes)
        {
            EnsureThemePrepared(theme);
            Themes.Add(theme);
        }

        SelectedTheme = Themes.FirstOrDefault(theme => theme.Id == _themeLibrary.SelectedThemeId) ?? Themes.FirstOrDefault();
        _isInitializing = false;
        RefreshSetupGuideProgress(autoFinalize: true);
    }

    private void EnsureThemePrepared(ThemeProfile theme)
    {
        var currentSettings = CreateCurrentModelCatalogSnapshot();
        var roomMemoryPath = _themeStore.GetThemeSharedRoomMemoryPath(theme);

        if (string.IsNullOrWhiteSpace(theme.SummarizerProvider))
        {
            theme.SummarizerProvider = NormalizeModelSelection(
                _loadedConnectionSettings.SummarizerProvider, currentSettings);
        }
        else
        {
            theme.SummarizerProvider = NormalizeModelSelection(theme.SummarizerProvider, currentSettings);
        }

        for (var index = 0; index < theme.Agents.Count; index++)
        {
            var agent = theme.Agents[index];
            agent.Provider = NormalizeModelSelection(
                string.IsNullOrWhiteSpace(agent.Provider) ? "Groq Fast" : agent.Provider,
                currentSettings);

            if (string.IsNullOrWhiteSpace(agent.AccentHex) || string.IsNullOrWhiteSpace(agent.BackgroundHex))
            {
                var seeded = CreateAgent(agent.Name, agent.SystemPrompt, index);
                agent.AccentHex = seeded.AccentHex;
                agent.BackgroundHex = seeded.BackgroundHex;
            }

            agent.ShortTermMemoryFilePath = roomMemoryPath;
        }

        if (ReferenceEquals(SelectedTheme, theme))
        {
            LoadAutomaticThemeMemory(theme);
        }
    }

    private void PersistCurrentConfiguration()
    {
        if (_isInitializing)
        {
            return;
        }

        PersistCurrentAgentMemory();
        _themeLibrary.SelectedThemeId = SelectedTheme?.Id;
        _themeLibrary.Themes = Themes;
        _themeStore.Save(_themeLibrary);
    }

    private void RefreshSelectedThemeSummarizerEditor()
    {
        OnPropertyChanged(nameof(SelectedThemeSummarizerProvider));
        OnPropertyChanged(nameof(SelectedThemeSceneSummarizationLevel));
        OnPropertyChanged(nameof(CanEditSelectedThemeSummarizerSettings));
        OnPropertyChanged(nameof(SelectedThemeSceneSummarizerMaxTokens));
        OnPropertyChanged(nameof(SelectedThemeSceneSummarizerMaxLines));
        OnPropertyChanged(nameof(SelectedThemeSceneSummarizerBroaderTranscriptTurns));
        OnPropertyChanged(nameof(SelectedThemeSceneSummarizerMaxCharacters));
        OnPropertyChanged(nameof(SelectedThemeSummarizerAgentPrompt));
        OnPropertyChanged(nameof(SelectedThemeSummarizerEditorHelpText));
    }

    private void PersistCurrentAgentMemory()
    {
        if (_isInitializing || SelectedTheme is null || SelectedAgent is null)
        {
            return;
        }

        ApplySharedRoomMemory(SelectedTheme, SelectedAgent.ShortTermMemory);
    }

    private void MoveSelectedAgent(int offset)
    {
        if (SelectedTheme is null || SelectedAgent is null)
        {
            return;
        }

        var currentIndex = SelectedTheme.Agents.IndexOf(SelectedAgent);
        var nextIndex = currentIndex + offset;
        if (nextIndex < 0 || nextIndex >= SelectedTheme.Agents.Count)
        {
            return;
        }

        SelectedTheme.Agents.Move(currentIndex, nextIndex);
        EnsureThemePrepared(SelectedTheme);
        SelectedAgent = SelectedTheme.Agents[nextIndex];
        PersistCurrentConfiguration();
    }

    private void RefreshConnectionSettingsSummary()
    {
        ConnectionSettingsPath = _connectionSettingsStore.SettingsPath;

        try
        {
            ApplyLoadedConnectionSettings(_connectionSettingsStore.Load());
        }
        catch (Exception exception)
        {
            ConnectionSettingsSummary = exception.Message;
        }
    }

    private void ApplyLoadedConnectionSettings(ConnectionSettings settings)
    {
        var previouslySelectedConnectionId = SelectedAiConnection?.Id;
        var previouslySelectedModelId = SelectedAiModel?.Id;

        _isApplyingSettings = true;
        _loadedConnectionSettings = settings;
        ApplyTextToSpeechSettings(settings);
        ApplyAppearanceSettings(settings);

        // Snapshot agent providers before clearing AiModels — the agent ComboBoxes
        // are bound to AiModels with TwoWay, so Clear() causes WPF to write null
        // back to agent.Provider through the binding.
        var agentProviderSnapshot = new Dictionary<object, string>();
        foreach (var theme in Themes)
        {
            foreach (var agent in theme.Agents)
            {
                agentProviderSnapshot[agent] = agent.Provider ?? string.Empty;
            }
        }

        AiConnectionProfiles.Clear();
        foreach (var connection in settings.AiConnections)
        {
            AiConnectionProfiles.Add(connection);
        }

        AiModels.Clear();
        foreach (var model in settings.AiModels)
        {
            AiModels.Add(model);
        }

        // Restore agent providers that were corrupted by ComboBox binding write-backs.
        foreach (var theme in Themes)
        {
            foreach (var agent in theme.Agents)
            {
                if (agentProviderSnapshot.TryGetValue(agent, out var savedProvider))
                {
                    agent.Provider = savedProvider;
                }
            }
        }

        RefreshAiModelConnectionNames();
        CaptureAiModelNameSnapshots();

        SelectedAiConnection = AiConnectionProfiles.FirstOrDefault(connection => string.Equals(connection.Id, previouslySelectedConnectionId, StringComparison.OrdinalIgnoreCase))
            ?? AiConnectionProfiles.FirstOrDefault();
        SelectedAiModel = AiModels.FirstOrDefault(model =>
                string.Equals(model.Id, previouslySelectedModelId, StringComparison.OrdinalIgnoreCase)
                && (SelectedAiConnection is null || string.Equals(model.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase)))
            ?? AiModels.FirstOrDefault(model => SelectedAiConnection is not null && string.Equals(model.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase))
            ?? AiModels.FirstOrDefault();

        // Set summarizer AFTER models are populated so the ComboBox can match it.
        SetProperty(ref _summarizerProviderSelection, NormalizeModelSelection(settings.SummarizerProvider, settings), nameof(SummarizerProviderSelection));

        NormalizeThemeSelections();
        ConnectionSettingsSummary = BuildConnectionSettingsSummary(settings);
        _isApplyingSettings = false;
        RefreshSetupGuideProgress(autoFinalize: false);
    }

    private void RefreshSetupGuideProgress(bool autoFinalize)
    {
        var modelsCompleted = _loadedConnectionSettings.SetupGuideModelsCompleted || InferSetupGuideModelsCompleted();
        var roomsCompleted = _loadedConnectionSettings.SetupGuideRoomsCompleted || InferSetupGuideRoomsCompleted();
        var ttsStatus = GetEffectiveSetupGuideTtsStatus();

        IsSetupGuideModelsCompleted = modelsCompleted;
        IsSetupGuideRoomsCompleted = roomsCompleted;
        SetupGuideTtsStatus = ttsStatus;
        UpdateSetupGuideVisibility();

        OnPropertyChanged(nameof(IsSetupGuideTtsCompleted));
        OnPropertyChanged(nameof(IsSetupGuideComplete));
        OnPropertyChanged(nameof(SetupGuideCompletedStepCount));
        OnPropertyChanged(nameof(SetupGuideProgressText));
        OnPropertyChanged(nameof(SetupGuideModelsChecklistText));
        OnPropertyChanged(nameof(SetupGuideRoomsChecklistText));
        OnPropertyChanged(nameof(SetupGuideTtsChecklistText));
        OnPropertyChanged(nameof(SetupGuideCompletionMessage));
        OnPropertyChanged(nameof(CanSkipSetupGuideTts));

        if (autoFinalize && IsSetupGuideComplete && !_loadedConnectionSettings.HideSetupGuide && !_isApplyingSetupGuideState)
        {
            PersistSetupGuideState(
                settings =>
                {
                    settings.SetupGuideModelsCompleted = true;
                    settings.SetupGuideRoomsCompleted = true;
                    settings.SetupGuideTtsStatus = ttsStatus;
                    settings.HideSetupGuide = true;
                },
                onCompleted: () =>
                {
                    _showSetupGuideOnDemand = false;
                    UpdateSetupGuideVisibility();

                    if (MainTabControl.Items.OfType<TabItem>().FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Setup Guide", StringComparison.Ordinal)) == MainTabControl.SelectedItem)
                    {
                        SelectTabByHeader("Rooms");
                    }

                    AddSystemMessage("Setup guide completed. It will stay hidden from now on.");
                });
        }
    }

    private void UpdateSetupGuideVisibility()
    {
        SetupGuideVisibility = _showSetupGuideOnDemand || !_loadedConnectionSettings.HideSetupGuide
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PersistSetupGuideState(Action<ConnectionSettings> applyChanges, Action? onCompleted = null)
    {
        try
        {
            _isApplyingSetupGuideState = true;
            var settings = _connectionSettingsStore.Load();
            applyChanges(settings);
            _connectionSettingsStore.Save(settings);
            applyChanges(_loadedConnectionSettings);
            RefreshSetupGuideProgress(autoFinalize: false);
            onCompleted?.Invoke();
        }
        catch (Exception exception)
        {
            ConnectionSettingsSummary = exception.Message;
        }
        finally
        {
            _isApplyingSetupGuideState = false;
        }
    }

    private bool InferSetupGuideModelsCompleted()
    {
        return AiModels.Count > _loadedConnectionSettings.AiModels.Count
            || AiConnectionProfiles.Count > _loadedConnectionSettings.AiConnections.Count
            || AiConnectionProfiles.Any(connection => !string.IsNullOrWhiteSpace(connection.ApiKey))
            || AiModels.Any(model => !string.IsNullOrWhiteSpace(model.Notes));
    }

    private bool InferSetupGuideRoomsCompleted()
    {
        return Themes.Count > 3
            || Themes.Any(theme => theme.Agents.Count > 3)
            || Themes.Any(theme => theme.Agents.Any(agent => !string.Equals(agent.Provider, "Groq Fast", StringComparison.OrdinalIgnoreCase)));
    }

    private string GetEffectiveSetupGuideTtsStatus()
    {
        if (_loadedConnectionSettings.SetupGuideTtsStatus is "Configured" or "Skipped")
        {
            return _loadedConnectionSettings.SetupGuideTtsStatus;
        }

        var looksConfigured = _loadedConnectionSettings.TextToSpeechEnabled
            || !string.Equals(_loadedConnectionSettings.TextToSpeechProvider, "Local", StringComparison.OrdinalIgnoreCase)
            || _loadedConnectionSettings.TextToSpeechRate != 0
            || !string.IsNullOrWhiteSpace(_loadedConnectionSettings.TextToSpeechVoice);

        return looksConfigured ? "Configured" : "Pending";
    }

    private void MarkSetupGuideModelsCompleted()
    {
        PersistSetupGuideState(settings => settings.SetupGuideModelsCompleted = true);
        RefreshSetupGuideProgress(autoFinalize: true);
    }

    private void MarkSetupGuideRoomsCompleted()
    {
        PersistSetupGuideState(settings => settings.SetupGuideRoomsCompleted = true);
        RefreshSetupGuideProgress(autoFinalize: true);
    }

    private void MarkSetupGuideTtsConfigured()
    {
        PersistSetupGuideState(settings => settings.SetupGuideTtsStatus = "Configured");
        RefreshSetupGuideProgress(autoFinalize: true);
    }

    private void SkipSetupGuideTtsButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistSetupGuideState(settings => settings.SetupGuideTtsStatus = "Skipped");
        RefreshSetupGuideProgress(autoFinalize: true);
    }

    private bool TryPersistAiSettings(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (_isApplyingSettings)
        {
            return true;
        }

        try
        {
            var repairedModelConnections = ReconcileAiModelConnections();
            if (repairedModelConnections)
            {
                RefreshAiModelConnectionNames();
                RefreshFilteredAiModels();
            }

            ValidateAiSettings(AiConnectionProfiles, AiModels);
            var renamedSelections = PropagateModelRenames();
            RefreshAiModelConnectionNames();

            var snapshot = CreateCurrentConnectionSettingsSnapshot();
            _connectionSettingsStore.Save(snapshot);

            var reloaded = _connectionSettingsStore.Load();
            ApplyLoadedConnectionSettings(reloaded);

            if (renamedSelections && Themes.Count > 0)
            {
                PersistCurrentConfiguration();
            }

            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            ConnectionSettingsSummary = exception.Message;
            return false;
        }
    }

    private bool ReconcileAiModelConnections()
    {
        var changed = false;
        foreach (var model in AiModels)
        {
            var connectionId = model.ConnectionId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(connectionId)
                && AiConnectionProfiles.Any(connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var resolvedConnection = ResolveConnectionForModelReference(connectionId, model.Name);
            if (resolvedConnection is null)
            {
                continue;
            }

            if (!string.Equals(model.ConnectionId, resolvedConnection.Id, StringComparison.OrdinalIgnoreCase))
            {
                model.ConnectionId = resolvedConnection.Id;
                changed = true;
            }
        }

        return changed;
    }

    private AiConnectionProfile? ResolveConnectionForModelReference(string connectionReference, string modelName)
    {
        if (!string.IsNullOrWhiteSpace(connectionReference))
        {
            var exactConnection = AiConnectionProfiles.FirstOrDefault(connection =>
                string.Equals(connection.Name, connectionReference, StringComparison.OrdinalIgnoreCase));
            if (exactConnection is not null)
            {
                return exactConnection;
            }

            var canonicalReference = GetCanonicalConnectionReference(connectionReference);
            var canonicalConnection = AiConnectionProfiles.FirstOrDefault(connection =>
                string.Equals(GetCanonicalConnectionReference(connection.Name), canonicalReference, StringComparison.OrdinalIgnoreCase));
            if (canonicalConnection is not null)
            {
                return canonicalConnection;
            }
        }

        var inferredConnectionName = InferConnectionNameFromModelName(modelName);
        if (string.IsNullOrWhiteSpace(inferredConnectionName))
        {
            return null;
        }

        return AiConnectionProfiles.FirstOrDefault(connection =>
            string.Equals(GetCanonicalConnectionReference(connection.Name), inferredConnectionName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCanonicalConnectionReference(string? value)
    {
        return value?.Trim() switch
        {
            "Groq Fast" => "Groq",
            "Groq Smart" => "Groq",
            "Hugging Face" => "HuggingFace",
            _ => value?.Trim() ?? string.Empty,
        };
    }

    private static string InferConnectionNameFromModelName(string? modelName)
    {
        var tokens = TokenizeModelReference(modelName);
        if (tokens.Contains("groq"))
        {
            return "Groq";
        }

        if (tokens.Contains("gemini"))
        {
            return "Gemini";
        }

        if (tokens.Contains("huggingface") || (tokens.Contains("hugging") && tokens.Contains("face")))
        {
            return "HuggingFace";
        }

        if (tokens.Contains("ollama"))
        {
            return "Ollama";
        }

        if (tokens.Contains("openrouter"))
        {
            return "OpenRouter";
        }

        return string.Empty;
    }

    private ConnectionSettings CreateCurrentConnectionSettingsSnapshot()
    {
        var settings = new ConnectionSettings
        {
            SummarizerProvider = NormalizeModelSelection(_summarizerProviderSelection,
                new ConnectionSettings { AiModels = AiModels.Select(CloneAiModel).ToList() }),
            SetupGuideModelsCompleted = _loadedConnectionSettings.SetupGuideModelsCompleted,
            SetupGuideRoomsCompleted = _loadedConnectionSettings.SetupGuideRoomsCompleted,
            SetupGuideTtsStatus = _loadedConnectionSettings.SetupGuideTtsStatus,
            HideSetupGuide = _loadedConnectionSettings.HideSetupGuide,
            UiTheme = _loadedConnectionSettings.UiTheme,
            UiAccent = _loadedConnectionSettings.UiAccent,
            TextToSpeechProvider = _loadedConnectionSettings.TextToSpeechProvider,
            TextToSpeechEnabled = _loadedConnectionSettings.TextToSpeechEnabled,
            TextToSpeechVoice = _loadedConnectionSettings.TextToSpeechVoice,
            TextToSpeechRate = _loadedConnectionSettings.TextToSpeechRate,
            PiperExecutablePath = _loadedConnectionSettings.PiperExecutablePath,
            PiperModelsDirectory = _loadedConnectionSettings.PiperModelsDirectory,
            KokoroBaseUrl = _loadedConnectionSettings.KokoroBaseUrl,
            KokoroModel = _loadedConnectionSettings.KokoroModel,
            KokoroVoice = _loadedConnectionSettings.KokoroVoice,
            KokoroLanguageCode = _loadedConnectionSettings.KokoroLanguageCode,
            KokoroSpeed = _loadedConnectionSettings.KokoroSpeed,
            KokoroVoices = _loadedConnectionSettings.KokoroVoices?.ToList() ?? new List<string>(),
            AiConnections = AiConnectionProfiles.Select(CloneAiConnection).ToList(),
            AiModels = AiModels.Select(CloneAiModel).ToList(),
        };

        return settings;
    }

    private ConnectionSettings CreateCurrentModelCatalogSnapshot()
    {
        return new ConnectionSettings
        {
            SummarizerProvider = _summarizerProviderSelection,
            AiConnections = AiConnectionProfiles.Select(CloneAiConnection).ToList(),
            AiModels = AiModels.Select(CloneAiModel).ToList(),
        };
    }

    private static AiConnectionProfile CloneAiConnection(AiConnectionProfile connection)
    {
        return new AiConnectionProfile
        {
            Id = connection.Id,
            Name = connection.Name?.Trim() ?? string.Empty,
            Transport = connection.Transport?.Trim() ?? string.Empty,
            Endpoint = connection.Endpoint?.Trim() ?? string.Empty,
            ApiKey = connection.ApiKey?.Trim() ?? string.Empty,
        };
    }

    private static AiModelCatalogEntry CloneAiModel(AiModelCatalogEntry model)
    {
        return new AiModelCatalogEntry
        {
            Id = model.Id,
            Name = model.Name?.Trim() ?? string.Empty,
            ConnectionId = model.ConnectionId?.Trim() ?? string.Empty,
            ModelId = model.ModelId?.Trim() ?? string.Empty,
            Notes = model.Notes?.Trim() ?? string.Empty,
        };
    }

    private void RefreshAiModelConnectionNames()
    {
        foreach (var model in AiModels)
        {
            var connectionName = AiConnectionProfiles.FirstOrDefault(connection => string.Equals(connection.Id, model.ConnectionId, StringComparison.OrdinalIgnoreCase))?.Name
                ?? "Missing connection";
            model.ConnectionName = connectionName;
        }
    }

    private void CaptureAiModelNameSnapshots()
    {
        _aiModelNameSnapshots.Clear();

        foreach (var model in AiModels)
        {
            _aiModelNameSnapshots[model.Id] = model.Name;
        }
    }

    private bool PropagateModelRenames()
    {
        var changed = false;

        foreach (var model in AiModels)
        {
            var currentName = model.Name?.Trim() ?? string.Empty;
            if (!_aiModelNameSnapshots.TryGetValue(model.Id, out var previousName)
                || string.IsNullOrWhiteSpace(previousName)
                || string.Equals(previousName, currentName, StringComparison.Ordinal))
            {
                continue;
            }

            RenameModelSelectionReferences(previousName, currentName);
            changed = true;
        }

        return changed;
    }

    private void AiConnectionProfiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AiConnectionProfile connection in e.OldItems)
            {
                connection.PropertyChanged -= AiConnectionProfile_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AiConnectionProfile connection in e.NewItems)
            {
                connection.PropertyChanged += AiConnectionProfile_PropertyChanged;
            }
        }

        RefreshAiModelConnectionNames();
        RefreshFilteredAiModels();
    }

    private void AiModels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AiModelCatalogEntry model in e.OldItems)
            {
                model.PropertyChanged -= AiModelCatalogEntry_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AiModelCatalogEntry model in e.NewItems)
            {
                model.PropertyChanged += AiModelCatalogEntry_PropertyChanged;
            }
        }

        RefreshFilteredAiModels();
    }

    private void AiConnectionProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AiConnectionProfile connection
            && ReferenceEquals(SelectedAiConnection, connection)
            && e.PropertyName == nameof(AiConnectionProfile.Name))
        {
            OnPropertyChanged(nameof(SelectedAiConnectionDisplayName));
        }

        if (e.PropertyName is nameof(AiConnectionProfile.Name)
            or nameof(AiConnectionProfile.Transport)
            or nameof(AiConnectionProfile.Endpoint))
        {
            RefreshAiModelConnectionNames();
            RefreshFilteredAiModels();
        }
    }

    private void AiModelCatalogEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AiModelCatalogEntry model)
        {
            return;
        }

        if (e.PropertyName is nameof(AiModelCatalogEntry.ConnectionId)
            or nameof(AiModelCatalogEntry.Name)
            or nameof(AiModelCatalogEntry.ModelId))
        {
            RefreshAiModelConnectionNames();

            if (ReferenceEquals(SelectedAiModel, model)
                && !string.IsNullOrWhiteSpace(model.ConnectionId)
                && (SelectedAiConnection is null || !string.Equals(SelectedAiConnection.Id, model.ConnectionId, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedAiConnection = AiConnectionProfiles.FirstOrDefault(connection => string.Equals(connection.Id, model.ConnectionId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                RefreshFilteredAiModels();
            }
        }
    }

    private bool FilterAiModelEntry(object item)
    {
        if (item is not AiModelCatalogEntry model)
        {
            return false;
        }

        return SelectedAiConnection is null
            || string.Equals(model.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilteredAiModels()
    {
        RefreshAiModelConnectionNames();
        FilteredAiModels.Refresh();

        if (SelectedAiConnection is null)
        {
            if (SelectedAiModel is null || !AiModels.Any(model => string.Equals(model.Id, SelectedAiModel.Id, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedAiModel = AiModels.FirstOrDefault();
            }

            return;
        }

        if (SelectedAiModel is not null
            && string.Equals(SelectedAiModel.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase)
            && AiModels.Any(model => string.Equals(model.Id, SelectedAiModel.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedAiModel = AiModels.FirstOrDefault(model => string.Equals(model.ConnectionId, SelectedAiConnection.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void RenameModelSelectionReferences(string previousName, string currentName)
    {
        if (string.Equals(_summarizerProviderSelection, previousName, StringComparison.OrdinalIgnoreCase))
        {
            SetProperty(ref _summarizerProviderSelection, currentName, nameof(SummarizerProviderSelection));
        }

        if (string.Equals(_loadedConnectionSettings.SummarizerProvider, previousName, StringComparison.OrdinalIgnoreCase))
        {
            _loadedConnectionSettings.SummarizerProvider = currentName;
        }

        foreach (var theme in Themes)
        {
            if (string.Equals(theme.SummarizerProvider, previousName, StringComparison.OrdinalIgnoreCase))
            {
                theme.SummarizerProvider = currentName;
            }

            foreach (var agent in theme.Agents)
            {
                if (string.Equals(agent.Provider, previousName, StringComparison.OrdinalIgnoreCase))
                {
                    agent.Provider = currentName;
                }
            }
        }
    }

    private bool NormalizeThemeSelections()
    {
        var changed = false;
        var currentSettings = CreateCurrentModelCatalogSnapshot();

        foreach (var theme in Themes)
        {
            var normalizedThemeSummarizer = NormalizeModelSelection(theme.SummarizerProvider, currentSettings);
            if (!string.Equals(theme.SummarizerProvider, normalizedThemeSummarizer, StringComparison.Ordinal))
            {
                theme.SummarizerProvider = normalizedThemeSummarizer;
                changed = true;
            }

            foreach (var agent in theme.Agents)
            {
                var normalizedSelection = NormalizeModelSelection(agent.Provider, currentSettings);
                if (!string.Equals(agent.Provider, normalizedSelection, StringComparison.Ordinal))
                {
                    agent.Provider = normalizedSelection;
                    changed = true;
                }
            }
        }

        var normalizedSummarizer = NormalizeModelSelection(_summarizerProviderSelection, currentSettings);
        if (!string.Equals(_summarizerProviderSelection, normalizedSummarizer, StringComparison.Ordinal))
        {
            SetProperty(ref _summarizerProviderSelection, normalizedSummarizer, nameof(SummarizerProviderSelection));
            changed = true;
        }

        return changed;
    }

    private static void ValidateAiSettings(
        IEnumerable<AiConnectionProfile> connections,
        IEnumerable<AiModelCatalogEntry> models)
    {
        var connectionList = connections.ToList();
        var modelList = models.ToList();

        if (connectionList.Count == 0)
        {
            throw new InvalidOperationException("Add at least one AI connection before saving.");
        }

        if (modelList.Count == 0)
        {
            throw new InvalidOperationException("Add at least one AI model before saving.");
        }

        var connectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connectionList)
        {
            if (string.IsNullOrWhiteSpace(connection.Name))
            {
                throw new InvalidOperationException("Every AI connection needs a name.");
            }

            if (!connectionNames.Add(connection.Name.Trim()))
            {
                throw new InvalidOperationException($"AI connection names must be unique. Duplicate: '{connection.Name}'.");
            }

            if (!ConnectionSettingsStore.AvailableTransportNames.Contains(connection.Transport?.Trim() ?? string.Empty, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"'{connection.Name}' uses an unsupported transport.");
            }
        }

        var modelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in modelList)
        {
            var modelName = model.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new InvalidOperationException("Every AI model needs a display name.");
            }

            if (!modelNames.Add(modelName))
            {
                throw new InvalidOperationException($"AI model names must be unique. Duplicate: '{modelName}'.");
            }

            if (string.IsNullOrWhiteSpace(model.ConnectionId)
                || !connectionList.Any(connection => string.Equals(connection.Id, model.ConnectionId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"'{modelName}' must point to a valid AI connection.");
            }
        }
    }

    private bool TryDescribeModelUsage(string modelName, out string usageDescription)
    {
        var usages = new List<string>();

        if (string.Equals(_summarizerProviderSelection, modelName, StringComparison.OrdinalIgnoreCase))
        {
            usages.Add("room summarizer");
        }

        foreach (var theme in Themes)
        {
            if (string.Equals(theme.SummarizerProvider, modelName, StringComparison.OrdinalIgnoreCase))
            {
                usages.Add($"{theme.Name} summarizer");
            }

            foreach (var agent in theme.Agents.Where(agent => string.Equals(agent.Provider, modelName, StringComparison.OrdinalIgnoreCase)))
            {
                usages.Add($"{theme.Name} / {agent.Name}");
            }
        }

        usageDescription = usages.Count == 0
            ? string.Empty
            : string.Join(", ", usages.Take(5));

        return usages.Count > 0;
    }

    private (string Name, string Transport, string Endpoint) GetNextConnectionTemplate()
    {
        foreach (var template in new[]
        {
            (Name: "Groq", Transport: "Groq", Endpoint: GetSuggestedEndpointForTransport("Groq")),
            (Name: "Gemini", Transport: "Gemini", Endpoint: GetSuggestedEndpointForTransport("Gemini")),
            (Name: "HuggingFace", Transport: "HuggingFace", Endpoint: GetSuggestedEndpointForTransport("HuggingFace")),
            (Name: "Ollama", Transport: "Ollama", Endpoint: GetSuggestedEndpointForTransport("Ollama")),
            (Name: "OpenRouter", Transport: "OpenAI Compatible", Endpoint: GetSuggestedEndpointForTransport("OpenAI Compatible")),
        })
        {
            if (!AiConnectionProfiles.Any(connection => string.Equals(connection.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return template;
            }
        }

        for (var index = 1; ; index++)
        {
            var candidate = $"Custom Connection {index}";
            if (!AiConnectionProfiles.Any(connection => string.Equals(connection.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return (candidate, "OpenAI Compatible", GetSuggestedEndpointForTransport("OpenAI Compatible"));
            }
        }
    }

    private string GetNextModelName()
    {
        for (var index = 1; ; index++)
        {
            var candidate = $"Model {index}";
            if (!AiModels.Any(model => string.Equals(model.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private static string GetSuggestedEndpointForTransport(string transport)
    {
        return transport.Trim() switch
        {
            "Groq" => "https://api.groq.com/openai/v1/chat/completions",
            "Gemini" => "https://generativelanguage.googleapis.com/v1beta/models",
            "HuggingFace" => "https://router.huggingface.co/v1/chat/completions",
            "Ollama" => "http://localhost:11434/api/chat",
            _ => "https://openrouter.ai/api/v1/chat/completions",
        };
    }

    private static string NormalizeModelSelection(string? selection, ConnectionSettings settings)
    {
        var trimmedSelection = selection?.Trim();
        if (settings.AiModels.Count == 0)
        {
            return string.IsNullOrWhiteSpace(trimmedSelection) ? "Gemini" : trimmedSelection;
        }

        if (string.IsNullOrWhiteSpace(trimmedSelection))
        {
            return GetDefaultModelSelection(settings);
        }

        var matchedByName = settings.AiModels.FirstOrDefault(model => string.Equals(model.Name, trimmedSelection, StringComparison.OrdinalIgnoreCase));
        if (matchedByName is not null)
        {
            return matchedByName.Name;
        }

        var matchedById = settings.AiModels.FirstOrDefault(model => string.Equals(model.Id, trimmedSelection, StringComparison.OrdinalIgnoreCase));
        if (matchedById is not null)
        {
            return matchedById.Name;
        }

        var legacyAliasMatch = ResolveLegacyModelAlias(trimmedSelection, settings.AiModels);
        if (!string.IsNullOrWhiteSpace(legacyAliasMatch))
        {
            return legacyAliasMatch;
        }

        return GetDefaultModelSelection(settings);
    }

    private static string? ResolveLegacyModelAlias(string selection, IReadOnlyList<AiModelCatalogEntry> models)
    {
        if (string.Equals(selection, "Groq", StringComparison.OrdinalIgnoreCase))
        {
            return models.FirstOrDefault(model => string.Equals(model.Name, "Groq Fast", StringComparison.OrdinalIgnoreCase))?.Name
                ?? models.FirstOrDefault(model => string.Equals(model.Name, "Groq Smart", StringComparison.OrdinalIgnoreCase))?.Name;
        }

        if (string.Equals(selection, "Hugging Face", StringComparison.OrdinalIgnoreCase))
        {
            return models.FirstOrDefault(model => string.Equals(model.Name, "HuggingFace", StringComparison.OrdinalIgnoreCase))?.Name;
        }

        return FindTokenMatchedModel(selection, models);
    }

    private static string? FindTokenMatchedModel(string selection, IReadOnlyList<AiModelCatalogEntry> models)
    {
        var selectionTokens = TokenizeModelReference(selection);
        if (selectionTokens.Count == 0)
        {
            return null;
        }

        var matchedByName = models.FirstOrDefault(model => AreReferenceTokensContained(selectionTokens, model.Name));
        if (matchedByName is not null)
        {
            return matchedByName.Name;
        }

        return models.FirstOrDefault(model => AreReferenceTokensContained(selectionTokens, model.ModelId))?.Name;
    }

    private static bool AreReferenceTokensContained(HashSet<string> selectionTokens, string? candidate)
    {
        var candidateTokens = TokenizeModelReference(candidate);
        return selectionTokens.Count > 0 && selectionTokens.All(candidateTokens.Contains);
    }

    private static HashSet<string> TokenizeModelReference(string? value)
    {
        return Regex.Matches(value?.ToLowerInvariant() ?? string.Empty, "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(token => token.Any(char.IsLetter))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetDefaultModelSelection(ConnectionSettings settings)
    {
        var gemini = settings.AiModels.FirstOrDefault(model => string.Equals(model.Name, "Gemini", StringComparison.OrdinalIgnoreCase));
        return gemini?.Name
            ?? settings.AiModels.FirstOrDefault()?.Name
            ?? "Gemini";
    }

    private static bool TryResolveConfiguredModel(
        ConnectionSettings settings,
        string selection,
        out AiModelCatalogEntry? model,
        out AiConnectionProfile? connection)
    {
        var normalizedSelection = selection.Trim();
        model = settings.AiModels.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalizedSelection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Id, normalizedSelection, StringComparison.OrdinalIgnoreCase));
        var resolvedConnectionId = model?.ConnectionId;
        connection = resolvedConnectionId is null
            ? null
            : settings.AiConnections.FirstOrDefault(candidate => string.Equals(candidate.Id, resolvedConnectionId, StringComparison.OrdinalIgnoreCase));

        return model is not null && connection is not null;
    }

    private static LlmProvider ParseTransport(string transport)
    {
        return transport.Trim() switch
        {
            "Groq" => LlmProvider.Groq,
            "Gemini" => LlmProvider.Gemini,
            "HuggingFace" => LlmProvider.HuggingFace,
            "Hugging Face" => LlmProvider.HuggingFace,
            "Ollama" => LlmProvider.Ollama,
            _ => LlmProvider.OpenAiCompatible,
        };
    }

    private static string BuildConnectionSettingsSummary(ConnectionSettings settings)
    {
        string textToSpeechProvider;
        if (string.Equals(settings.TextToSpeechProvider, "Piper", StringComparison.OrdinalIgnoreCase))
        {
            var modelCount = Directory.Exists(settings.PiperModelsDirectory)
                ? Directory.EnumerateFiles(settings.PiperModelsDirectory, "*.onnx", SearchOption.AllDirectories).Count()
                : 0;
            textToSpeechProvider = $"Piper ({(File.Exists(settings.PiperExecutablePath) ? "exe ready" : "exe missing")}, {modelCount} model{(modelCount == 1 ? string.Empty : "s")})";
        }
        else if (string.Equals(settings.TextToSpeechProvider, "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            var voiceCount = settings.KokoroVoices?.Count ?? 0;
            textToSpeechProvider = $"Kokoro ({settings.KokoroBaseUrl}/v1/audio/speech, default {settings.KokoroVoice}, {voiceCount} voice{(voiceCount == 1 ? string.Empty : "s")})";
        }
        else
        {
            textToSpeechProvider = "Local Windows voices";
        }

        var textToSpeechSummary = settings.TextToSpeechEnabled
            ? $"TTS: on via {textToSpeechProvider} (per-agent voices, rate {settings.TextToSpeechRate})"
            : "TTS: off";
        var appearanceSummary = $"Appearance: {settings.UiTheme} / {settings.UiAccent} accent";
        var modelLines = settings.AiModels
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(model =>
            {
                var connection = settings.AiConnections.FirstOrDefault(candidate => string.Equals(candidate.Id, model.ConnectionId, StringComparison.OrdinalIgnoreCase));
                var connectionLabel = connection?.Name ?? "Missing connection";
                var apiKeyState = connection is null
                    ? "connection missing"
                    : ConnectionSettingsStore.TransportRequiresApiKey(connection.Transport)
                        ? (string.IsNullOrWhiteSpace(connection.ApiKey) ? "key missing" : "key saved")
                        : (string.IsNullOrWhiteSpace(connection.ApiKey) ? "key optional" : "key saved");
                var modelId = string.IsNullOrWhiteSpace(model.ModelId) ? "(no model id)" : model.ModelId;
                return $"{model.Name}: {modelId} via {connectionLabel} ({apiKeyState})";
            })
            .ToList();

        if (settings.AiModels.Count > modelLines.Count)
        {
            modelLines.Add($"... and {settings.AiModels.Count - modelLines.Count} more model(s)");
        }

        return $"Summarizer model: {NormalizeModelSelection(settings.SummarizerProvider, settings)}\n{textToSpeechSummary}\n{appearanceSummary}\nAI catalog: {settings.AiConnections.Count} connection(s), {settings.AiModels.Count} model(s)\n{string.Join("\n", modelLines)}";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private AgentProfile CreateAgent(string name, string prompt, int index)
    {
        var palette = new[]
        {
            (Accent: "#C56A54", Background: "#F9E5DE"),
            (Accent: "#367A72", Background: "#E1F1EE"),
            (Accent: "#6E5AA6", Background: "#ECE6FA"),
            (Accent: "#A67C33", Background: "#F5EBCF"),
        };

        var selected = palette[index % palette.Length];
        return new AgentProfile
        {
            Name = name,
            Provider = "Groq Fast",
            SystemPrompt = prompt,
            CompactionBudget = 420,
            AccentHex = selected.Accent,
            BackgroundHex = selected.Background,
            TextToSpeechVoice = GetSuggestedTextToSpeechVoice(index),
        };
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record SessionTurn(string Speaker, string Content);
}