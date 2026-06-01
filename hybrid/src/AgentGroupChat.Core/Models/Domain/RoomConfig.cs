namespace AgentGroupChat.Core.Models.Domain;

public sealed class RoomConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
    public List<AgentConfig> Agents { get; set; } = new();
    public List<HumanParticipantConfig> HumanParticipants { get; set; } = new();
}
