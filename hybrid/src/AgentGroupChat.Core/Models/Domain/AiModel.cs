namespace AgentGroupChat.Core.Models.Domain;

public sealed class AiModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public decimal Temperature { get; set; } = 0.7m;
    public int MaxTokens { get; set; } = 512;

    public string UserId { get; set; } = string.Empty;
    /// <summary>Resolved at runtime from connection. Not persisted.</summary>
    public string ConnectionName { get; set; } = string.Empty;
    
}
