using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Hybrid.State;

public sealed class RoomState
{
    private readonly IRoomRepository _roomRepo;
    public List<RoomConfig> Rooms { get; private set; } = new();
    public RoomConfig? SelectedRoom { get; private set; }

    public event Action? OnChange;

    public RoomState(IRoomRepository roomRepo) => _roomRepo = roomRepo;

    public async Task LoadAsync()
    {
        Rooms = await _roomRepo.GetAllAsync();
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
        await _roomRepo.SaveAsync(room);
        await LoadAsync();
    }

    public async Task DeleteRoomAsync(string id)
    {
        await _roomRepo.DeleteAsync(id);
        if (SelectedRoom?.Id == id) SelectedRoom = null;
        await LoadAsync();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
