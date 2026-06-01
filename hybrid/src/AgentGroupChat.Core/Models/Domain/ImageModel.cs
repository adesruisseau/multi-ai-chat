namespace AgentGroupChat.Core.Models.Domain;

public sealed class ImageModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
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

    /// <summary>Resolved at runtime from connection. Not persisted.</summary>
    public string ConnectionName { get; set; } = string.Empty;
}