namespace AgentGroupChat.Core.Models.Domain;

public sealed class TranscriptTurn
{
    public long Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int Round { get; set; }
    public string Speaker { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string ColorTheme { get; set; } = "Terracotta";
}
