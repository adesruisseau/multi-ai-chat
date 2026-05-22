namespace AgentGroupChat.Core.Models.Domain;

public enum MemoryKind
{
    SharedRoom,
    Durable,
    AgentLong,
    AgentShort,
}

public sealed class MemoryBlock
{
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public MemoryKind Kind { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
