using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Services;

public sealed class LogService
{
    private readonly ILogRepository _logRepo;

    public LogService(ILogRepository logRepo) => _logRepo = logRepo;

    public event Action<LogEntry>? OnEntryAdded;

    public async Task LogAsync(LogCategory category, string source, string message, string detail = "", long? durationMs = null)
    {
        var entry = new LogEntry
        {
            Category = category,
            Source = source,
            Message = message,
            Detail = detail,
            DurationMs = durationMs,
        };
        await _logRepo.AppendAsync(entry);
        OnEntryAdded?.Invoke(entry);
    }

    public Task<List<LogEntry>> QueryAsync(string? category = null, string? source = null, int limit = 200)
        => _logRepo.QueryAsync(category, source, limit);

    public Task<List<string>> GetSourcesAsync()
        => _logRepo.GetSourcesAsync();
}
