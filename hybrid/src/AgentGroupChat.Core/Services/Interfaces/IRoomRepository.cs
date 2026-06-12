using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IRoomRepository
{
    Task<List<RoomConfig>> GetAllAsync();
    Task<RoomConfig?> GetAsync(string id);
    Task SaveAsync(RoomConfig room);
    Task DeleteAsync(string id);
    Task SeedRoom();
}
