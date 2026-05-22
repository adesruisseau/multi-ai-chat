using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AgentGroupChat;

public sealed class SessionLogStore
{
    private readonly object _sync = new();
    private readonly string _sessionLogPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public SessionLogStore()
    {
        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat",
            "logs");

        Directory.CreateDirectory(logsDirectory);
        _sessionLogPath = Path.Combine(logsDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
    }

    public string SessionLogPath => _sessionLogPath;

    public void Append(LogEntry entry)
    {
        var payload = new PersistedLogEntry(
            entry.Timestamp.ToString("O"),
            entry.CategoryText,
            entry.SourceText,
            entry.Message,
            entry.Detail,
            entry.DurationMilliseconds);

        var line = JsonSerializer.Serialize(payload, _serializerOptions);

        lock (_sync)
        {
            File.AppendAllText(_sessionLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private sealed record PersistedLogEntry(
        string Timestamp,
        string Category,
        string Source,
        string Message,
        string Detail,
        long? DurationMilliseconds);
}