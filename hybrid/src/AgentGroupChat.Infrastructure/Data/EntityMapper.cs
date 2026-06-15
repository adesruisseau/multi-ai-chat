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
        KokoroUserVoice = e.KokoroUserVoice,
        KokoroLangCode = e.KokoroLangCode,
        KokoroSpeed = e.KokoroSpeed,
        SetupModelsCompleted = e.SetupModelsCompleted,
        SetupRoomsCompleted = e.SetupRoomsCompleted,
        SetupTtsStatus = e.SetupTtsStatus,
        HideSetupGuide = e.HideSetupGuide,
        UserId = e.UserId,
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
        e.KokoroUserVoice = d.KokoroUserVoice;
        e.KokoroLangCode = d.KokoroLangCode;
        e.KokoroSpeed = d.KokoroSpeed;
        e.SetupModelsCompleted = d.SetupModelsCompleted;
        e.SetupRoomsCompleted = d.SetupRoomsCompleted;
        e.SetupTtsStatus = d.SetupTtsStatus;
        e.HideSetupGuide = d.HideSetupGuide;
        e.UserId = d.UserId;
    }

    public static AiConnection ToDomain(AiConnectionEntity e) => new()
    {
        Id = e.Id, Name = e.Name, Transport = e.Transport,
        Endpoint = e.Endpoint, ApiKey = e.ApiKey, SortOrder = e.SortOrder,
        UserId = e.UserId
    };

    public static AiConnectionEntity ToEntity(AiConnection d) => new()
    {
        Id = d.Id, Name = d.Name, Transport = d.Transport,
        Endpoint = d.Endpoint, ApiKey = d.ApiKey, SortOrder = d.SortOrder,
        UserId = d.UserId
    };

    public static AiModel ToDomain(AiModelEntity e) => new()
    {
        Id = e.Id, 
        Name = e.Name, 
        ConnectionId = e.ConnectionId,
        ModelId = e.ModelId, 
        Notes = e.Notes, 
        SortOrder = e.SortOrder,
        Temperature = e.Temperature,
        MaxTokens = e.MaxTokens,
        UserId = e.UserId,

    };

    public static AiModelEntity ToEntity(AiModel d) => new()
    {
        Id = d.Id, 
        Name = d.Name, 
        ConnectionId = d.ConnectionId,
        ModelId = d.ModelId, 
        Notes = d.Notes, 
        SortOrder = d.SortOrder,
        Temperature = d.Temperature,
        MaxTokens = d.MaxTokens,
        UserId = d.UserId,
    };

    public static ImageConnection ToDomain(ImageConnectionEntity e) => new()
    {
        Id = e.Id, Name = e.Name, Transport = e.Transport,
        Endpoint = e.Endpoint, ApiKey = e.ApiKey, SortOrder = e.SortOrder,
        UserId = e.UserId   
    };

    public static ImageConnectionEntity ToEntity(ImageConnection d) => new()
    {
        Id = d.Id, Name = d.Name, Transport = d.Transport,
        Endpoint = d.Endpoint, ApiKey = d.ApiKey, SortOrder = d.SortOrder,
        UserId = d.UserId
    };

    public static ImageModel ToDomain(ImageModelEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        ConnectionId = e.ConnectionId,
        ModelId = e.ModelId,
        WorkflowId = e.WorkflowId,
        Width = e.Width,
        Height = e.Height,
        Steps = e.Steps,
        GuidanceScale = e.GuidanceScale,
        NegativePrompt = e.NegativePrompt,
        Notes = e.Notes,
        SortOrder = e.SortOrder,
        UserId = e.UserId,
    };

    public static ImageModelEntity ToEntity(ImageModel d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        ConnectionId = d.ConnectionId,
        ModelId = d.ModelId,
        WorkflowId = d.WorkflowId,
        Width = d.Width,
        Height = d.Height,
        Steps = d.Steps,
        GuidanceScale = d.GuidanceScale,
        NegativePrompt = d.NegativePrompt,
        Notes = d.Notes,
        SortOrder = d.SortOrder,
        UserId = d.UserId
    };

    public static PromptSample ToDomain(PromptSampleEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Category = e.Category,
        Description = e.Description,
        PromptText = e.PromptText,
        Tags = e.Tags,
        IsBuiltIn = e.IsBuiltIn,
        ParentPromptSampleId = e.ParentPromptSampleId,
        SourceLabel = e.SourceLabel,
        SortOrder = e.SortOrder,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        UserId = e.UserId,
    };

    public static PromptSampleEntity ToEntity(PromptSample d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Category = d.Category,
        Description = d.Description,
        PromptText = d.PromptText,
        Tags = d.Tags,
        IsBuiltIn = d.IsBuiltIn,
        ParentPromptSampleId = d.ParentPromptSampleId,
        SourceLabel = d.SourceLabel,
        SortOrder = d.SortOrder,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        UserId = d.UserId,
    };

    public static AgentConfig ToDomain(AgentEntity e) => new()
    {
        Id = e.Id, RoomId = e.RoomId, Name = e.Name, ModelId = e.ModelId,
        SystemPrompt = e.SystemPrompt, IsEnabled = e.IsEnabled,
        MaxTokensOverride = e.MaxTokensOverride, CompactionBudget = e.CompactionBudget,
        TtsVoice = e.TtsVoice, AppearanceSummary = e.AppearanceSummary,
        UseShortTermMemoryStorage = e.UseShortTermMemoryStorage,
        UseLongTermMemoryStorage = e.UseLongTermMemoryStorage,
        IsNpc = e.IsNpc,
        SpawnedByAgentId = e.SpawnedByAgentId,
        IsTemporarilySuspended = e.IsTemporarilySuspended,
        SuspendedByAgentId = e.SuspendedByAgentId,
        SuspendedUntilRound = e.SuspendedUntilRound,
        SuspensionReason = e.SuspensionReason,
        SortOrder = e.SortOrder,
        IsHumanParticipant = e.IsHumanParticipant,
        PromptSampleId = e.PromptSampleId,
        UserId = e.UserId,
        ColorTheme = e.ColorTheme
    };

    public static AgentEntity ToEntity(AgentConfig d) => new()
    {
        Id = d.Id, RoomId = d.RoomId, Name = d.Name, ModelId = d.ModelId,
        SystemPrompt = d.SystemPrompt, IsEnabled = d.IsEnabled,
        MaxTokensOverride = d.MaxTokensOverride, CompactionBudget = d.CompactionBudget,
        TtsVoice = d.TtsVoice, AppearanceSummary = d.AppearanceSummary,
        UseShortTermMemoryStorage = d.UseShortTermMemoryStorage,
        UseLongTermMemoryStorage = d.UseLongTermMemoryStorage,
        IsNpc = d.IsNpc,
        SpawnedByAgentId = d.SpawnedByAgentId,
        IsTemporarilySuspended = d.IsTemporarilySuspended,
        SuspendedByAgentId = d.SuspendedByAgentId,
        SuspendedUntilRound = d.SuspendedUntilRound,
        SuspensionReason = d.SuspensionReason,
        SortOrder = d.SortOrder,
        IsHumanParticipant = d.IsHumanParticipant,
        PromptSampleId = d.PromptSampleId,
        UserId = d.UserId,
        ColorTheme = d.ColorTheme
            
};

    public static RoomConfig ToDomain(RoomEntity e) => new()
    {
        Id = e.Id, Name = e.Name, Topic = e.Topic,
        WaitForUserReply = e.WaitForUserReply, PauseAfterEveryReply = e.PauseAfterEveryReply,
        AgentDelaySeconds = e.AgentDelaySeconds,
        //MaxTokens = e.MaxTokens, RecentTurnsWindow = e.RecentTurnsWindow,
        UserCompactionBudget = e.UserCompactionBudget, SummarizerModelId = e.SummarizerModelId,
        UseSummarizer = e.UseSummarizer,
        StoreSharedRoomMemory = e.StoreSharedRoomMemory,
        StoreDurableMemory = e.StoreDurableMemory,
        StoreLongTermArchives = e.StoreLongTermArchives,
        SummarizationLevel = e.SummarizationLevel, SummarizerMaxTokens = e.SummarizerMaxTokens,
        SummarizerMaxLines = e.SummarizerMaxLines, SummarizerMaxCharacters = e.SummarizerMaxCharacters,
        SummarizerBroaderTurns = e.SummarizerBroaderTurns,
        SummarizerPromptOverride = e.SummarizerPromptOverride,
        
        TtsEnabledOverride = e.TtsEnabledOverride,
        TtsProviderOverride = e.TtsProviderOverride,
        TtsFallbackVoice = e.TtsFallbackVoice,
        TtsUserVoice = e.TtsUserVoice,
        EnableSceneImageGeneration = e.EnableSceneImageGeneration,
        UseCreativeImageGeneration = e.UseCreativeImageGeneration,
        SceneImageModelId = e.SceneImageModelId,
        SceneImageStyleNotes = e.SceneImageStyleNotes,
        SceneImageNegativePrompt = e.SceneImageNegativePrompt,
        MemoryModelId = e.MemoryModelId, MaxArchivedScenes = e.MaxArchivedScenes,
        EnableSceneArchive = e.EnableSceneArchive,
        EnablePrivilegedActions = e.EnablePrivilegedActions,
        EnableNpcSpawning = e.EnableNpcSpawning,
        PrivilegedAgentId = e.PrivilegedAgentId,
        NpcModelId = e.NpcModelId,
        NpcDefaultMaleVoice = e.NpcDefaultMaleVoice,
        NpcDefaultFemaleVoice = e.NpcDefaultFemaleVoice,
        NpcMaxTokens = e.NpcMaxTokens,
        NpcCompactionBudget = e.NpcCompactionBudget,
        NpcBaseInstructions = e.NpcBaseInstructions,
        MaxConcurrentNpcs = e.MaxConcurrentNpcs,
        SortOrder = e.SortOrder,
        Agents = e.Agents.Select(ToDomain).OrderBy(a => a.SortOrder).ToList(),
        DataTrackers = e.DataTrackers.Select(ToDomain).OrderBy(x => x.Id).ToList(),
        SharedRoomMemoryPromptSampleId = e.SharedRoomMemoryPromptSampleId,
        DurableMemoryPromptSampleId = e.DurableMemoryPromptSampleId,
        NpcPromptSampleId = e.NpcPromptSampleId,
        UserId = e.UserId,
    };

    public static RoomEntity ToEntity(RoomConfig d) => new()
    {
        Id = d.Id, Name = d.Name, Topic = d.Topic,
        WaitForUserReply = d.WaitForUserReply, PauseAfterEveryReply = d.PauseAfterEveryReply,
        AgentDelaySeconds = d.AgentDelaySeconds,
        //MaxTokens = d.MaxTokens, RecentTurnsWindow = d.RecentTurnsWindow,
        UserCompactionBudget = d.UserCompactionBudget, SummarizerModelId = d.SummarizerModelId,
        UseSummarizer = d.UseSummarizer,
        StoreSharedRoomMemory = d.StoreSharedRoomMemory,
        StoreDurableMemory = d.StoreDurableMemory,
        StoreLongTermArchives = d.StoreLongTermArchives,
        SummarizationLevel = d.SummarizationLevel, SummarizerMaxTokens = d.SummarizerMaxTokens,
        SummarizerMaxLines = d.SummarizerMaxLines, SummarizerMaxCharacters = d.SummarizerMaxCharacters,
        SummarizerBroaderTurns = d.SummarizerBroaderTurns,
        SummarizerPromptOverride = d.SummarizerPromptOverride,
        TtsEnabledOverride = d.TtsEnabledOverride,
        TtsProviderOverride = d.TtsProviderOverride,
        TtsFallbackVoice = d.TtsFallbackVoice,
        TtsUserVoice = d.TtsUserVoice,
        EnableSceneImageGeneration = d.EnableSceneImageGeneration,
        UseCreativeImageGeneration = d.UseCreativeImageGeneration,
        SceneImageModelId = d.SceneImageModelId,
        SceneImageStyleNotes = d.SceneImageStyleNotes,
        SceneImageNegativePrompt = d.SceneImageNegativePrompt,
        MemoryModelId = d.MemoryModelId, MaxArchivedScenes = d.MaxArchivedScenes,
        EnableSceneArchive = d.EnableSceneArchive,
        EnablePrivilegedActions = d.EnablePrivilegedActions,
        EnableNpcSpawning = d.EnableNpcSpawning,
        PrivilegedAgentId = d.PrivilegedAgentId,
        NpcModelId = d.NpcModelId,
        NpcDefaultMaleVoice = d.NpcDefaultMaleVoice,
        NpcDefaultFemaleVoice = d.NpcDefaultFemaleVoice,
        NpcMaxTokens = d.NpcMaxTokens,
        NpcCompactionBudget = d.NpcCompactionBudget,
        NpcBaseInstructions = d.NpcBaseInstructions,
        MaxConcurrentNpcs = d.MaxConcurrentNpcs,
        SortOrder = d.SortOrder,
        Agents = d.Agents.Select(ToEntity).ToList(),
        DataTrackers = d.DataTrackers.Select(ToEntity).ToList(),
        SharedRoomMemoryPromptSampleId = d.SharedRoomMemoryPromptSampleId,
        DurableMemoryPromptSampleId = d.DurableMemoryPromptSampleId,
        NpcPromptSampleId = d.NpcPromptSampleId,
        UserId = d.UserId,
    };

    public static TranscriptTurn ToDomain(TranscriptTurnEntity e) => new()
    {
        Id = e.Id, RoomId = e.RoomId, Round = e.Round, Speaker = e.Speaker,
        Content = e.Content, ColorTheme = e.ColorTheme,
        CreatedAt = e.CreatedAt,
    };

    public static TranscriptTurnEntity ToEntity(TranscriptTurn d) => new()
    {
        RoomId = d.RoomId, Round = d.Round, Speaker = d.Speaker,
        Content = d.Content, ColorTheme = d.ColorTheme,
        CreatedAt = d.CreatedAt
    };

    public static LogEntry ToDomain(LogEntryEntity e) => new()
    {
        Id = e.Id, Category = Enum.TryParse<LogCategory>(e.Category, out var c) ? c : LogCategory.System,
        Source = e.Source, Message = e.Message, Detail = e.Detail,
        DurationMs = e.DurationMs, CreatedAt = e.CreatedAt,
        RoomId = e.RoomId,
    };

    public static LogEntryEntity ToEntity(LogEntry d) => new()
    {
        Category = d.Category.ToString(), Source = d.Source, Message = d.Message,
        Detail = d.Detail, DurationMs = d.DurationMs, CreatedAt = d.CreatedAt,
        RoomId = d.RoomId,
    };

    public static SceneArchive ToDomain(SceneArchiveEntity e) => new()
    {
        Id = e.Id, RoomId = e.RoomId, RoundNumber = e.RoundNumber,
        Label = e.Label, KeyEntities = e.KeyEntities,
        SharedRoomSnapshot = e.SharedRoomSnapshot, DurableSnapshot = e.DurableSnapshot,
        IsMajor = e.IsMajor, CreatedAt = e.CreatedAt,
    };

    public static SceneArchiveEntity ToEntity(SceneArchive d) => new()
    {
        RoomId = d.RoomId, RoundNumber = d.RoundNumber,
        Label = d.Label, KeyEntities = d.KeyEntities,
        SharedRoomSnapshot = d.SharedRoomSnapshot, DurableSnapshot = d.DurableSnapshot,
        IsMajor = d.IsMajor, CreatedAt = d.CreatedAt,
    };
    public static DataTrackerConfig ToDomain(DataTrackerEntity e) => new()
    {
        Id = e.Id,
        RoomId = e.RoomId,
        AgentId = e.AgentId,
        DataKey = e.DataKey,
        Value = e.Value,
        ValueType = e.ValueType,
        MinValue = e.MinValue,
        MaxValue = e.MaxValue,
        Enabled = e.Enabled,
        CreatedAt = e.CreatedAt,
        PromptText = e.PromptText,
        PrivilegedAgentPrompt = e.PrivilegedAgentPrompt
    };

    public static DataTrackerEntity ToEntity(DataTrackerConfig d) => new()
    {
        Id = d.Id,
        RoomId = d.RoomId,
        AgentId = d.AgentId,
        DataKey = d.DataKey,
        Value = d.Value,
        ValueType = d.ValueType,
        MinValue = d.MinValue,
        MaxValue = d.MaxValue,
        Enabled = d.Enabled,
        CreatedAt = d.CreatedAt,
        PromptText = d.PromptText,
        PrivilegedAgentPrompt = d.PrivilegedAgentPrompt
    };

    public static RoomInvite ToDomain(RoomInviteEntity e) => new()
    {
        Id = e.Id,
        RedeemedByUserId = e.RedeemedByUserId,
        RedeemedDate = e.RedeemedDate,
        CreatedDate = e.CreatedDate,
        ExpirationDate = e.ExpirationDate,
        HostUserId = e.HostUserId,
        RoomId = e.RoomId
    };

    public static RoomInviteEntity ToEntity(RoomInvite d) => new()
    {
        Id = d.Id,
        RedeemedByUserId = d.RedeemedByUserId,
        RedeemedDate = d.RedeemedDate,
        CreatedDate = d.CreatedDate,
        ExpirationDate = d.ExpirationDate,
        HostUserId = d.HostUserId,
        RoomId = d.RoomId
    };
}
