using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IRoomRepository
{
    Task<List<RoomConfig>> GetAllAsync(string userId);
    Task<List<RoomConfig>> GetInvitedToRooms(string userId);
    Task<RoomConfig?> GetAsync(string id, string userId);
    Task SaveAsync(RoomConfig room);
    Task DeleteAsync(string id, string userId);
    Task SeedRoom(string userId);
}
