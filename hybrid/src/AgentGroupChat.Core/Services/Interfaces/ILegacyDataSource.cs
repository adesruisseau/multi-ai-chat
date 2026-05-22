using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ILegacyDataSource
{
    bool HasLegacyData();
    AppSettings LoadAppSettings();
    List<AiConnection> LoadConnections();
    List<AiModel> LoadModels();
    List<RoomConfig> LoadRooms();
    Dictionary<string, string> LoadRoomMemory(string roomId, string roomName);
    List<TranscriptTurn> LoadTranscript(string roomId, string roomName);
}
