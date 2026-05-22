using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Infrastructure.Entities;

namespace AgentGroupChat.Infrastructure.Data;

public static class EntityMapper
{
    public static AppSettings ToDomain(AppSettingsEntity e) => new()
    {
        UiTheme = e.UiTheme,
        UiAccent = e.UiAccent,
        TtsEnabled = e.TtsEnabled,
        TtsProvider = e.TtsProvider,
        TtsVoice = e.TtsVoice,
        TtsRate = e.TtsRate,
        PiperExePath = e.PiperExePath,
        PiperModelsDir = e.PiperModelsDir,
        KokoroBaseUrl = e.KokoroBaseUrl,
        KokoroModel = e.KokoroModel,
        KokoroVoice = e.KokoroVoice,
        KokoroLangCode = e.KokoroLangCode,
        KokoroSpeed = e.KokoroSpeed,
        SetupModelsCompleted = e.SetupModelsCompleted,
        SetupRoomsCompleted = e.SetupRoomsCompleted,
        SetupTtsStatus = e.SetupTtsStatus,
        HideSetupGuide = e.HideSetupGuide,
    };

    public static void ApplyTo(AppSettings d, AppSettingsEntity e)
    {
        e.UiTheme = d.UiTheme;
        e.UiAccent = d.UiAccent;
        e.TtsEnabled = d.TtsEnabled;
        e.TtsProvider = d.TtsProvider;
        e.TtsVoice = d.TtsVoice;
        e.TtsRate = d.TtsRate;
        e.PiperExePath = d.PiperExePath;
        e.PiperModelsDir = d.PiperModelsDir;
        e.KokoroBaseUrl = d.KokoroBaseUrl;
        e.KokoroModel = d.KokoroModel;
        e.KokoroVoice = d.KokoroVoice;
        e.KokoroLangCode = d.KokoroLangCode;
        e.KokoroSpeed = d.KokoroSpeed;
        e.SetupModelsCompleted = d.SetupModelsCompleted;
        e.SetupRoomsCompleted = d.SetupRoomsCompleted;
        e.SetupTtsStatus = d.SetupTtsStatus;
        e.HideSetupGuide = d.HideSetupGuide;
    }

    public static AiConnection ToDomain(AiConnectionEntity e) => new()
    {
        Id = e.Id, Name = e.Name, Transport = e.Transport,
        Endpoint = e.Endpoint, ApiKey = e.ApiKey, SortOrder = e.SortOrder,
    };

    public static AiConnectionEntity ToEntity(AiConnection d) => new()
    {
        Id = d.Id, Name = d.Name, Transport = d.Transport,
        Endpoint = d.Endpoint, ApiKey = d.ApiKey, SortOrder = d.SortOrder,
    };

    public static AiModel ToDomain(AiModelEntity e) => new()
    {
        Id = e.Id, Name = e.Name, ConnectionId = e.ConnectionId,
        ModelId = e.ModelId, Notes = e.Notes, SortOrder = e.SortOrder,
    };

    public static AiModelEntity ToEntity(AiModel d) => new()
    {
        Id = d.Id, Name = d.Name, ConnectionId = d.ConnectionId,
        ModelId = d.ModelId, Notes = d.Notes, SortOrder = d.SortOrder,
    };

    public static AgentConfig ToDomain(AgentEntity e) => new()
    {
        Id = e.Id, RoomId = e.RoomId, Name = e.Name, ModelId = e.ModelId,
        SystemPrompt = e.SystemPrompt, IsEnabled = e.IsEnabled,
        MaxTokensOverride = e.MaxTokensOverride, CompactionBudget = e.CompactionBudget,
        AccentHex = e.AccentHex, BackgroundHex = e.BackgroundHex,
        TtsVoice = e.TtsVoice, SortOrder = e.SortOrder,
    };

    public static AgentEntity ToEntity(AgentConfig d) => new()
    {
        Id = d.Id, RoomId = d.RoomId, Name = d.Name, ModelId = d.ModelId,
        SystemPrompt = d.SystemPrompt, IsEnabled = d.IsEnabled,
        MaxTokensOverride = d.MaxTokensOverride, CompactionBudget = d.CompactionBudget,
        AccentHex = d.AccentHex, BackgroundHex = d.BackgroundHex,
        TtsVoice = d.TtsVoice, SortOrder = d.SortOrder,
    };

    public static RoomConfig ToDomain(RoomEntity e) => new()
    {
        Id = e.Id, Name = e.Name, Topic = e.Topic,
        WaitForUserReply = e.WaitForUserReply, AgentDelaySeconds = e.AgentDelaySeconds,
        MaxTokens = e.MaxTokens, RecentTurnsWindow = e.RecentTurnsWindow,
        UserCompactionBudget = e.UserCompactionBudget, SummarizerModelId = e.SummarizerModelId,
        SummarizationLevel = e.SummarizationLevel, SummarizerMaxTokens = e.SummarizerMaxTokens,
        SummarizerMaxLines = e.SummarizerMaxLines, SummarizerMaxCharacters = e.SummarizerMaxCharacters,
        SummarizerBroaderTurns = e.SummarizerBroaderTurns,
        SummarizerPromptOverride = e.SummarizerPromptOverride, SortOrder = e.SortOrder,
        Agents = e.Agents.Select(ToDomain).OrderBy(a => a.SortOrder).ToList(),
    };

    public static RoomEntity ToEntity(RoomConfig d) => new()
    {
        Id = d.Id, Name = d.Name, Topic = d.Topic,
        WaitForUserReply = d.WaitForUserReply, AgentDelaySeconds = d.AgentDelaySeconds,
        MaxTokens = d.MaxTokens, RecentTurnsWindow = d.RecentTurnsWindow,
        UserCompactionBudget = d.UserCompactionBudget, SummarizerModelId = d.SummarizerModelId,
        SummarizationLevel = d.SummarizationLevel, SummarizerMaxTokens = d.SummarizerMaxTokens,
        SummarizerMaxLines = d.SummarizerMaxLines, SummarizerMaxCharacters = d.SummarizerMaxCharacters,
        SummarizerBroaderTurns = d.SummarizerBroaderTurns,
        SummarizerPromptOverride = d.SummarizerPromptOverride, SortOrder = d.SortOrder,
        Agents = d.Agents.Select(ToEntity).ToList(),
    };

    public static TranscriptTurn ToDomain(TranscriptTurnEntity e) => new()
    {
        Id = e.Id, RoomId = e.RoomId, Round = e.Round, Speaker = e.Speaker,
        Content = e.Content, AccentHex = e.AccentHex, BackgroundHex = e.BackgroundHex,
        CreatedAt = e.CreatedAt,
    };

    public static TranscriptTurnEntity ToEntity(TranscriptTurn d) => new()
    {
        RoomId = d.RoomId, Round = d.Round, Speaker = d.Speaker,
        Content = d.Content, AccentHex = d.AccentHex, BackgroundHex = d.BackgroundHex,
        CreatedAt = d.CreatedAt,
    };

    public static LogEntry ToDomain(LogEntryEntity e) => new()
    {
        Id = e.Id, Category = Enum.TryParse<LogCategory>(e.Category, out var c) ? c : LogCategory.System,
        Source = e.Source, Message = e.Message, Detail = e.Detail,
        DurationMs = e.DurationMs, CreatedAt = e.CreatedAt,
    };

    public static LogEntryEntity ToEntity(LogEntry d) => new()
    {
        Category = d.Category.ToString(), Source = d.Source, Message = d.Message,
        Detail = d.Detail, DurationMs = d.DurationMs, CreatedAt = d.CreatedAt,
    };
}
