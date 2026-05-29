using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class SceneArchiveRepository : ISceneArchiveRepository
{
    private readonly AppDbContext _db;

    public SceneArchiveRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SceneArchive>> GetByRoomAsync(string roomId)
    {
        var entities = await _db.SceneArchives
            .AsNoTracking()
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.RoundNumber)
            .ToListAsync();

        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SceneArchive>> GetByIdsAsync(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return [];

        var entities = await _db.SceneArchives
            .AsNoTracking()
            .Where(s => idList.Contains(s.Id))
            .ToListAsync();

        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task AppendAsync(SceneArchive archive)
    {
        _db.SceneArchives.Add(EntityMapper.ToEntity(archive));
        await _db.SaveChangesAsync();
    }

    public async Task PruneAsync(string roomId, int maxCount)
    {
        if (maxCount < 1) return;

        var total = await _db.SceneArchives.CountAsync(s => s.RoomId == roomId);
        if (total <= maxCount) return;

        var excess = total - maxCount;

        // Remove oldest minor snapshots first
        var minorToRemove = await _db.SceneArchives
            .Where(s => s.RoomId == roomId && !s.IsMajor)
            .OrderBy(s => s.CreatedAt)
            .Take(excess)
            .ToListAsync();

        _db.SceneArchives.RemoveRange(minorToRemove);
        excess -= minorToRemove.Count;

        // If still over cap, remove oldest major snapshots
        if (excess > 0)
        {
            var majorToRemove = await _db.SceneArchives
                .Where(s => s.RoomId == roomId && s.IsMajor)
                .OrderBy(s => s.CreatedAt)
                .Take(excess)
                .ToListAsync();

            _db.SceneArchives.RemoveRange(majorToRemove);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAllForRoomAsync(string roomId)
    {
        await _db.SceneArchives.Where(s => s.RoomId == roomId).ExecuteDeleteAsync();
    }
}
