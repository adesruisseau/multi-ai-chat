using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IRoomRepository
{
    Task<List<RoomConfig>> GetAllAsync(string userId);
    Task<List<RoomConfig>> GetInvitedToRooms(string userId);
    Task<RoomConfig?> GetAsync(string id, string userId);
    Task<RoomUpdateResult> SaveAsync(RoomConfig room);
    Task<RoomDeleteResult> DeleteAsync(string id, string userId);
    Task SeedRoom(string userId);
}
