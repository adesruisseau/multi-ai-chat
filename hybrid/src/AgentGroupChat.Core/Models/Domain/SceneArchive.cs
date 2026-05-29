namespace AgentGroupChat.Core.Models.Domain;

public sealed class SceneArchive
{
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public string Label { get; set; } = string.Empty;
    public string KeyEntities { get; set; } = string.Empty;
    public string SharedRoomSnapshot { get; set; } = string.Empty;
    public string DurableSnapshot { get; set; } = string.Empty;
    public bool IsMajor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
