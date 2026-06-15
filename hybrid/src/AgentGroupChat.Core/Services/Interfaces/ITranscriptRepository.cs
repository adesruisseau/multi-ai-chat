using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ITranscriptRepository
{
    Task<List<TranscriptTurn>> GetAsync(string roomId);
    Task<ChatUpdateResult> AppendAsync(TranscriptTurn turn);
    Task<ChatUpdateResult> ClearAsync(string roomId);
}
