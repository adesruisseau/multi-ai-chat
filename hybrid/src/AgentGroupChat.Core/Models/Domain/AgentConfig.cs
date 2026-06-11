namespace AgentGroupChat.Core.Models.Domain;

public sealed class AgentConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
    public bool UseShortTermMemoryStorage { get; set; } = true;
    public bool UseLongTermMemoryStorage { get; set; } = true;
    public bool IsNpc { get; set; }
    public string SpawnedByAgentId { get; set; } = string.Empty;
    public bool IsTemporarilySuspended { get; set; }
    public string SuspendedByAgentId { get; set; } = string.Empty;
    public int? SuspendedUntilRound { get; set; }
    public string SuspensionReason { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsHumanParticipant { get; set; }
    public int PromptSampleId { get; set; } = 1;
}
