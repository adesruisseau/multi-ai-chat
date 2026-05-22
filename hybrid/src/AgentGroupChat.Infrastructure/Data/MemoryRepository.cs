using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class MemoryRepository : IMemoryRepository
{
    private readonly AppDbContext _db;

    public MemoryRepository(AppDbContext db) => _db = db;

    public async Task<string> GetAsync(string roomId, string? agentId, MemoryKind kind)
    {
        var kindStr = kind.ToString();
        var entity = await _db.MemoryBlocks
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.AgentId == agentId && m.Kind == kindStr);
        return entity?.Content ?? string.Empty;
    }

    public async Task SaveAsync(string roomId, string? agentId, MemoryKind kind, string content)
    {
        var kindStr = kind.ToString();
        var entity = await _db.MemoryBlocks
            .FirstOrDefaultAsync(m => m.RoomId == roomId && m.AgentId == agentId && m.Kind == kindStr);

        if (entity is null)
        {
            _db.MemoryBlocks.Add(new MemoryBlockEntity
            {
                RoomId = roomId,
                AgentId = agentId,
                Kind = kindStr,
                Content = content,
                UpdatedAt = DateTimeOffset.Now,
            });
        }
        else
        {
            entity.Content = content;
            entity.UpdatedAt = DateTimeOffset.Now;
        }

        await _db.SaveChangesAsync();
    }

    public async Task ClearAllForRoomAsync(string roomId)
    {
        await _db.MemoryBlocks.Where(m => m.RoomId == roomId).ExecuteDeleteAsync();
    }
}
