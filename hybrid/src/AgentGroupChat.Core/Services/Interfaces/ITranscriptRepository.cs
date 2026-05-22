using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ITranscriptRepository
{
    Task<List<TranscriptTurn>> GetAsync(string roomId);
    Task AppendAsync(TranscriptTurn turn);
    Task ClearAsync(string roomId);
}
