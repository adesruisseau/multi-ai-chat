using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Realtime
{
    public sealed record ChatLiveEvent(string RoomId, string Kind, TranscriptTurn? Turn = null);
}
