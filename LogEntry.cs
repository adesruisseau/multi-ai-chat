namespace AgentGroupChat;

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
    public LogEntry(
        LogCategory category,
        string source,
        string message,
        string? detail = null,
        long? durationMilliseconds = null)
    {
        Category = category;
        Source = source;
        Message = message;
        Detail = detail ?? string.Empty;
        DurationMilliseconds = durationMilliseconds;
        Timestamp = DateTimeOffset.Now;
        TimestampText = Timestamp.ToString("h:mm:ss tt");
    }

    public LogCategory Category { get; }

    public string Source { get; }

    public DateTimeOffset Timestamp { get; }

    public string TimestampText { get; }

    public string Message { get; }

    public string Detail { get; }

    public long? DurationMilliseconds { get; }

    public string CategoryText => Category.ToString();

    public string SourceText => string.IsNullOrWhiteSpace(Source) ? "General" : Source;

    public string DurationText => DurationMilliseconds is long value ? $"{value} ms" : string.Empty;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}