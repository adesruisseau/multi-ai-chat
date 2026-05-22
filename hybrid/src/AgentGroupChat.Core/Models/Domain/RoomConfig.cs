namespace AgentGroupChat.Core.Models.Domain;

public sealed class RoomConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public bool WaitForUserReply { get; set; } = true;
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
    public int SortOrder { get; set; }
    public List<AgentConfig> Agents { get; set; } = new();
}
