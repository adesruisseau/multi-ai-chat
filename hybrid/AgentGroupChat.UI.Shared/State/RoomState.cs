using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Identity;

namespace AgentGroupChat.UI.Shared.State;

public sealed class RoomState
{
    private readonly IRoomRepository _roomRepo;
    private readonly IUserContext _userContext;
    public List<RoomConfig> Rooms { get; private set; } = new();
    public RoomConfig? SelectedRoom { get; private set; }

    public event Action? OnChange;

    public RoomState(IRoomRepository roomRepo, IUserContext userContext)
    {
        _roomRepo = roomRepo;
        _userContext = userContext;
    }

    public async Task LoadAsync()
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        Rooms = await _roomRepo.GetAllAsync(userId);
        if (SelectedRoom is not null)
            SelectedRoom = Rooms.FirstOrDefault(r => r.Id == SelectedRoom.Id);
        NotifyChanged();
    }

    public void SelectRoom(RoomConfig? room)
    {
        SelectedRoom = room;
        NotifyChanged();
    }

    public async Task SaveRoomAsync(RoomConfig room)
    {
        room.UserId = await _userContext.GetRequiredUserIdAsync();
        await _roomRepo.SaveAsync(room);
        await LoadAsync();
    }

    public async Task DeleteRoomAsync(string id)
    {
        await _roomRepo.DeleteAsync(id, await _userContext.GetRequiredUserIdAsync());
        if (SelectedRoom?.Id == id) SelectedRoom = null;
        await LoadAsync();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
