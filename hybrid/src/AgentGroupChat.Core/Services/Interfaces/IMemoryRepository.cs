using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IMemoryRepository
{
    Task<string> GetAsync(string roomId, string? agentId, MemoryKind kind);
    Task<IReadOnlyList<MemoryBlock>> GetBlocksAsync(string roomId);
    Task SaveAsync(string roomId, string? agentId, MemoryKind kind, string content);
    Task ClearAllForRoomAsync(string roomId);
}
