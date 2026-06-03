using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgentGroupChat.Infrastructure.Entities;

[Table("AppSettings")]
public class AppSettingsEntity
{
    [Key]
    public int Id { get; set; } = 1;
    public string UiTheme { get; set; } = "System";
    public string UiAccent { get; set; } = "Terracotta";
    public bool TtsEnabled { get; set; }
    public string TtsProvider { get; set; } = "Local";
    public string TtsVoice { get; set; } = string.Empty;
    public int TtsRate { get; set; }
    public string PiperExePath { get; set; } = string.Empty;
    public string PiperModelsDir { get; set; } = string.Empty;
    public string KokoroBaseUrl { get; set; } = "http://127.0.0.1:8880";
    public string KokoroModel { get; set; } = "kokoro";
    public string KokoroVoice { get; set; } = "af_heart";
    public string KokoroUserVoice { get; set; } = string.Empty;
    public string KokoroLangCode { get; set; } = "a";
    public double KokoroSpeed { get; set; } = 1.0;
    public bool SetupModelsCompleted { get; set; }
    public bool SetupRoomsCompleted { get; set; }
    public string SetupTtsStatus { get; set; } = "Pending";
    public bool HideSetupGuide { get; set; }
}

[Table("AiConnections")]
public class AiConnectionEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "OpenAI Compatible";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

[Table("AiModels")]
public class AiModelEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

[Table("ImageConnections")]
public class ImageConnectionEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "ComfyUI";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

[Table("ImageModels")]
public class ImageModelEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public int? Steps { get; set; }
    public double? GuidanceScale { get; set; }
    public string NegativePrompt { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

[Table("PromptSamples")]
public class PromptSampleEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public string ParentPromptSampleId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Table("Rooms")]
public class RoomEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public bool WaitForUserReply { get; set; } = true;
    public bool PauseAfterEveryReply { get; set; }
    public int AgentDelaySeconds { get; set; } = 5;
    public int MaxTokens { get; set; } = 300;
    public int RecentTurnsWindow { get; set; } = 6;
    public int UserCompactionBudget { get; set; } = 3200;
    public string SummarizerModelId { get; set; } = string.Empty;
    public string SummarizationLevel { get; set; } = "Moderate";
    public int SummarizerMaxTokens { get; set; } = 500;
    public int SummarizerMaxLines { get; set; } = 28;
    public int SummarizerMaxCharacters { get; set; } = 5600;
    public int SummarizerBroaderTurns { get; set; } = 6;
    public string SummarizerPromptOverride { get; set; } = string.Empty;
    public bool? TtsEnabledOverride { get; set; }
    public string TtsProviderOverride { get; set; } = string.Empty;
    public string TtsFallbackVoice { get; set; } = string.Empty;
    public string TtsUserVoice { get; set; } = string.Empty;
    public bool EnableSceneImageGeneration { get; set; }
    public bool UseCreativeImageGeneration { get; set; }
    public string SceneImageModelId { get; set; } = string.Empty;
    public string SceneImageStyleNotes { get; set; } = string.Empty;
    public string SceneImageNegativePrompt { get; set; } = string.Empty;
    public string MemoryModelId { get; set; } = string.Empty;
    public int MaxArchivedScenes { get; set; } = 40;
    public bool EnableSceneArchive { get; set; } = true;
    public bool EnablePrivilegedActions { get; set; }
    public bool EnableNpcSpawning { get; set; }
    public string PrivilegedAgentId { get; set; } = string.Empty;
    public string NpcModelId { get; set; } = string.Empty;
    public string NpcDefaultMaleVoice { get; set; } = string.Empty;
    public string NpcDefaultFemaleVoice { get; set; } = string.Empty;
    public int? NpcMaxTokens { get; set; }
    public int NpcCompactionBudget { get; set; } = 300;
    public string NpcBaseInstructions { get; set; } = string.Empty;
    public int MaxConcurrentNpcs { get; set; } = 2;
    public int SortOrder { get; set; }
    public List<AgentEntity> Agents { get; set; } = new();
    public List<HumanParticipantEntity> HumanParticipants { get; set; } = new();
}

[Table("HumanParticipants")]
public class HumanParticipantEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPlayerCharacter { get; set; }
    public string AppearanceSummary { get; set; } = string.Empty;
    public string TtsVoice { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#4A90D9";
    public string BackgroundHex { get; set; } = "#DDE8F0";
    public string ParticipationMode { get; set; } = "TurnParticipant";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public RoomEntity? Room { get; set; }
}

[Table("Agents")]
public class AgentEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int? MaxTokensOverride { get; set; }
    public int CompactionBudget { get; set; } = 420;
    public string AccentHex { get; set; } = "#C56A54";
    public string BackgroundHex { get; set; } = "#F9E5DE";
    public string TtsVoice { get; set; } = string.Empty;
    public string AppearanceSummary { get; set; } = string.Empty;
    public bool IsNpc { get; set; }
    public string SpawnedByAgentId { get; set; } = string.Empty;
    public bool IsTemporarilySuspended { get; set; }
    public string SuspendedByAgentId { get; set; } = string.Empty;
    public int? SuspendedUntilRound { get; set; }
    public string SuspensionReason { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsHumanParticipant { get; set; }
    public RoomEntity? Room { get; set; }
}

[Table("TranscriptTurns")]
public class TranscriptTurnEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int Round { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AccentHex { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

[Table("MemoryBlocks")]
public class MemoryBlockEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

[Table("LogEntries")]
public class LogEntryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public long? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

[Table("SceneArchives")]
public class SceneArchiveEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public string Label { get; set; } = string.Empty;
    public string KeyEntities { get; set; } = string.Empty;
    public string SharedRoomSnapshot { get; set; } = string.Empty;
    public string DurableSnapshot { get; set; } = string.Empty;
    public bool IsMajor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
