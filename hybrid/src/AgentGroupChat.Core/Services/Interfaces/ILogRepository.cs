using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ILogRepository
{
    Task AppendAsync(LogEntry entry);
    Task ClearAsync();
    Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit);
    Task<List<string>> GetSourcesAsync();
}
