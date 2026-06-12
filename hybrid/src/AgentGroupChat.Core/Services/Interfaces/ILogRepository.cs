using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ILogRepository
{
    Task AppendAsync(LogEntry entry);
    Task ClearAsync(string roomId);
    Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit, string roomId);
    Task<List<string>> GetSourcesAsync(string roomId);
}
