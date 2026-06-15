using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Identity;

namespace AgentGroupChat.UI.Shared.State;

public sealed class RoomState
{
    private readonly IRoomRepository _roomRepo;
    private readonly IUserContext _userContext;
    private readonly RoomRealtimeService _roomRealtimeService;

    public List<RoomConfig> Rooms { get; private set; } = new();
    public List<RoomConfig> InvitedToRooms { get; private set; } = new();
    public RoomConfig? SelectedRoom { get; private set; }

    public event Action? OnChange;

    public RoomState(IRoomRepository roomRepo, IUserContext userContext, RoomRealtimeService roomRealtimeService)
    {
        _roomRepo = roomRepo;
        _userContext = userContext;
        _roomRealtimeService = roomRealtimeService;
    }

    public async Task LoadAsync()
    {
        var userId = await _userContext.GetRequiredUserIdAsync();

        var ownedRooms = await _roomRepo.GetAllAsync(userId);
        var invitedRooms = await _roomRepo.GetInvitedToRooms(userId);

        Rooms = ownedRooms
            .Concat(invitedRooms)
            .GroupBy(room => room.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(room => room.SortOrder)
            .ToList();

        if (SelectedRoom is not null)
            SelectedRoom = Rooms.FirstOrDefault(r => r.Id == SelectedRoom.Id);

        NotifyChanged();
    }

    public void SelectRoom(RoomConfig? room)
    {
        SelectedRoom = room;
        NotifyChanged();
    }

    public async Task<RoomUpdateResult> SaveRoomAsync(RoomConfig room)
    {
        var currentUserId = await _userContext.GetRequiredUserIdAsync();
        if (string.IsNullOrWhiteSpace(room.UserId))
        {
            room.UserId = SelectedRoom?.UserId ?? currentUserId;
        }

        var roomUpdateResult = await _roomRealtimeService.UpdateRoomAsync(room);
        await LoadAsync();
        return roomUpdateResult;
    }

    public async Task<RoomDeleteResult> DeleteRoomAsync(string id)
    {
        var roomDeleteResult = await _roomRealtimeService.DeleteRoomAsync(id, await _userContext.GetRequiredUserIdAsync());
        if (SelectedRoom?.Id == id) SelectedRoom = null;
        await LoadAsync();
        return roomDeleteResult;
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
