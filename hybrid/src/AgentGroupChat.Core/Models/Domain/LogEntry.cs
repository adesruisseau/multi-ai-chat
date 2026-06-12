namespace AgentGroupChat.Core.Models.Domain;

public enum LogCategory
{
    System,
    Speech,
    Timing,
    Request,
    Response,
    Memory,
}

public sealed class LogEntry
{
    public long Id { get; set; }
    public LogCategory Category { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public long? DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public string CategoryText => Category.ToString();
    public string DurationText => DurationMs is long v ? $"{v} ms" : string.Empty;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public string RoomId { get; set; }
}
