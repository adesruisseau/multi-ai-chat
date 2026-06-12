namespace AgentGroupChat.Core.Models.Domain;

public sealed class PromptSample
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public string ParentPromptSampleId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UserId { get; set; } = string.Empty;
}