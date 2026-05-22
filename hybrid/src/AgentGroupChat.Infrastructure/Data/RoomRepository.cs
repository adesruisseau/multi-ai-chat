using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class RoomRepository : IRoomRepository
{
    private readonly AppDbContext _db;

    public RoomRepository(AppDbContext db) => _db = db;

    public async Task<List<RoomConfig>> GetAllAsync()
    {
        var entities = await _db.Rooms.Include(r => r.Agents)
            .OrderBy(r => r.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<RoomConfig?> GetAsync(string id)
    {
        var entity = await _db.Rooms.Include(r => r.Agents)
            .AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    public async Task SaveAsync(RoomConfig room)
    {
        var existing = await _db.Rooms.Include(r => r.Agents)
            .FirstOrDefaultAsync(r => r.Id == room.Id);

        if (existing is null)
        {
            _db.Rooms.Add(EntityMapper.ToEntity(room));
        }
        else
        {
            var entity = EntityMapper.ToEntity(room);
            _db.Entry(existing).CurrentValues.SetValues(entity);

            var existingAgentIds = existing.Agents.Select(a => a.Id).ToHashSet();
            var incomingAgentIds = room.Agents.Select(a => a.Id).ToHashSet();

            foreach (var removed in existing.Agents.Where(a => !incomingAgentIds.Contains(a.Id)).ToList())
                _db.Agents.Remove(removed);

            foreach (var agent in room.Agents)
            {
                var existingAgent = existing.Agents.FirstOrDefault(a => a.Id == agent.Id);
                if (existingAgent is null)
                    _db.Agents.Add(EntityMapper.ToEntity(agent));
                else
                    _db.Entry(existingAgent).CurrentValues.SetValues(EntityMapper.ToEntity(agent));
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var entity = await _db.Rooms.Include(r => r.Agents).FirstOrDefaultAsync(r => r.Id == id);
        if (entity is not null)
        {
            _db.Rooms.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}
