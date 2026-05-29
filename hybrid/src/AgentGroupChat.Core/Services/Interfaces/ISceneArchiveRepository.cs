using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ISceneArchiveRepository
{
    Task<IReadOnlyList<SceneArchive>> GetByRoomAsync(string roomId);
    Task<IReadOnlyList<SceneArchive>> GetByIdsAsync(IEnumerable<long> ids);
    Task AppendAsync(SceneArchive archive);
    Task PruneAsync(string roomId, int maxCount);
    Task DeleteAllForRoomAsync(string roomId);
}
